using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using UnityEngine;

/// <summary>
/// 2-player fixed + Ready:
/// - Watch lobby members/READY
/// - When both ready, perform P2P handshake
/// - Handshake is unified: Input(frame=0) -> Ack(frame=0)
///   (No separate Ping/Pong and no extra 1-frame test afterwards)
/// </summary>
public sealed class P2PConnector : IDisposable
{
    public enum State
    {
        Stopped,
        WaitingLobby,
        WaitingReady,
        Handshaking,
        Connected,
    }

    public event Action<ProductUserId> OnConnected;
    public event Action<string> OnAborted;
    public event Action<string> OnError;

    // ===== Settings =====
    public string SOCKET_NAME = "GAME";
    public int PollIntervalMs = 200;
    public int HandshakeTimeoutMs = 6000;

    // Handshake payload (same format you will use for real input packets)
    private const int HANDSHAKE_FRAME = 0;
    private const ushort HANDSHAKE_INPUT_BITS = 0x0001;

    // Packet types
    private const byte PKT_INPUT = 0;
    private const byte PKT_ACK = 1;

    // ===== External deps =====
    private readonly EOSLobbyManager _lobbyManager;
    private readonly Func<P2PInterface> _getP2P;

    // ===== Internal state =====
    private CancellationTokenSource _cts;
    private State _state = State.Stopped;

    private ProductUserId _remotePuid;
    private SocketId _socketId;

    private ulong _notifyPeerRequestId;
    private bool _notifyRegistered;

    private volatile bool _handshakeAcked;
    private readonly byte[] _recvBuffer = new byte[4096];

    public State CurrentState => _state;

    public P2PConnector(EOSLobbyManager lobbyManager)
    {
        _lobbyManager = lobbyManager;
        _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
    }

    public void Start()
    {
        Stop();
        _socketId = new SocketId { SocketName = SOCKET_NAME };
        _cts = new CancellationTokenSource();

        SetState(State.WaitingLobby);
        RunLoop(_cts.Token).Forget();
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        CleanupP2P();
        _remotePuid = null;
        SetState(State.Stopped);
    }

    public void Dispose() => Stop();

