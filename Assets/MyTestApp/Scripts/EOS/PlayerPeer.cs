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
    const int HandshakeTimeoutMs = 6000;

    // Packet types
    private const byte PKT_Hb = 0;
    private const byte PKT_Seed = 1;
    private const byte PKT_SeedAck = 2;
    private const byte PKT_Input = 3;
    
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

    public PlayerPeer()
    {
        _socketId = new SocketId { SocketName = SOCKET_NAME };

        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

        sendPacketOptions = new SendPacketOptions
        {
            LocalUserId = EosCommonData.myPuid,
            RemoteUserId = remotePuid,//送信直前にも入れる
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.UnreliableUnordered,
            AllowDelayedDelivery = true,
        };

        receivePacketOptions = new ReceivePacketOptions
        {
            LocalUserId = EosCommonData.myPuid,//送信側が設定した受信可能なPUIDと照合するID（基本的には自分）
            MaxDataSizeBytes = (uint)_recvBuffer.Length,//今回の受信で許容するサイズ。これを超えるパケットは受け取れない
            RequestedChannel = null,//受信するメッセージの種類をフィルタリング。GGPOは送信情報を種類分けしないので設定しない。
        };

        for(int i = 0; i < inputDatasMaxSize; i++)
        {
            inputFrames_local[i] = -1;
            inputFrames_remote[i] = -1;
        }
    }

    public void Dispose()
    {
        CloseConnection();
    }

    public UniTask<bool> AcceptConnectionRequest(ProductUserId _remotePuid)
    {
        remotePuid = _remotePuid;

        //監視する接続要求を指定。ここでは自分あてに来るリクエストを指定
        var opt = new AddNotifyPeerConnectionRequestOptions()
        {
            LocalUserId = EosCommonData.myPuid,
            SocketId = _socketId,
        };

        var tcs = new UniTaskCompletionSource<bool>();

        //上記条件に合うリクエストが来た時のコールバックを購読。戻り値は購読解除するときに必要
        //AddNotifyPeerConnectionRequestは一度アクセプトするとクリーンアップするまで飛んでこない
        notifyPeerRequestId = p2pInterface.AddNotifyPeerConnectionRequest(ref opt, null,
            //受信したリクエストの詳細を専用の構造体を用意して取得
            (ref OnIncomingConnectionRequestInfo data) =>
            {
                if (data.RemoteUserId == null)tcs.TrySetResult(false);

                var a = Accept(data.RemoteUserId);
                tcs.TrySetResult(a);
            }
        );

        return tcs.Task;

        //リクエスト受け入れを許可
        bool Accept(ProductUserId puid)
        {
            //ここで指定したremotePuidとソケットIDがAddNotifyPeerConnectionRequestで接続済みと判定される
            var _opt = new AcceptConnectionOptions
            {
                LocalUserId = EosCommonData.myPuid,
                RemoteUserId = puid,
                SocketId = _socketId
            };

            var r = p2pInterface.AcceptConnection(ref _opt);
            return r == Result.Success || r == Result.AlreadyPending;
        }
    }

    HeartBeat heartBeat = new HeartBeat();

    public double HeartBeat()
    {
        heartBeat.TickHb();
        ReceivePump();

        return heartBeat.PingMs;
    }


    public async UniTask<bool> StartConnectToPeer(bool _isLobbyHost, CancellationToken token)
    {
        isLobbyHost = _isLobbyHost;
        if (isLobbyHost) _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        
        state = PeerState.SHARING_SEED;
        
        //ロビーデータ確認省略
        //メンバー人数確認省略
        //メンバー情報取得省略

        if (isLobbyHost)
        {
            return await OwnerLoop();
        }
        else
        {
            return await GuestLoop();
        }

        async UniTask<bool> OwnerLoop()
        {
            float timeOutClock = Time.time;
            float nextSendTime = Time.time;
            uint sendSeed = _seed;

            //シード値の共有
            while (!token.IsCancellationRequested)
            {
                //時間切れ
                if (Time.time - timeOutClock >= HandshakeTimeoutMs)
                {
                    state = PeerState.HANDSHAKE_TIME_OUT;
                    return false;
                }

                //仮インプットデータ送信
                if (Time.time >= nextSendTime)
                {
                    SendSeed(sendSeed);
                    nextSendTime = Time.time + 3f;
                }

                ReceivePump();

                if (recievedSeedAck)
                {
                    state = PeerState.SEED_SHARED;
                    break;
                }

                //これがないと1フレーム中にループしまくってフリーズ
                await UniTask.Yield();
            }

            return true;
        }

        async UniTask<bool> GuestLoop()
        {
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
                    break;
                }

                //これがないと1フレーム中にループしまくってフリーズ
                await UniTask.Yield();
            }

            return true;
        }
    }

    int lastAckFrame = -1;

    public void SendHB()
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        _sendBuffer_hb[0] = PKT_Hb;
        BitConverter.GetBytes(System.Diagnostics.Stopwatch.GetTimestamp()).CopyTo(_sendBuffer_hb, 1);

        sendPacketOptions.RemoteUserId = remotePuid;
        sendPacketOptions.Data = _sendBuffer_hb;

        p2pInterface.SendPacket(ref sendPacketOptions);
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

        p2pInterface.SendPacket(ref sendPacketOptions);
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
                break;

            //通信相手以外からのパケットをはじく
            if (outPeerId == null || outBytesWritten == 0)
                continue;
            if (outPeerId != remotePuid)
                continue;
            //受信データタイプを識別、タイプごとに処理を分岐
            byte packetType = _recvBuffer[0];

            switch (packetType)
            {
                default:
                    heartBeat.OnReceiveRaw(_recvBuffer);
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
    }

}

