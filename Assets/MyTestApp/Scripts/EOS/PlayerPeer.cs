using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;
using PlayEveryWare.EpicOnlineServices;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using UnityEngine.Windows;
using System.Diagnostics;
using PlasticGui.WorkspaceWindow.Replication;

public class PeerInputData
{
    public int frame;
    public int lastAckFrame;
    public bool input;

    public PeerInputData(int _frame, int _lastAckFrame, bool _input)
    {
        frame = _frame;
        lastAckFrame = _lastAckFrame;
        input = _input;
    }
}

public class PlayerPeer: IDisposable
{
    public enum PeerState
    {
        SLEEP,
        SHARING_SEED,
        SEED_SHARED,
        GAME_LOOP,

        HANDSHAKE_TIME_OUT,
    }

    public PeerState state { get; private set; } = PeerState.SLEEP;

    // ===== Settings =====
    const string SOCKET_NAME = "GAME";
    const int PollIntervalMs = 200;
    const int HandshakeTimeoutMs = 6;

    // Packet types

    private const byte PKT_Seed = 3;
    private const byte PKT_SeedAck = 4;
    private const byte PKT_Input = 5;
    
    P2PInterface p2pInterface;

    private SocketId _socketId;

    bool isLobbyHost = false;

    const int inputDatasMaxSize = 128;

    const int inputDatasMaxSize_musk = inputDatasMaxSize - 1;
    //ロールバック用なら長さを固定にする。リプレイ用に全履歴を残すなら別枠
    PeerInputData[] inputDatas_local = new PeerInputData[inputDatasMaxSize];
    PeerInputData[] inputDatas_remote = new PeerInputData[inputDatasMaxSize];

    int[] inputFrames_local = new int[inputDatasMaxSize];
    int[] inputFrames_remote = new int[inputDatasMaxSize];

    int pressedFrame_local = -1;
    int pressedFrame_remote = -1;

    //保存用ではなく、一時的な受け取りに使う。どんな形式でも共通で使うので長めに設定。
    private readonly byte[] _recvBuffer = new byte[4096];

    //送るときは長さを形式に合わせて固定。
    private readonly byte[] _sendBuffer_hb = new byte[5];
    private readonly byte[] _sendBuffer_input = new byte[10];
    private readonly byte[] _sendBuffer_seed = new byte[5];

    SendPacketOptions sendPacketOptions;
    ReceivePacketOptions receivePacketOptions;

    ProductUserId outPeerId;
    byte outChannel;
    uint outBytesWritten;

    uint _seed;
    ulong notifyPeerRequestId;
    bool recievedSeedAck = false;

    ProductUserId remotePuid;

    HeartbeatSession heartBeat;


    public PlayerPeer()
    {
        _socketId = new SocketId { SocketName = SOCKET_NAME };

        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

        sendPacketOptions = new SendPacketOptions
        {
            LocalUserId = EosCommonData.myPuid,
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.UnreliableUnordered,
            AllowDelayedDelivery = true,
        };

        receivePacketOptions = new ReceivePacketOptions
        {
            LocalUserId = EosCommonData.myPuid,//送信側が設定した受信可能なPUIDと照合するID（基本的には自分）
            //MaxDataSizeBytes = (uint)_recvBuffer.Length,//今回の受信で許容するサイズ。これを超えるパケットは受け取れない
            //RequestedChannel = null,//受信するメッセージの種類をフィルタリング。GGPOは送信情報を種類分けしないので設定しない。
        };

        for(int i = 0; i < inputDatasMaxSize; i++)
        {
            inputFrames_local[i] = -1;
            inputFrames_remote[i] = -1;
        }

        heartBeat = new(SendHB);
    }

    public void Dispose()
    {
        CloseConnection();
    }

    HashSet<ProductUserId> acceptConnection = new(); 

