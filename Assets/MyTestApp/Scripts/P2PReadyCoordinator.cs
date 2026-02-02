using System;
using System.Linq;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.tvOS;

/// <summary>
/// 2人固定 + Ready方式：
/// Lobbyのmembers/READYを監視し、両者Ready成立でP2P handshake（Accept + Ping/Pong）を完了させる。
/// 成功したら OnConnected(remotePuid) を発火。中断条件が来たら Stop() で確実に後始末する。
/// </summary>
public sealed class P2PReadyCoordinator : IDisposable
{
    public enum State
    {
        Stopped,
        WaitingLobby,
        WaitingReady,
        Handshaking,
        Connected,
    }

    public event Action<State> OnStateChanged;
    public event Action<ProductUserId> OnConnected;
    public event Action<string> OnAborted;
    public event Action<string> OnError;

    // ====== 設定 ======
    public string SOCKET_NAME = "GAME"; // 2人固定ならこれ1つでOK
    public int PollIntervalMs = 200;

    public int HandshakeTimeoutMs = 6000; // Ping/Pongが成立するまでの猶予
    public int PingIntervalMs = 500;
    public int PingMaxTries = 10;

    // ====== 外部依存 ======
    private EOSLobbyManager _lobbyManager;            // あなたの LobbyService
    private Func<P2PInterface> _getP2P;            // P2PInterfaceの取得方法を注入（プロジェクト差分吸収）

    // ====== 内部状態 ======
    private CancellationTokenSource _cts;
    private State _state = State.Stopped;

    private ProductUserId _remotePuid;             // handshaking開始時に確定して固定
    private SocketId _socketId;

    private ulong _notifyPeerRequestId = 0;
    private bool _notifyRegistered = false;

    // 受信バッファ（適当に大きめ）
    private readonly byte[] _recvBuffer = new byte[4096];

    public State CurrentState => _state;

    public P2PReadyCoordinator(EOSLobbyManager lm)
    {
        _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        _lobbyManager = lm;
    }

    public void Start()
    {
        Stop(); // 多重起動防止
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

        // P2P後始末（notify解除/connection close）
        CleanupP2P();

        _remotePuid = null;
        SetState(State.Stopped);
    }

    public void Dispose()
    {
        Stop();
    }

