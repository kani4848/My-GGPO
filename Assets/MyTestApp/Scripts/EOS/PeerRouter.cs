using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using UnityEngine;
using static PlayerPeer;
using System;
using Unity.VisualScripting.YamlDotNet.Serialization;
using PlayEveryWare.EpicOnlineServices;
using PlasticPipe.PlasticProtocol.Messages;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

public enum PacketType : byte
{
    None = 0,

    //大量に送るものはack不要
    HbPing = 1,
    HbPong = 2,

    Input = 3,
    
    //足並みをそろえるタイミングに送るものはackを用意
    Input_Result = 6,
    Input_Result_Ack = 7,

    RoundData = 8,
    RoundData_Ack = 9,
}

public class PeerRouter
{
    //パケット送受信用の入れ物。高回転なのでリストだと遅いため配列を使用する。

    //識別子（上限１０）＋ラウンド数（上限10）＋シグナル（4バイト）+タイムアップフレーム、 ackと兼用
    const int BufferSize_roundData = 1 + 1 + 4 + 4;
    const int inputHitorySize = 6;
    const int BufferSize_input = 1 + 4 + 1 + 1 * inputHitorySize;
    const int BufferSize_inputResult = 1 + 4;
    private readonly byte[] _sendBuffer_input = new byte[BufferSize_input];//過去数フレーム分のインプット履歴も追加
    private readonly byte[] _sendBuffer_InputResult = new byte[BufferSize_inputResult];
    private readonly byte[] _sendBuffer_roundData = new byte[BufferSize_roundData];
    private readonly byte[] _recvBuffer = new byte[4096];//一時的な受け取りに使う。どんな形式でも共通で使うので長めに設定。
    const int ringBuffer_MaxSize = 32;
    const int ringBuffer_MaxSize_musk = ringBuffer_MaxSize - 1;
    PeerInputData[] ringBuffer_input_local = new PeerInputData[ringBuffer_MaxSize];
    PeerInputData[] ringBuffer_input_remote = new PeerInputData[ringBuffer_MaxSize];

    //パケット設定回り
    P2PInterface p2pInterface;
    private SocketId _socketId;
    SendPacketOptions sendPacketOptions_Unreliable;
    SendPacketOptions sendPacketOptions_Reliable;
    ReceivePacketOptions receivePacketOptions;
    ProductUserId outPeerId;
    byte outChannel;
    uint outBytesWritten;
    const string SOCKET_NAME = "game";
    

    ProductUserId remotePuid;
    HashSet<ProductUserId> acceptConnection = new();
    ulong notifyPeerRequestId;

    //ピンポン
    HeartbeatSession heartBeat;
    bool gotPong = false;
    bool gotPing = false;
    double pingMs;

    public PeerRouter()
    {
        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

        _socketId = new SocketId { SocketName = SOCKET_NAME };

        sendPacketOptions_Unreliable = new SendPacketOptions
        {
            LocalUserId = EosCommonData.myPuid,
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.UnreliableUnordered,
            AllowDelayedDelivery = true,
        };

        sendPacketOptions_Reliable = new SendPacketOptions
        {
            LocalUserId = EosCommonData.myPuid,
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.ReliableOrdered,
            AllowDelayedDelivery = true,
        };

        receivePacketOptions = new ReceivePacketOptions
        {
            LocalUserId = EosCommonData.myPuid,//送信側が設定した受信可能なPUIDと照合するID（基本的には自分）
            MaxDataSizeBytes = (uint)_recvBuffer.Length,//今回の受信で許容するサイズ。これを超えるパケットは受け取れない
            RequestedChannel = null,//受信するメッセージの種類をフィルタリング。GGPOは送信情報を種類分けしないので設定しない。
        };

        ClearRingBuffer();
        heartBeat = new(SendHB);
    }

    //インフラ=======================================================

    public void Tick()
    {
        ReceivePump();
        heartBeat.Tick();
    }