    public void RegisterConnectionRequestAccept(ProductUserId _remotePuid)
    {
        remotePuid = _remotePuid;

        //監視する接続要求を指定。ここでは自分あてに来るリクエストを指定
        var opt = new AddNotifyPeerConnectionRequestOptions()
        {
            LocalUserId = EosCommonData.myPuid,
            SocketId = _socketId,
        };

        UnityEngine.Debug.Log("リクエスト待ち受け開始");
    
        //上記条件に合うリクエストが来た時のコールバックを購読。戻り値は購読解除するときに必要
        //AddNotifyPeerConnectionRequestは一度アクセプトするとクリーンアップするまで飛んでこない
        notifyPeerRequestId = p2pInterface.AddNotifyPeerConnectionRequest(ref opt, null,
            //受信したリクエストの詳細を専用の構造体を用意して取得
            (ref OnIncomingConnectionRequestInfo data) =>
            {
                if (data.RemoteUserId == null)
                {
                    UnityEngine.Debug.Log("アクセプトできませんでした");
                }

                UnityEngine.Debug.Log("アクセプト成功");
                var a = Accept();
                acceptConnection.Add(remotePuid);
            }
        );

        //リクエスト受け入れを許可
        bool Accept()
        {
            //ここで指定したremotePuidとソケットIDがAddNotifyPeerConnectionRequestで接続済みと判定される
            var _opt = new AcceptConnectionOptions
            {
                LocalUserId = EosCommonData.myPuid,
                RemoteUserId = remotePuid,
                SocketId = _socketId
            };

            var r = p2pInterface.AcceptConnection(ref _opt);

            if(r == Result.Success) UnityEngine.Debug.Log("アクセプト成功2");

            return r == Result.Success || r == Result.AlreadyPending;
        }
    }

    public async UniTask<bool> SendSeedAnsWaitAck(CancellationToken token)
    {
        state = PeerState.SHARING_SEED;

        _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);

        float timeOutClock = Time.time;
        float nextSendTime = Time.time;
        uint sendSeed = _seed;

        //シード値の共有
        while (!token.IsCancellationRequested)
        {
            //時間切れ
            if (Time.time - timeOutClock >= HandshakeTimeoutMs)
            {
                UnityEngine.Debug.Log("握手タイムアウト");
                state = PeerState.HANDSHAKE_TIME_OUT;
                return false;
            }

            //仮インプットデータ送信
            if (Time.time >= nextSendTime)
            {
                UnityEngine.Debug.Log("シード送信");
                SendSeed(sendSeed);
                nextSendTime = Time.time + 3f;
            }

            ReceivePump();

            if (recievedSeedAck)
            {
                state = PeerState.SEED_SHARED;
                return true;
            }

            //これがないと1フレーム中にループしまくってフリーズ
            await UniTask.Yield();
        }