    // ====== メイン監視ループ ======
    private async UniTaskVoid RunLoop(CancellationToken ct)
    {
        try
        {
            // notifyは「受け入れ準備」として先に常駐させる（取りこぼし防止）
            EnsureNotifyRegistered();

            while (!ct.IsCancellationRequested)
            {
                Lobby currentLobby = _lobbyManager.GetCurrentLobby();

                if (currentLobby == null)
                {
                    SetState(State.WaitingLobby);
                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    UnityEngine.Debug.Log("ロビー情報を取得中");
                    continue;
                }

                var members = currentLobby.Members;

                if (members == null || members.Count != 2)
                {
                    // 2人固定：成立しないならHandshake/Connectedは維持しない
                    if (_state == State.Handshaking || _state == State.Connected)
                    {
                        Abort("members.Count != 2");
                    }
                    else
                    {
                        SetState(State.WaitingReady);
                    }

                    UnityEngine.Debug.Log("対戦相手待ち受け中");
                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                // local/remote 判定（remoteは handshaking 開始時に固定する）
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

                // READY 判定（TryGetValue失敗は false 扱い）
                bool localReady = TryGetReady(localMember);
                bool remoteReady = TryGetReady(remoteMember);

                // 受信処理は常に回す（PINGへの応答など）
                PumpReceive(remoteMember.ProductId);

                if (!localReady || !remoteReady)
                {
                    if (_state == State.Handshaking || _state == State.Connected)
                        Abort("ready became false");
                    else
                        SetState(State.WaitingReady);


                    UnityEngine.Debug.Log("メンバーの準備完了待ち");

                    await UniTask.Delay(PollIntervalMs, cancellationToken: ct);
                    continue;
                }

                // Ready成立
                if (_state == State.WaitingReady || _state == State.WaitingLobby)
                {
                    _remotePuid = remoteMember.ProductId; // ここで確定
                    SetState(State.Handshaking);

                    UnityEngine.Debug.Log("握手");

                    bool ok = await DoHandshake(_remotePuid, ct);
                    if (ok)
                    {
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
            // Stopによるキャンセルは正常
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

            // MemberAttributes.TryGetValue が使える前提
            if (!lobbyMember.MemberAttributes.TryGetValue(LobbySceneManager.READY_KEY, out var att)) return false;

            // AsString が "1" なら ready
            string s = att.AsString;
            return s == "1";
        }
        catch
        {
            return false;
        }
    }

    // ====== P2P handshake ======
    private async UniTask<bool> DoHandshake(ProductUserId remote, CancellationToken ct)
    {
        EnsureNotifyRegistered();

        // Accept（両側で同時に呼んでOK）
        if (!Accept(remote))
            return false;

        // Ping/Pong 疎通確認
        int tries = 0;
        var start = Time.realtimeSinceStartup;

        while (!ct.IsCancellationRequested)
        {
            tries++;

            // PING送信
            SendText(remote, "PING:" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Debug.Log($"[P2P][PING->] to {remote}");

            // 受信を少し回して、PONG待ち
            var pong = await WaitForPong(remote, PingIntervalMs, ct);
            if (pong)
                return true;

            // timeout / tries
            float elapsedMs = (Time.realtimeSinceStartup - start) * 1000f;
            if (elapsedMs >= HandshakeTimeoutMs) return false;
            if (tries >= PingMaxTries) return false;
        }

        return false;
    }

    private bool WaitedPongFlag = false;

    private async UniTask<bool> WaitForPong(ProductUserId remote, int waitMs, CancellationToken ct)
    {
        WaitedPongFlag = false;

        float start = Time.realtimeSinceStartup;
        while (!ct.IsCancellationRequested)
        {
            PumpReceive(remote);

            if (WaitedPongFlag)
                return true;

            float elapsedMs = (Time.realtimeSinceStartup - start) * 1000f;
            if (elapsedMs >= waitMs)
                return false;

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return false;
    }

    // ====== P2P receive / respond ======
    private void PumpReceive(ProductUserId remote)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return;

        // 受信を可能な限り捌く（フレーム内で数回程度）
        for (int i = 0; i < 8; i++)
        {
            var receiveOptions = new ReceivePacketOptions
            {
                LocalUserId = LobbySceneManager.myPUID,
                MaxDataSizeBytes = (uint)_recvBuffer.Length,
                RequestedChannel = null, // チャンネル未指定
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

            // 相手以外は無視（2人固定）
            if (outPeerId != remote)
                continue;

            string msg = Encoding.UTF8.GetString(_recvBuffer, 0, (int)outBytesWritten);

            // 最小プロトコル：PINGにはPONGで返す / PONGを見たらフラグ
            if (msg.StartsWith("PING"))
            {
                SendText(remote, "PONG");
            }
            else if (msg.StartsWith("PONG"))
            {
                Debug.Log($"[P2P][<-PONG] from {remote} (Handshake OK)");
                WaitedPongFlag = true;
            }
        }
    }

    private bool SendText(ProductUserId remote, string text)
    {
        var p2p = _getP2P?.Invoke();
        if (p2p == null) return false;

        byte[] data = Encoding.UTF8.GetBytes(text);

        var sendOptions = new SendPacketOptions
        {
            LocalUserId = LobbySceneManager.myPUID,
            RemoteUserId = remote,
            SocketId = _socketId,
            Channel = 0,
            Data = data,
            //DataLengthBytes = (uint)data.Length,
            AllowDelayedDelivery = true,
            //DeliveryReliability = PacketReliability.ReliableUnordered, // 疎通確認はこれでOK
        };

        var r = p2p.SendPacket(ref sendOptions);
        return r == Result.Success;
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
        return r == Result.Success || r == Result.AlreadyPending; // Already系は環境で出るので許容
    }

    // ====== notify ======
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
            // ここで「来たらAccept」しておくとより堅い（相手の先行要求を取りこぼさない）
            if (data.RemoteUserId != null)
            {
                Accept(data.RemoteUserId);
            }
        });

        _notifyRegistered = _notifyPeerRequestId != 0;
    }

    // ====== cleanup ======
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
        OnStateChanged?.Invoke(_state);
    }
}
