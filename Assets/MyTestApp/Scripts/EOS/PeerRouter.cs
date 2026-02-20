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

public enum PacketType : byte
{
    None = 0,

    //大量に送るものはack不要
    HbPing = 1,
    HbPong = 2,

    Input = 3,
    
    //足並みをそろえるタイミングに送るものはackを用意
    Input_Final = 6,
    Input_Final_Ack = 7,

    RoundData = 8,
    RoundData_Ack = 9,
}

public class PeerRouter
{
    //パケット送受信用の入れ物。高回転なのでリストだと遅いため配列を使用する。
    const int inputHitorySize = 6;
    const int inputBufferSize = 1 + 4 + 1 + 1 * inputHitorySize;
    const int finalInputBufferSize = 1 + 4;
    //識別子（上限１０）＋ラウンド数（上限10）＋シグナル（4バイト）+タイムアップフレーム、 ackと兼用
    const int roundDataBufferSize = 1 + 1 + 4 + 4;
    private readonly byte[] _sendBuffer_input = new byte[inputBufferSize];//過去数フレーム分のインプット履歴も追加
    private readonly byte[] _sendBuffer_finalInput = new byte[finalInputBufferSize];
    private readonly byte[] _sendBuffer_roundData = new byte[roundDataBufferSize];
    private readonly byte[] _recvBuffer = new byte[4096];//一時的な受け取りに使う。どんな形式でも共通で使うので長めに設定。
    const int inputDatasMaxSize = 32;
    const int inputDatasMaxSize_musk = inputDatasMaxSize - 1;
    PeerInputData[] ringBuffer_input_local = new PeerInputData[inputDatasMaxSize];
    PeerInputData[] ringBuffer_input_remote = new PeerInputData[inputDatasMaxSize];

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
    const int HandshakeTimeoutMs = 6;

    HeartbeatSession heartBeat;
    ProductUserId remotePuid;
    double pingMs;
    HashSet<ProductUserId> acceptConnection = new();
    ulong notifyPeerRequestId;


    bool gotPong = false;
    bool gotPing = false;
    public bool gotInput { get; private set; } = false;

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

        for (int i = 0; i < inputDatasMaxSize; i++)
        {
            ringBuffer_input_local[i] = new PeerInputData();
            ringBuffer_input_remote[i] = new PeerInputData();
        }

        heartBeat = new(SendHB);
    }


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

        for (int i = 0; i < inputDatasMaxSize; i++)
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
                        if (outBytesWritten < inputBufferSize) continue;
                        gotInput = true;
                        UnPackInput(_recvBuffer);
                        break;

                    case PacketType.Input_Final:
                        if (outBytesWritten < finalInputBufferSize) continue;
                        UnPackFinalInput(_recvBuffer);
                        break;

                    case PacketType.Input_Final_Ack:
                        break;

                    case PacketType.RoundData_Ack:
                    case PacketType.RoundData:
                        if (outBytesWritten < roundDataBufferSize) continue;
                        UnPackRoundData(_recvBuffer);
                        break;
                }
            }
        }
        finally
        {
        }
    }


    public void SendInput(int frame, bool _input)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        //インプットデータをローカルストレージに保存
        int index = frame & inputDatasMaxSize_musk;//リングバッファ
        var data = new PeerInputData(frame, _input);
        ringBuffer_input_local[index] = data;

        _sendBuffer_input[0] = (byte)PacketType.Input;

        BitConverter.GetBytes(frame).CopyTo(_sendBuffer_input, 1);
        _sendBuffer_input[1 + 4] = Convert.ToByte(_input);

        for (int i = 0; i < inputHitorySize; i++)//過去分
        {
            int pastIndex = frame - 1 - i & inputDatasMaxSize_musk;
            var pastInput = ringBuffer_input_local[pastIndex];
            _sendBuffer_input[1 + 4 + 1 + i] = Convert.ToByte(pastInput.input);
        }

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = _sendBuffer_input;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);

        UnityEngine.Debug.Log($"インプット送信結果:{r}");
    }
    void UnPackInput(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < inputBufferSize)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        int frame = BitConverter.ToInt32(payload, 1);
        bool input = Convert.ToBoolean(payload[1 + 4]);

        PeerInputData inputData = new(frame, input);

        //データを保存
        int index = frame & inputDatasMaxSize_musk;
        ringBuffer_input_remote[index] = inputData;//リングバッファ

        for (int i = 0; i < inputHitorySize; i++)//ヒストリー分開封＆記録
        {
            int pastFrame = frame - 1 - i;
            int pastIndex = pastFrame & inputDatasMaxSize_musk;
            var pastInput = Convert.ToBoolean(payload[1 + 4 + 1 + i]);

            var pastData = ringBuffer_input_remote[pastIndex];

            if (pastData.frame == pastFrame && pastData.input != pastInput)
            {
                UnityEngine.Debug.Log("ヒストリーと過去記録したインプットが合致しません");
                continue;
            }

            ringBuffer_input_remote[pastIndex] = new PeerInputData(pastFrame, pastInput);
        }
    }

    public void SendFinalInput(bool ack = false)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        if (ack)
        {
            _sendBuffer_finalInput[0] = (byte)PacketType.Input_Final_Ack;
        }
        else
        {
            _sendBuffer_finalInput[0] = (byte)PacketType.Input_Final;
        }

        Result r;

        sendPacketOptions_Reliable.RemoteUserId = remotePuid;
        sendPacketOptions_Reliable.Data = _sendBuffer_finalInput;

        r = p2pInterface.SendPacket(ref sendPacketOptions_Reliable);

        UnityEngine.Debug.Log($"ファイナルインプット送信結果:{r}");
    }
    void UnPackFinalInput(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < finalInputBufferSize)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        int frame = BitConverter.ToInt32(payload, 1);

        SendFinalInput(true);
    }

    RoundData lastSendRoundData;
    RoundData recvRoundDataAck;
    public RoundData roundDataBuffer { get; set; }//ゲスト用


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

        if(packetType == PacketType.RoundData) roundDataBuffer = _roundData;
        if (packetType == PacketType.RoundData_Ack) recvRoundDataAck = _roundData;

        UnityEngine.Debug.Log($"シグナル受信結果…シグナル：{signalFrame}, ラウンド：{roundCount}");
    }

    public bool SharedRoundData()
    {
        return lastSendRoundData == recvRoundDataAck;
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
}