        return false;
    }



    public async UniTask<bool> WaitReceivingSeed(CancellationToken token)
    {
        state = PeerState.SHARING_SEED;

        float timeOutClock = Time.time;

        //シード値の共有
        while (!token.IsCancellationRequested)
        {
            //時間切れ
            if (Time.time - timeOutClock >= HandshakeTimeoutMs)
            {
                state = PeerState.HANDSHAKE_TIME_OUT;
                return false;
            }

            ReceivePump();

            if (recievedSeedAck)
            {
                state = PeerState.SEED_SHARED;
                return true;
            }

            //これがないと1フレーム中にループしまくってフリーズ
            await UniTask.Yield();
        }

        return false;
    }

    int lastAckFrame = -1;

    public double HbTick()
    {
        heartBeat.Tick();
        ReceivePump();
        return heartBeat.PingMs;
    }

    void SendHB(byte[] payload)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null)
        {
            UnityEngine.Debug.Log($"remotepuidがヌルだ");
            return;
        }

        sendPacketOptions.RemoteUserId = remotePuid;
        sendPacketOptions.Data = payload; // 渡されたpayloadをそのまま送る
        var r = p2pInterface.SendPacket(ref sendPacketOptions);
        UnityEngine.Debug.Log($"送信結果{r}");
    }

    public void SendInput(int frame, bool _input)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        if (pressedFrame_local == -1 && _input) pressedFrame_local = frame;

        _sendBuffer_input[0] = PKT_Input;
        BitConverter.GetBytes(frame).CopyTo(_sendBuffer_input, 1);//入力フレーム
        BitConverter.GetBytes(lastAckFrame).CopyTo(_sendBuffer_input, 5);//最後に受信した相手のフレーム
        _sendBuffer_input[9] = Convert.ToByte(_input);//入力内容

        sendPacketOptions.RemoteUserId = remotePuid;
        sendPacketOptions.Data = _sendBuffer_input;

        StoreLocalInput(frame, lastAckFrame, _input);

        p2pInterface.SendPacket(ref sendPacketOptions);
    }

    public void SendSeed(uint seed, bool ack = false)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        _sendBuffer_seed[0] = ack?PKT_SeedAck : PKT_Seed;
        BitConverter.GetBytes(seed).CopyTo(_sendBuffer_seed, 1);

        sendPacketOptions.RemoteUserId = remotePuid;
        sendPacketOptions.Data = _sendBuffer_seed;

        var r = p2pInterface.SendPacket(ref sendPacketOptions);
        UnityEngine.Debug.Log($"シードの送信結果：{r}");
    }

    public void ReceivePump()
    {
        if (p2pInterface == null) return;

        for (int i = 0; i < 8; i++)
        {
            //パケットの受信と開封
            var result = p2pInterface.ReceivePacket(
                ref receivePacketOptions,//上記を参照
                ref outPeerId, //送信元のPUID
                ref _socketId, //EOSにおけるポート番号的なもの
                out outChannel, //ポートのサブカテゴリ的なもので受信データをもう一段階細分化したいときに使う。soketで十分なのであんまり使わない
                _recvBuffer, //受信データの保存先変数
                out outBytesWritten//受信データのサイズ
                );

            //successではないなら、キュー内に条件に合うパケットがないので終了
            if (result != Result.Success)
            {
                UnityEngine.Debug.Log($"受信失敗{result}");
                break;
            }

            UnityEngine.Debug.Log($"何か来てる");

            //通信相手以外からのパケットをはじく
            if (outPeerId == null || outBytesWritten == 0)
            {
                UnityEngine.Debug.Log("送信元ブロック");
                continue;
            }
            if (outPeerId != remotePuid)
            {
                UnityEngine.Debug.Log("PUIDブロック");
                continue;
            }

            heartBeat.TryConsume(_recvBuffer);

            //受信データタイプを識別、タイプごとに処理を分岐
            byte packetType = _recvBuffer[0];

            switch (packetType)
            {
                case 1:

                    break;

                case PKT_Seed:
                    if (outBytesWritten < 5) continue;
                    UnPackSeedData(_recvBuffer);
                    break;

                case PKT_SeedAck:
                    if (outBytesWritten < 5) continue;
                    UnPackSeedAckData(_recvBuffer);
                    break;

                case PKT_Input:
                    if (outBytesWritten < 10) continue;
                    UnPackInputData(_recvBuffer);
                    break;
            }
        }
    }
    void UnPackSeedData(byte[] bytes)
    {
        if (recievedSeedAck) return;
        var tempSeed = BitConverter.ToUInt32(bytes, 1);

        if (!isLobbyHost)
        {
            recievedSeedAck = true;
            _seed = tempSeed;
            SendSeed(tempSeed, true);
        }
    }

    void UnPackSeedAckData(byte[] bytes)
    {
        if (recievedSeedAck) return;
        if (isLobbyHost) recievedSeedAck = true;
    }

    PeerInputData UnPackInputData(byte[] bytes)
    {
        int remoteCurrentFrame = BitConverter.ToInt32(bytes, 1);
        int remoteAckFrame = BitConverter.ToInt32(bytes, 5);
        bool remoteInput = Convert.ToBoolean(bytes[9]);

        PeerInputData inputData = new PeerInputData(remoteCurrentFrame, remoteAckFrame, remoteInput);

        StoreRemoteInput(remoteCurrentFrame, remoteAckFrame, remoteInput);

        if (pressedFrame_remote == -1 && remoteInput)
        {
            pressedFrame_remote = remoteCurrentFrame;
        }

        lastAckFrame = remoteCurrentFrame;

        return inputData;
    }

    private void StoreLocalInput(int frame, int lastAckFrame, bool input)
    {
        int index = frame & inputDatasMaxSize;
        inputDatas_local[index] = new PeerInputData(frame, lastAckFrame, input);
        inputFrames_local[index] = frame;
    }

    private void StoreRemoteInput(int frame, int lastAckFrame, bool input)
    {
        int index = frame & inputDatasMaxSize;
        inputDatas_remote[index] = new PeerInputData(frame, lastAckFrame, input);
        inputFrames_remote[index] = frame;
    }

    public bool TryGetLocalInput(int frame, out PeerInputData data)
    {
        int index = frame & inputDatasMaxSize;
        if (inputFrames_local[index] == frame)
        {
            data = inputDatas_local[index];
            return true;
        }

        data = null;
        return false;
    }

    public bool TryGetRemoteInput(int frame, out PeerInputData data)
    {
        int index = frame & inputDatasMaxSize;
        if (inputFrames_remote[index] == frame)
        {
            data = inputDatas_remote[index];
            return true;
        }

        data = null;
        return false;
    }

    public void ClearInputData()
    {
        for (int i = 0; i < inputDatasMaxSize; i++)
        {
            inputFrames_local[i] = -1;
            inputFrames_remote[i] = -1;
            inputDatas_local[i] = null;
            inputDatas_remote[i] = null;
        }

        pressedFrame_local = -1;
        pressedFrame_remote = -1;
    }

    public void CloseConnection()
    {
        ClearInputData();

        if (p2pInterface != null)
        {
            if (notifyPeerRequestId != 0)
            {
                p2pInterface.RemoveNotifyPeerConnectionRequest(notifyPeerRequestId);
            }

            if (remotePuid != null)
            {
                var close = new CloseConnectionOptions
                {
                    LocalUserId = EosCommonData.myPuid,
                    RemoteUserId = remotePuid,
                    SocketId = _socketId
                };
                p2pInterface.CloseConnection(ref close);
            }
        }

        notifyPeerRequestId = 0;
        remotePuid = null;

        recievedSeedAck = false;

        state = PeerState.SLEEP;

        acceptConnection.Clear();
    }

}