public sealed class HeartBeat
{
    // ===== HB設定 =====
    private const int HB_INTERVAL_MS = 200;      // HB送信間隔（レディ前/試合中どちらでも無難）
    private const int HB_TIMEOUT_MS = 1500;     // これ以上返らないHBは破棄（切断判定にも使える）

    // ===== HB状態 =====
    private uint _hbSeq = 0;
    private long _nextHbSendAtTs = 0;            // Stopwatch ticks
    private readonly Dictionary<uint, long> _hbPending = new(); // seq -> sendTimestamp
    private long _lastHbRecvTs = 0;              // 最後にHB系を受けた時刻（Stopwatch ticks）

    // 表示用（平滑化）
    public double PingMs { get; private set; } = -1;        // 表示用 ping（RTT/2）
    public double RttMs { get; private set; } = -1;        // 参考用 RTT
    private double _pingEmaMs = -1;                         // EMA内部

    // EMA係数（小さいほど滑らか）
    private const double PING_EMA_ALPHA = 0.15;

    // ===== 外部から呼ぶ想定 =====
    // 毎フレーム or 定期的に呼ばれる Update 的な場所から呼ぶ
    public void TickHb()
    {
        var nowTs = NowTs();

        // 未返信の掃除（溜まり続けないように）
        CleanupHbPending(nowTs);

        // 次の送信時刻になったら送る
        if (nowTs < _nextHbSendAtTs) return;

        SendHbPing(nowTs);

        // 次回の送信時刻を設定
        _nextHbSendAtTs = nowTs + MsToTs(HB_INTERVAL_MS);
    }

    // 生存判定が欲しいなら（任意）
    public bool IsAlive()
    {
        if (_lastHbRecvTs == 0) return true; // まだ一度も受けてない初期状態は生存扱い
        var nowTs = NowTs();
        var elapsedMs = TsToMs(nowTs - _lastHbRecvTs);
        return elapsedMs <= HB_TIMEOUT_MS;
    }

    // ===== 送受信 =====
    private void SendHbPing(long nowTs)
    {
        var seq = ++_hbSeq;

        // 送信時刻をpendingに保存（未返信ぶんだけ）
        _hbPending[seq] = nowTs;

        // payload: [MsgType][seq]
        var payload = NetMsg.PackHbPing(seq);
        SendRaw(payload);
    }

    // 受信入口（既存の受信処理から呼ぶ想定）
    public void OnReceiveRaw(byte[] payload)
    {
        var type = NetMsg.PeekType(payload);

        switch (type)
        {
            case NetMsg.MsgType.HbPing:
                {
                    var seq = NetMsg.UnpackHbPing(payload);

                    // 受け取った seq をそのまま返す（相手時刻は不要）
                    var pong = NetMsg.PackHbPong(seq);
                    SendRaw(pong);

                    _lastHbRecvTs = NowTs();
                    break;
                }

            case NetMsg.MsgType.HbPong:
                {
                    var seq = NetMsg.UnpackHbPong(payload);
                    OnHbPong(seq);
                    _lastHbRecvTs = NowTs();
                    break;
                }

            default:
                // 既存の他メッセージ処理へ
                OnReceiveOther(payload);
                break;
        }
    }

    private void OnHbPong(uint seq)
    {
        var nowTs = NowTs();

        if (!_hbPending.TryGetValue(seq, out var sendTs))
        {
            // もう掃除済み / 重複 / 順序ずれ等（無視でOK）
            return;
        }

        _hbPending.Remove(seq);

        var rttMs = TsToMs(nowTs - sendTs);
        var pingMs = rttMs * 0.5;

        RttMs = rttMs;

        // EMAで平滑化（最初の1回はそのまま採用）
        if (_pingEmaMs < 0) _pingEmaMs = pingMs;
        else _pingEmaMs = _pingEmaMs + (pingMs - _pingEmaMs) * PING_EMA_ALPHA;

        PingMs = _pingEmaMs;
    }

    private void CleanupHbPending(long nowTs)
    {
        if (_hbPending.Count == 0) return;

        // timeout超過したseqを消す（Dictionaryを直接foreachで消せないので一旦リスト化）
        _tmpRemove.Clear();

        foreach (var kv in _hbPending)
        {
            var elapsedMs = TsToMs(nowTs - kv.Value);
            if (elapsedMs > HB_TIMEOUT_MS) _tmpRemove.Add(kv.Key);
        }

        for (int i = 0; i < _tmpRemove.Count; i++)
        {
            _hbPending.Remove(_tmpRemove[i]);
        }

        // ここで「未返信が多すぎる」「一定回数連続でtimeout」などをカウントして
        // 切断扱いにしてもOK（設計次第）
    }

    private readonly List<uint> _tmpRemove = new();

    // ===== 時刻ユーティリティ（Stopwatch ticks） =====
    private static long NowTs() => Stopwatch.GetTimestamp();

    private static long MsToTs(int ms)
        => (long)(ms * (Stopwatch.Frequency / 1000.0));

    private static double TsToMs(long dtTs)
        => dtTs * 1000.0 / Stopwatch.Frequency;

    // ===== 既存想定メソッド（あなたの環境に合わせて置き換え） =====
    private void SendRaw(byte[] payload)
    {
        // TODO: 既存のP2P sendへ接続する
        // 例: _p2p.Send(peerPuid, payload);
    }

    private void OnReceiveOther(byte[] payload)
    {
        // TODO: 既存のメッセージ処理
    }
}

// ===== メッセージパック/アンパック（例） =====
public static class NetMsg
{
    public enum MsgType : byte
    {
        HbPing = 1,
        HbPong = 2,
        // ... 既存
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