    private async UniTaskVoid RunLoop(CancellationToken ct)
    {
        try
        {
            EnsureNotifyRegistered();

            while (!ct.IsCancellationRequested)
            {
                Lobby currentLobby = _lobbyManager.GetCurrentLobby();
                if (currentLobby == null)
                {
                    SetState(State.WaitingLobby);
                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                var members = currentLobby.Members;
                if (members == null || members.Count != 2)
                {
                    if (_state == State.Handshaking || _state == State.Connected)
                        Abort("members.Count != 2");
                    else
                        SetState(State.WaitingReady);

                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                var localMember = members.FirstOrDefault(m => m.ProductId == LobbySceneManager.myPUID);
                var remoteMember = members.FirstOrDefault(m => m.ProductId != LobbySceneManager.myPUID);

                if (localMember == null || remoteMember == null)
                {
                    if (_state == State.Handshaking || _state == State.Connected)
                        Abort("localMember or remoteMember missing");
                    else
                        SetState(State.WaitingReady);

                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                bool localReady = TryGetReady(localMember);
                bool remoteReady = TryGetReady(remoteMember);

                // Always pump receive (to respond to incoming handshake packets)
                PumpReceive(remoteMember.ProductId);

                if (!localReady || !remoteReady)
                {
                    if (_state == State.Handshaking || _state == State.Connected)
                        Abort("ready became false");
                    else
                        SetState(State.WaitingReady);

                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                //相手プレイヤー発見
                if (_state == State.WaitingReady || _state == State.WaitingLobby)
                {
                    _remotePuid = remoteMember.ProductId;

                    //ｐ２ｐ接続開始
                    SetState(State.Handshaking);

                    bool ok = await DoHandshakeUnified(_remotePuid, ct);

                    if (ok)
                    {
                        //接続完了
                        SetState(State.Connected);
                        Debug.Log($"[P2P] Connected with {_remotePuid}");
                        OnConnected?.Invoke(_remotePuid);
                    }
                    else
                    {
                        Abort("handshake failed");
                    }
                }

                await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex.ToString());
            Abort("exception");
        }
    }

    private bool TryGetReady(LobbyMember lobbyMember)
    {
        try
        {
            if (lobbyMember.MemberAttributes == null) return false;
            if (!lobbyMember.MemberAttributes.TryGetValue(LobbySceneManager.READY_KEY, out var att)) return false;
            return att.AsString == "1";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unified handshake:
    /// - AcceptConnection
    /// - Send INPUT(frame=0, inputBits=const)
    /// - Wait until ACK(frame=0) is received
    /// </summary>
    private async UniTask<bool> DoHandshakeUnified(ProductUserId remote, CancellationToken ct)
    {
        EnsureNotifyRegistered();

        if (!Accept(remote))
            return false;

        _handshakeAcked = false;

        // Send the same packet format you will use in real gameplay.
        // Receiver responds with ACK(frame=0).
        SendInput(remote, HANDSHAKE_FRAME, HANDSHAKE_INPUT_BITS);
        Debug.Log($"[P2P][HS->] INPUT frame={HANDSHAKE_FRAME} bits={HANDSHAKE_INPUT_BITS} to {remote}");

        float start = Time.realtimeSinceStartup;
        while (!ct.IsCancellationRequested)
        {
            PumpReceive(remote);

            if (_handshakeAcked)
                return true;

            float elapsedMs = (Time.realtimeSinceStartup - start) * 1000f;
            if (elapsedMs >= HandshakeTimeoutMs)
                return false;

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        return false;
    }

    // ===== P2P receive / respond =====
    private void PumpReceive(ProductUserId remote)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return;

        for (int i = 0; i < 8; i++)
        {
            var receiveOptions = new ReceivePacketOptions
            {
                LocalUserId = LobbySceneManager.myPUID,
                MaxDataSizeBytes = (uint)_recvBuffer.Length,
                RequestedChannel = null,
            };

            var socketId = _socketId;
            var outPeerId = default(ProductUserId);
            var outChannel = default(byte);
            uint outBytesWritten = 0;

            var result = p2p.ReceivePacket(ref receiveOptions, ref outPeerId, ref socketId, out outChannel, _recvBuffer, out outBytesWritten);
            if (result != Result.Success)
                break;

            if (outPeerId == null || outBytesWritten == 0)
                continue;

            if (outPeerId != remote)
                continue;

            // Packet format:
            // [0]   byte  type (0=input, 1=ack)
            // [1-4] int   frame
            // [5-6] ushort input (only for input)
            byte type = _recvBuffer[0];
            if (outBytesWritten < 5) continue;

            int frame = BitConverter.ToInt32(_recvBuffer, 1);

            if (type == PKT_INPUT)
            {
                if (outBytesWritten < 7) continue;
                ushort inputBits = BitConverter.ToUInt16(_recvBuffer, 5);
                Debug.Log($"[P2P][<-HS] INPUT frame={frame} bits={inputBits} from {remote}");

                // Always ACK back for the received frame.
                SendAck(remote, frame);
            }
            else if (type == PKT_ACK)
            {
                Debug.Log($"[P2P][<-HS] ACK frame={frame} from {remote}");
                if (frame == HANDSHAKE_FRAME)
                    _handshakeAcked = true;
            }
        }
    }

    private void SendInput(ProductUserId remote, int frame, ushort inputBits)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return;

        byte[] buf = new byte[7];
        buf[0] = PKT_INPUT;
        BitConverter.GetBytes(frame).CopyTo(buf, 1);
        BitConverter.GetBytes(inputBits).CopyTo(buf, 5);

        var opt = new SendPacketOptions
        {
            LocalUserId = LobbySceneManager.myPUID,
            RemoteUserId = remote,
            SocketId = _socketId,
            Channel = 0,
            Data = buf,
            //DataLengthBytes = (uint)buf.Length,
            Reliability = PacketReliability.ReliableUnordered,
            AllowDelayedDelivery = true,
        };

        p2p.SendPacket(ref opt);
    }

    private void SendAck(ProductUserId remote, int frame)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return;

        byte[] buf = new byte[5];
        buf[0] = PKT_ACK;
        BitConverter.GetBytes(frame).CopyTo(buf, 1);

        var opt = new SendPacketOptions
        {
            LocalUserId = LobbySceneManager.myPUID,
            RemoteUserId = remote,
            SocketId = _socketId,
            Channel = 0,
            Data = buf,
            //DataLengthBytes = (uint)buf.Length,
            Reliability = PacketReliability.ReliableUnordered,
            AllowDelayedDelivery = true,
        };

        p2p.SendPacket(ref opt);
    }

    private bool Accept(ProductUserId remote)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return false;

        var opt = new AcceptConnectionOptions
        {
            LocalUserId = LobbySceneManager.myPUID,
            RemoteUserId = remote,
            SocketId = _socketId
        };

        var r = p2p.AcceptConnection(ref opt);
        return r == Result.Success || r == Result.AlreadyPending;
    }

    // ===== notify =====
    private void EnsureNotifyRegistered()
    {
        if (_notifyRegistered) return;

        var p2p = _getP2P?.Invoke();
        if (p2p == null) return;

        var opt = new AddNotifyPeerConnectionRequestOptions
        {
            LocalUserId = LobbySceneManager.myPUID,
            SocketId = _socketId
        };

        _notifyPeerRequestId = p2p.AddNotifyPeerConnectionRequest(ref opt, null, (ref OnIncomingConnectionRequestInfo data) =>
        {
            if (data.RemoteUserId != null)
            {
                // Be permissive: accept incoming requests immediately.
                Accept(data.RemoteUserId);
            }
        });

        _notifyRegistered = _notifyPeerRequestId != 0;
    }

    // ===== cleanup =====
    private void CleanupP2P()
    {
        var p2p = _getP2P?.Invoke();
        if (p2p != null)
        {
            if (_notifyRegistered && _notifyPeerRequestId != 0)
            {
                p2p.RemoveNotifyPeerConnectionRequest(_notifyPeerRequestId);
            }

            if (_remotePuid != null)
            {
                var close = new CloseConnectionOptions
                {
                    LocalUserId = LobbySceneManager.myPUID,
                    RemoteUserId = _remotePuid,
                    SocketId = _socketId
                };
                p2p.CloseConnection(ref close);
            }
        }

        _notifyPeerRequestId = 0;
        _notifyRegistered = false;
        _handshakeAcked = false;
    }

    private void Abort(string reason)
    {
        CleanupP2P();
        _remotePuid = null;
        SetState(State.WaitingReady);
        OnAborted?.Invoke(reason);
    }

    private void SetState(State s)
    {
        if (_state == s) return;
        _state = s;
    }
}