public sealed class HeartbeatSession
{
    // ===== 設定 =====
    private readonly int _intervalMs;
    private readonly int _timeoutMs;
    private readonly double _emaAlpha;

    // ===== 外部依存（PlayerPeerから注入） =====
    private readonly Func<long> _nowTs;                 // Stopwatch ticks を返す
    private readonly Action<byte[]> _send;              // 実際の送信（PlayerPeerのSendに接続）

    // ===== 状態 =====
    private uint _seq = 0;
    private long _nextSendAtTs = 0;
    private long _lastRecvAtTs = 0;

    // 未返信ぶんだけ保持
    private readonly Dictionary<uint, long> _pending = new();
    private readonly List<uint> _tmpRemove = new();

    // 計測値（外部に見せる）
    public double PingMs { get; private set; } = -1;
    public double RttMs { get; private set; } = -1;

    private double _pingEmaMs = -1;

    public HeartbeatSession(
        Action<byte[]> send,
        int intervalMs = 200,
        int timeoutMs = 1500,
        double emaAlpha = 0.15)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _intervalMs = intervalMs;
        _timeoutMs = timeoutMs;
        _emaAlpha = emaAlpha;

        // 時刻源はStopwatch固定（ロールバックやTime.timeの影響を避ける）
        _nowTs = Stopwatch.GetTimestamp;
    }

    /// <summary>
    /// PlayerPeerのUpdateなどから毎フレーム呼ぶ
    /// </summary>
    public void Tick()
    {
        var nowTs = _nowTs();

        CleanupPending(nowTs);

        if (nowTs < _nextSendAtTs) return;
        UnityEngine.Debug.Log("ピンを送った");
        SendPing(nowTs);
        _nextSendAtTs = nowTs + MsToTs(_intervalMs);
    }

    /// <summary>
    /// 受信パケットがHBなら消費してtrueを返す（PlayerPeer側で以降の処理を止められる）
    /// </summary>
    public bool TryConsume(byte[] payload)
    {
        var type = NetMsg.PeekType(payload);

        switch (type)
        {
            case NetMsg.MsgType.HbPing:
                {
                    var seq = NetMsg.UnpackHbPing(payload);

                    // 受け取ったseqをそのまま返す（相手の時刻は不要）
                    var pong = NetMsg.PackHbPong(seq);
                    _send(pong);

                    _lastRecvAtTs = _nowTs();
                    return true;
                }

            case NetMsg.MsgType.HbPong:
                {
                    var seq = NetMsg.UnpackHbPong(payload);
                    OnPong(seq);

                    _lastRecvAtTs = _nowTs();
                    return true;
                }

            default:
                return false;
        }
    }

    public bool IsAlive()
    {
        if (_lastRecvAtTs == 0) return true; // 初期は生存扱い
        var elapsedMs = TsToMs(_nowTs() - _lastRecvAtTs);
        return elapsedMs <= _timeoutMs;
    }

    // ===== 内部処理 =====
    private void SendPing(long nowTs)
    {
        var seq = ++_seq;

        // 未返信ぶんだけ保存
        _pending[seq] = nowTs;

        var ping = NetMsg.PackHbPing(seq);
        _send(ping);
    }

    private void OnPong(uint seq)
    {
        var nowTs = _nowTs();

        if (!_pending.TryGetValue(seq, out var sentTs))
        {
            // timeoutで掃除済み / 重複など
            return;
        }

        _pending.Remove(seq);

        var rttMs = TsToMs(nowTs - sentTs);
        var pingMs = rttMs * 0.5;

        RttMs = rttMs;

        // 表示のブレを抑える（EMA）
        if (_pingEmaMs < 0) _pingEmaMs = pingMs;
        else _pingEmaMs = _pingEmaMs + (pingMs - _pingEmaMs) * _emaAlpha;

        PingMs = _pingEmaMs;
    }

    private void CleanupPending(long nowTs)
    {
        if (_pending.Count == 0) return;

        _tmpRemove.Clear();

        foreach (var kv in _pending)
        {
            var elapsedMs = TsToMs(nowTs - kv.Value);
            if (elapsedMs > _timeoutMs) _tmpRemove.Add(kv.Key);
        }

        for (int i = 0; i < _tmpRemove.Count; i++)
        {
            _pending.Remove(_tmpRemove[i]);
        }
    }

    // ===== ticks/ms変換 =====
    private static long MsToTs(int ms)
        => (long)(ms * (Stopwatch.Frequency / 1000.0));

    private static double TsToMs(long dtTs)
        => dtTs * 1000.0 / Stopwatch.Frequency;
}

public static class NetMsg
{
    public enum MsgType : byte
    {
        HbPing = 1,
        HbPong = 2,
        // ...その他
    }

    public static MsgType PeekType(byte[] p) => (MsgType)p[0];

    public static byte[] PackHbPing(uint seq)
    {
        var b = new byte[1 + 4];
        b[0] = (byte)MsgType.HbPing;
        WriteU32(b, 1, seq);
        return b;
    }

    public static byte[] PackHbPong(uint seq)
    {
        var b = new byte[1 + 4];
        b[0] = (byte)MsgType.HbPong;
        WriteU32(b, 1, seq);
        return b;
    }

    public static uint UnpackHbPing(byte[] p) => ReadU32(p, 1);
    public static uint UnpackHbPong(byte[] p) => ReadU32(p, 1);

    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o + 0] = (byte)(v);
        b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16);
        b[o + 3] = (byte)(v >> 24);
    }

    private static uint ReadU32(byte[] b, int o)
        => (uint)(b[o + 0] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
}