    void ReceivePump()
    {
        if (p2pInterface == null) return;

        try
        {
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

                if (result != Result.Success) break;//successではないなら、キュー内に条件に合うパケットがないので終了
                if (outPeerId == null) continue;
                if (outPeerId != remotePuid) continue;

                //受信データタイプを識別、タイプごとに処理を分岐
                PacketType pt = (PacketType)_recvBuffer[0];

                UnityEngine.Debug.Log($"パケット受信:{pt}");

                switch (pt)
                {
                    case PacketType.HbPing:
                        gotPing = true;
                        heartBeat.TryPing(_recvBuffer);
                        break;

                    case PacketType.HbPong:
                        gotPong = true;
                        heartBeat.TryPong(_recvBuffer);
                        break;

                    case PacketType.Input:
                        if (outBytesWritten < BufferSize_input) continue;
                        gotInput = true;
                        UnPackInput(_recvBuffer);
                        break;

                    case PacketType.Input_Result:
                        if (outBytesWritten < BufferSize_inputResult) continue;
                        gotInputResult_remote = true;
                        UnPackInputResult(_recvBuffer);
                        break;

                    case PacketType.Input_Result_Ack:
                        gotInputResultAck = true;
                        break;

                    case PacketType.RoundData_Ack:
                    case PacketType.RoundData:
                        if (outBytesWritten < BufferSize_roundData) continue;
                        UnPackRoundData(_recvBuffer);
                        break;
                }
            }
        }
        finally
        {
        }
    }

    void SendHB(byte[] p)
    {
        if (p2pInterface == null)
        {
            UnityEngine.Debug.Log($"p2pがnull");
            return;
        }
        if (remotePuid == null)
        {
            UnityEngine.Debug.Log($"puidがnull");
            return;
        }

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = p;
        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);
    }
    public double GetPingMs()
    {
        return pingMs;
    }

    void ClearRingBuffer()
    {
        for (int i = 0; i < ringBuffer_MaxSize; i++)
        {
            ringBuffer_input_local[i] = new PeerInputData();
            ringBuffer_input_remote[i] = new PeerInputData();
        }
    }

    public void ResetBuffers()
    {
        ClearInputBuffer();
        ClearRoundData();
        ClearRingBuffer();
    }

    //P2P接続確率=======================================================

    public async UniTask<bool> RegisterConnectionRequestAccept(ProductUserId _remotePuid, CancellationToken token)
    {
        //既に接続済みなら終了
        if (acceptConnection.Contains(_remotePuid))
        {
            UnityEngine.Debug.Log("既に接続済みです");
            return true;
        }

        // 入力チェック（ProductUserIdはnullよりIsValidが本命なことが多い）
        if (_remotePuid == null || !_remotePuid.IsValid())
        {
            UnityEngine.Debug.Log("PUIDが無効です");
            return false;
        }

        //通知を登録するが別ルートで自動アクセプトされることがあり、その場合はここの通知をすり抜けることがある。
        //そのため別軸でピンポンできていれば成立とみなす処理も走らせる

        var connectTask = new UniTaskCompletionSource<bool>();

        token.Register(() => connectTask.TrySetResult(false));

        // キャンセル登録（後でDisposeする）
        var ctr = token.Register(() => connectTask.TrySetResult(false));

        notifyPeerRequestId = 0;

        try
        {
            remotePuid = _remotePuid;

            UnityEngine.Debug.Log("p2p接続リクエスト許可");

            var opt_request = new AddNotifyPeerConnectionRequestOptions()
            {
                LocalUserId = EosCommonData.myPuid,
                SocketId = _socketId,
            };

            notifyPeerRequestId = p2pInterface.AddNotifyPeerConnectionRequest(
                ref opt_request,
                null,
                (ref OnIncomingConnectionRequestInfo data) =>
                {
                    // ここでは data.RemoteUserId を信用して使う（remotePuidは使わない）
                    if (data.RemoteUserId == null)
                    {
                        UnityEngine.Debug.Log("リモートユーザーIDが取得できません");
                        connectTask.TrySetResult(false);
                        return;
                    }

                    var opt_accept = new AcceptConnectionOptions
                    {
                        LocalUserId = data.LocalUserId,
                        RemoteUserId = data.RemoteUserId,
                        SocketId = data.SocketId
                    };

                    var r = p2pInterface.AcceptConnection(ref opt_accept);
                    if (r != Result.Success)
                    {
                        UnityEngine.Debug.Log($"リクエストの許可に失敗しました: {r}");
                        connectTask.TrySetResult(false);
                        return;
                    }

                    acceptConnection.Add(data.RemoteUserId);
                    connectTask.TrySetResult(true);
                }
            );

            if (notifyPeerRequestId == 0)
            {
                UnityEngine.Debug.Log("AddNotifyPeerConnectionRequest 登録失敗");
                return false;
            }

            // ping/pong成立待ち と Accept待ち を競争。この処理には時間切れはなくていいや
            var pingpongTask = UniTask.WaitUntil(() => gotPing && gotPong, cancellationToken: token);

            var winner = await UniTask.WhenAny(pingpongTask, connectTask.Task);

            // どちらが勝っても、最終成功条件はここで決める
            bool acceptOk = false;

            if (winner == 1)
            {
                // connectTcsが先に完了
                acceptOk = await connectTask.Task; // true/falseを取得
            }

            bool pingpongOk = (gotPing && gotPong);
            bool success = pingpongOk || acceptOk;

            if (!success)
            {
                UnityEngine.Debug.Log("接続確立に失敗（pingpong/accept とも不成立）");
                return false;
            }

            UnityEngine.Debug.Log("接続確立（pingpong or accept）");
            return true;
        }
        finally
        {
            // notify解除（0なら何もしない）
            if (notifyPeerRequestId != 0)
            {
                p2pInterface.RemoveNotifyPeerConnectionRequest(notifyPeerRequestId);
                notifyPeerRequestId = 0;
            }

            // キャンセル登録解除
            ctr.Dispose();
        }
    }
    public void CloseConnection()
    {
        gotPong = false;
        gotPing = false;


        PeerInputData inputData = new();

        for (int i = 0; i < ringBuffer_MaxSize; i++)
        {
            ringBuffer_input_local[i] = inputData;
            ringBuffer_input_remote[i] = inputData;
        }

        acceptConnection.Clear();

        if (p2pInterface == null) return;

        if (notifyPeerRequestId != 0)
        {
            p2pInterface.RemoveNotifyPeerConnectionRequest(notifyPeerRequestId);
            notifyPeerRequestId = 0;
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

            remotePuid = null;
        }
    }

    //インプット送受信=======================================================

    public bool gotInput { get; private set; } = false;
    public bool gotInputResultAck { get; private set; } = false;
    public bool gotInputResult_remote { get; private set; } = false;
    
    public int shotFrame_local { get; set; } = -1;
    public int shotFrame_remote { get; set; } = -1;

    public void SendInput(int frame, bool _input)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        //インプットデータをローカルストレージに保存
        int index = frame & ringBuffer_MaxSize_musk;//リングバッファ
        ringBuffer_input_local[index] = new PeerInputData(frame, _input);

        _sendBuffer_input[0] = (byte)PacketType.Input;

        BitConverter.GetBytes(frame).CopyTo(_sendBuffer_input, 1);
        _sendBuffer_input[1 + 4] = Convert.ToByte(_input);

        for (int i = 0; i < inputHitorySize; i++)//過去分
        {
            int pastIndex = frame - 1 - i & ringBuffer_MaxSize_musk;
            var pastInput = ringBuffer_input_local[pastIndex];
            _sendBuffer_input[1 + 4 + 1 + i] = Convert.ToByte(pastInput.input);
        }

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = _sendBuffer_input;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);

        UnityEngine.Debug.Log($"インプット送信結果:{r}");

        if (r == Result.Success && _input) shotFrame_local = frame;
    }
    void UnPackInput(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < BufferSize_input)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        int frame = BitConverter.ToInt32(payload, 1);
        bool input = Convert.ToBoolean(payload[1 + 4]);

        PeerInputData inputData = new(frame, input);

        //データを保存
        int index = frame & ringBuffer_MaxSize_musk;
        ringBuffer_input_remote[index] = inputData;//リングバッファ

        if (input) shotFrame_remote = frame;

        for (int i = 0; i < inputHitorySize; i++)//ヒストリー分開封＆記録
        {
            int pastFrame = frame - 1 - i;
            int pastIndex = pastFrame & ringBuffer_MaxSize_musk;
            var pastInput = Convert.ToBoolean(payload[1 + 4 + 1 + i]);

            var pastData = ringBuffer_input_remote[pastIndex];

            if (pastData.frame == pastFrame && pastData.input != pastInput)
            {
                UnityEngine.Debug.Log("ヒストリーと過去記録したインプットが合致しません");
                continue;
            }

            if (pastInput) shotFrame_remote = pastFrame;

            ringBuffer_input_remote[pastIndex] = new PeerInputData(pastFrame, pastInput);
        }
    }

    public void SendInputResult(bool ack = false)
    {
        if (p2pInterface == null)
        {
            UnityEngine.Debug.Log($"p2pがねえ");
            return;
        }
        if (remotePuid == null)
        {
            UnityEngine.Debug.Log($"puidがねえ");
            return;
        }

        if (ack)
        {
            _sendBuffer_InputResult[0] = (byte)PacketType.Input_Result_Ack;
        }
        else
        {
            _sendBuffer_InputResult[0] = (byte)PacketType.Input_Result;
        }

        BitConverter.GetBytes(shotFrame_local).CopyTo(_sendBuffer_InputResult, 1);

        Result r;

        sendPacketOptions_Reliable.RemoteUserId = remotePuid;
        sendPacketOptions_Reliable.Data = _sendBuffer_InputResult;

        r = p2pInterface.SendPacket(ref sendPacketOptions_Reliable);

        UnityEngine.Debug.Log($"インプットリザルト送信結果:{r}");
    }
    void UnPackInputResult(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < BufferSize_inputResult)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        shotFrame_remote = BitConverter.ToInt32(payload, 1);

        //Ack送信
        SendInputResult(true);
    }

    void ClearInputBuffer()
    {
        gotInput = false;
        gotInputResultAck = false;
        gotInputResult_remote = false;
        shotFrame_remote = -1;
    }

    //ラウンドデータ送受信=======================================================

    public RoundData roundDataBuffer { get; set; }//ack照合用
    RoundData lastSendRoundData;
    RoundData recvRoundDataAck;

    public void SendRoundData(RoundData roundData, bool ack = false)
    {
        _sendBuffer_roundData[0] = ack ? (byte)PacketType.RoundData_Ack : (byte)PacketType.RoundData;
        _sendBuffer_roundData[1] = (byte)roundData.roundCount;
        BitConverter.GetBytes(roundData.signalFrame).CopyTo(_sendBuffer_roundData, 2);
        BitConverter.GetBytes(roundData.timeUpFrame).CopyTo(_sendBuffer_roundData, 6);

        sendPacketOptions_Reliable.RemoteUserId = remotePuid;
        sendPacketOptions_Reliable.Data = _sendBuffer_roundData;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Reliable);
        UnityEngine.Debug.Log($"ラウンドデータ送信結果：{r},");

        if(r == Result.Success)lastSendRoundData = roundData;
    }

    void UnPackRoundData(byte[] bytes)
    {
        PacketType packetType = (PacketType)bytes[0];

        int roundCount = bytes[1];
        int signalFrame = BitConverter.ToInt32(bytes, 2);
        int timeUpFrame = BitConverter.ToInt32(bytes, 6);

        var _roundData = new RoundData(roundCount, signalFrame, timeUpFrame);

        if (_roundData == null)
        {
            UnityEngine.Debug.Log($"受信したラウンドデータはヌルです");
            return;
        }

        if (packetType == PacketType.RoundData) roundDataBuffer = _roundData;
        if (packetType == PacketType.RoundData_Ack) recvRoundDataAck = _roundData;

        UnityEngine.Debug.Log($"ラウンドデータ受信結果…シグナル：{signalFrame}, ラウンド：{roundCount}");
    }

    public bool SharedRoundData()
    {
        if (lastSendRoundData == null) return false;
        if (recvRoundDataAck == null) return false;
        if (lastSendRoundData.roundCount != recvRoundDataAck.roundCount) 
            UnityEngine.Debug.Log($"ラウンドカウント不整合:{lastSendRoundData.roundCount},{recvRoundDataAck.roundCount}");
        if (lastSendRoundData.signalFrame != recvRoundDataAck.signalFrame)
            UnityEngine.Debug.Log($"シグナル不整合:{lastSendRoundData.signalFrame},{recvRoundDataAck.signalFrame}");
        if (lastSendRoundData.timeUpFrame != recvRoundDataAck.timeUpFrame)
            UnityEngine.Debug.Log($"タイムアップフレーム不整合:{lastSendRoundData.timeUpFrame},{recvRoundDataAck.timeUpFrame},");

        return true;
    }

    void ClearRoundData()
    {
        roundDataBuffer = null;
        lastSendRoundData = null;
        recvRoundDataAck = null;
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
        SendPing(nowTs);
        _nextSendAtTs = nowTs + MsToTs(_intervalMs);
    }

    /// <summary>
    /// 受信パケットがHBなら消費してtrueを返す（PlayerPeer側で以降の処理を止められる）
    /// </summary>
    public bool TryPing(byte[] payload)
    {
        var seq = NetMsg.UnpackHbPing(payload);

        // 受け取ったseqをそのまま返す（相手の時刻は不要）
        var pong = NetMsg.PackHbPong(seq);
        _send(pong);

        _lastRecvAtTs = _nowTs();
        return true;
    }

    public bool TryPong(byte[] payload)
    {
        var seq = NetMsg.UnpackHbPong(payload);
        OnPong(seq);

        _lastRecvAtTs = _nowTs();
        return true;
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
