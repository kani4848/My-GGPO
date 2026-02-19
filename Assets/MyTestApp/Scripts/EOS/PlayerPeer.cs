using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;
using PlayEveryWare.EpicOnlineServices;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using Codice.CM.Common;
using UnityEngine.XR;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEditor.ShaderKeywordFilter;
using System.ComponentModel;

public class PlayerPeer: IDisposable
{
    public enum PeerState
    {
        SLEEP,
     
        P2P_CONNECTING,
        P2P_CONNECTED,

        SEED_SHAERING,
        SEED_SHARED,

        INPUT_TEST,
        HANDSHAKED,

        MAIN_GAME,
    }

    public enum PacketType : byte
    {
        HbPing = 1,
        HbPong = 2,

        Seed = 3,
        Seed_Ack = 4,

        Input= 5,
        Input_Final = 6,

        RoundData = 8,
        RoundData_Ack = 9,
    }

    //リプレイ用インプットストレージ
    PeerInputData[] replayStrage_local = new PeerInputData[inputDatasMaxSize];
    PeerInputData[] replayStrage_remote = new PeerInputData[inputDatasMaxSize];

    //パケット送受信用の入れ物。高回転なのでリストだと遅いため配列を使用する。
    const int inputHitorySize = 6;
    const int inputBufferSize = 1 + 4 + 1 + 1 * inputHitorySize;
    const int finalInputBufferSize = 1 + 4;
    const int seedBufferSize = 1 + 4;//ackと兼用
    //識別子（上限１０）＋ラウンド数（上限10）+スタートフレーム＋シグナル（4バイト）+タイムアップフレーム、 ackと兼用
    const int roundDataBufferSize = 1 + 1 + 4 + 4 + 4;
    private readonly byte[] _sendBuffer_input = new byte[inputBufferSize];//過去数フレーム分のインプット履歴も追加
    private readonly byte[] _sendBuffer_finalInput = new byte[finalInputBufferSize];
    private readonly byte[] _sendBuffer_seed = new byte[seedBufferSize];
    private readonly byte[] _sendBuffer_roundData = new byte[roundDataBufferSize];
    private readonly byte[] _recvBuffer = new byte[4096];//一時的な受け取りに使う。どんな形式でも共通で使うので長めに設定。
    const int inputDatasMaxSize = 32;
    const int inputDatasMaxSize_musk = inputDatasMaxSize - 1;
    PeerInputData[] inputDatas_local = new PeerInputData[inputDatasMaxSize];
    PeerInputData[] inputDatas_remote = new PeerInputData[inputDatasMaxSize];

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

    //保存情報
    public PeerState state { get; private set; } = PeerState.SLEEP;
    uint _seed;
    ulong notifyPeerRequestId;
    public double pingMs { get; private set; } = -1;

    ProductUserId remotePuid;
    HeartbeatSession heartBeat;

    //ラウンド単位の保存情報
    int latestRemoteInputFrame = -1;
    int shotFrame_local = -1;
    int shotFrame_remote = -1;

    public PlayerPeer()
    {
        _socketId = new SocketId { SocketName = SOCKET_NAME };

        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

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

        for(int i = 0; i < inputDatasMaxSize; i++)
        {
            inputDatas_local[i] = new PeerInputData();
            inputDatas_remote[i] = new PeerInputData();
        }

        heartBeat = new(SendHB);
    }

    public void Dispose()
    {
        CloseConnection();
    }

    HashSet<ProductUserId> acceptConnection = new(); 

    public void Tick()
    {
        if (state != PeerState.SLEEP)
        {
            heartBeat.Tick();
            ReceivePump();
            pingMs = heartBeat.PingMs;
        }
    }

    //接続確率===================================================

    bool gotPing = false;
    bool gotPong = false;

    public async UniTask<bool> RegisterConnectionRequestAccept(ProductUserId _remotePuid , CancellationToken token)
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
            state = PeerState.P2P_CONNECTING; // このステートでパケットを飛ばす
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

            // ping/pong成立待ち と Accept待ち を競争
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
                state = PeerState.SLEEP;
                return false;
            }

            state = PeerState.P2P_CONNECTED;
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

    public async UniTask<bool> SendSeedAndWaitSeedAck(CancellationToken token)
    {
        state = PeerState.SEED_SHAERING;

        _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);

        float timeOutClock = Time.time;
        float nextSendTime = Time.time;

        //シード値の共有
        while (!token.IsCancellationRequested)
        {
            if (state == PeerState.SEED_SHARED)
            {
                return true;
            }

            //時間切れ
            if (Time.time - timeOutClock >= HandshakeTimeoutMs)
            {
                return false;
            }

            //仮インプットデータ送信
            if (Time.time >= nextSendTime)
            {
                UnityEngine.Debug.Log("シード送信");
                SendSeed(_seed);
            }


            //これがないと1フレーム中にループしまくってフリーズ
            await UniTask.Yield();
        }

        return false;
    }

    public async UniTask<bool> WaitReceivingSeed(CancellationToken token)
    {
        state = PeerState.SEED_SHAERING;

        float timeOutClock = Time.time;

        //シード値の共有
        while (!token.IsCancellationRequested)
        {
            if (state == PeerState.SEED_SHARED)
            {
                return true;
            }

            //時間切れ
            if (Time.time - timeOutClock >= HandshakeTimeoutMs)
            {
                return false;
            }

            //これがないと1フレーム中にループしまくってフリーズ
            await UniTask.Yield();
        }

        return false;
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

    public void SendSeed(uint seed)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        _sendBuffer_seed[0] = (byte)PacketType.Seed;
        BitConverter.GetBytes(seed).CopyTo(_sendBuffer_seed, 1);

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = _sendBuffer_seed;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);
        UnityEngine.Debug.Log($"シードの送信結果：Seed,{r}, データの長さ: {sendPacketOptions_Unreliable.Data.Count}");
    }

    public void SendSeedAck(uint seed)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        _sendBuffer_seed[0] = (byte)PacketType.Seed_Ack;
        BitConverter.GetBytes(seed).CopyTo(_sendBuffer_seed, 1);

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = _sendBuffer_seed;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);
        UnityEngine.Debug.Log($"シードの送信結果：SeedAck,{r}, データの長さ: {sendPacketOptions_Unreliable.Data.Count}");
    }

    public async UniTask<bool> InputCommunicationTest(CancellationToken token)
    {
        if(state == PeerState.SEED_SHARED) state = PeerState.INPUT_TEST;

        float timeout = Time.time + HandshakeTimeoutMs;
        float sendInterval = Time.time;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (state == PeerState.HANDSHAKED)
                {
                    ClearInputData();
                    return true;
                }

                if (Time.time > timeout)
                {
                    return false;
                }

                if (Time.time == sendInterval)
                {
                    SendInput(4649, true);
                    sendInterval += 0.2f;
                }

                await UniTask.Yield();
            }
        }
        finally
        {
        }

        return false;
    }

    //メインゲーム通信===================================================
    public void SendInput(int frame, bool _input)
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        if (_input) shotFrame_local = frame;

        //インプットデータをローカルストレージに保存
        int index = frame & inputDatasMaxSize_musk;//リングバッファ
        var data = new PeerInputData(frame, _input);
        replayStrage_local[index] = data;
        inputDatas_local[index] = data;

        _sendBuffer_input[0] = (byte)PacketType.Input;
        BitConverter.GetBytes(frame).CopyTo(_sendBuffer_input, 1);
        _sendBuffer_input[1 + 4] = Convert.ToByte(_input);

        for(int i =0; i < inputHitorySize; i++)//過去分
        {
            int pastIndex = frame - 1 - i & inputDatasMaxSize_musk;
            var pastInput = inputDatas_local[pastIndex];
            _sendBuffer_input[1 + 4 + 1 + i] = Convert.ToByte(pastInput.input);
        }

        sendPacketOptions_Unreliable.RemoteUserId = remotePuid;
        sendPacketOptions_Unreliable.Data = _sendBuffer_input;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Unreliable);

        UnityEngine.Debug.Log($"インプット送信結果:{r}");
    }

    public void SendFinalInput()
    {
        if (p2pInterface == null) return;
        if (remotePuid == null) return;

        _sendBuffer_input[0] = (byte)PacketType.Input_Final;
        BitConverter.GetBytes(shotFrame_local).CopyTo(_sendBuffer_input, 1);

        Result r;

        sendPacketOptions_Reliable.RemoteUserId = remotePuid;
        sendPacketOptions_Reliable.Data = _sendBuffer_input;

        r = p2pInterface.SendPacket(ref sendPacketOptions_Reliable);

        UnityEngine.Debug.Log($"インプット送信結果:{r}");
    }


    void UnPackInputData(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < inputBufferSize)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        int frame = BitConverter.ToInt32(payload, 1);
        bool input = Convert.ToBoolean(payload[1 + 4]);

        //最新フレームを更新
        if(frame> latestRemoteInputFrame) latestRemoteInputFrame = frame;

        //ショットフレームを更新
        if (input) shotFrame_remote = frame;

        PeerInputData inputData = new(frame, input);

        //データを保存
        int index = frame & inputDatasMaxSize_musk;
        replayStrage_remote[index] = inputData;
        inputDatas_remote[index] = inputData;

        for (int i = 0; i < inputHitorySize; i++)//ヒストリー分開封＆記録
        {
            int pastFrame = frame - 1 - i;
            int pastIndex = pastFrame & inputDatasMaxSize_musk;
            var pastInput = Convert.ToBoolean(payload[1 + 4 + 1 + i]);

            var pastData = inputDatas_remote[pastIndex];

            if (pastData.frame == pastFrame && pastData.input != pastInput)
            {
                UnityEngine.Debug.Log("ヒストリーと過去記録したインプットが合致しません");
                continue;
            }

            inputDatas_remote[pastIndex] = new PeerInputData(pastFrame, pastInput);
        }

        if (state == PeerState.INPUT_TEST)
        {
            state = PeerState.HANDSHAKED;
            UnityEngine.Debug.Log("インプットデータ受信を確認");
        }
    }


    void UnPackFinalInputData(byte[] payload)
    {
        // 最低限の長さチェック
        if (payload == null || payload.Length < finalInputBufferSize)
        {
            UnityEngine.Debug.Log("インプットデータに適切なサイズではありません");
            return;
        }

        int frame = BitConverter.ToInt32(payload, 1);

        //最新フレームを更新
        if (frame > latestRemoteInputFrame) latestRemoteInputFrame = frame;

        gotFinalRemoteInput = true;
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

                    case PacketType.Seed:
                        if (outBytesWritten < seedBufferSize) continue;
                        UnPackSeedData(_recvBuffer);
                        break;

                    case PacketType.Seed_Ack:
                        if (outBytesWritten < seedBufferSize) continue;
                        UnPackSeedAckData(_recvBuffer);
                        break;

                    case PacketType.Input:
                        if (outBytesWritten < inputBufferSize) continue;
                        UnPackInputData(_recvBuffer);
                        break;

                    case PacketType.Input_Final:
                        if (outBytesWritten < finalInputBufferSize) continue;
                        UnPackFinalInputData(_recvBuffer);
                        break;

                    case PacketType.RoundData:
                        if (outBytesWritten < roundDataBufferSize) continue;
                        UnPackRoundData(_recvBuffer);
                        break;

                    case PacketType.RoundData_Ack:
                        if (outBytesWritten < roundDataBufferSize) continue;
                        UnPackSignalAckData(_recvBuffer);
                        break;
                }
            }
        }
        finally
        {
        }
    }
    void UnPackSeedData(byte[] bytes)
    {
        _seed = BitConverter.ToUInt32(bytes, 1);
        SendSeedAck(_seed);

        if(state == PeerState.SEED_SHAERING)
        state = PeerState.SEED_SHARED;
    }

    void UnPackSeedAckData(byte[] bytes)
    {
        var seedAck = BitConverter.ToUInt32(bytes, 1);
        if (state == PeerState.SEED_SHAERING && _seed == seedAck)
        {
            state = PeerState.SEED_SHARED;
        }
    }

    public PeerInputData TryGetRemoteInput_ByFrame(int frame)
    {
        try
        {
            int index = frame & inputDatasMaxSize_musk;
            return replayStrage_remote[index];       
        }
        catch
        {
            return replayStrage_remote.LastOrDefault();
        }
    }

    public PeerInputData TryGetRemoteInput_Latest()
    {
        try
        {
            int index = latestRemoteInputFrame & inputDatasMaxSize_musk;
            return inputDatas_remote[index];
        }
        catch
        {
            return replayStrage_remote.LastOrDefault();
        }
    }

    public int TryGetRemoteShotFrame()
    {
        return shotFrame_remote;
    }

    public async UniTask<int> SendFinalInputAndWaitRemote(CancellationToken token)
    {
        gotFinalRemoteInput = false;

        SendFinalInput();

        try
        {
            await UniTask.WaitUntil(() => gotFinalRemoteInput, cancellationToken: token)
                .Timeout(TimeSpan.FromSeconds(5));

            return shotFrame_remote;
        }
        catch (TimeoutException)
        {
            return -2;
        }
    }

    //メインゲーム：シグナル通信===================================================
    
    bool gotSignal = false;
    bool gotSignalAck = false;

    bool gotFinalRemoteInput = false;

    int roundCount = 0;
    int startSceneFrame = -1;
    int signalFrame = -1;
    int timeUpFrame = -1;

    public async UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token)
    {
        gotSignalAck = false;

        roundCount = roundData.roundCount;
        startSceneFrame = roundData.startSceneFrame;
        signalFrame = roundData.signalFrame;
        timeUpFrame = roundData.timeUpFrame;

        SendRoundData(roundData);

        int retryCount = 0;

        while (!gotSignalAck)
        {
            if (retryCount == 5)
            {
                UnityEngine.Debug.Log($"スタートフレーム送信タイムアウト");
                return false;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);

            if (!gotSignalAck)
            {
                SendRoundData(roundData);
                retryCount++;
            }
        }

        roundCount++;
        return true;
    }

    public async UniTask<RoundData> WaitReceivingSignal(CancellationToken token)
    {
        gotSignal = false;

        float timeOut = Time.time + 5;

        var r = await UniTask.WhenAny(
            UniTask.WaitUntil(() => gotSignal, cancellationToken: token),
            UniTask.WaitUntil(() => Time.time >= timeOut, cancellationToken: token)
        );

        if(r == 0 )
        {
            return recievedRoundData;
        }
        else
        {
            UnityEngine.Debug.Log($"スタートフレーム受信タイムアウト");
            return null;
        }
    }

    void SendRoundData(RoundData roundData, bool ack = false)
    {
        _sendBuffer_roundData[0] = ack ? (byte)PacketType.RoundData_Ack : (byte)PacketType.RoundData;
        _sendBuffer_roundData[1] = (byte)roundData.roundCount;
        BitConverter.GetBytes(roundData.startSceneFrame).CopyTo(_sendBuffer_roundData, 2);
        BitConverter.GetBytes(roundData.signalFrame).CopyTo(_sendBuffer_roundData, 6);
        BitConverter.GetBytes(roundData.timeUpFrame).CopyTo(_sendBuffer_roundData, 10);

        sendPacketOptions_Reliable.RemoteUserId = remotePuid;
        sendPacketOptions_Reliable.Data = _sendBuffer_roundData;

        var r = p2pInterface.SendPacket(ref sendPacketOptions_Reliable);
        UnityEngine.Debug.Log($"ラウンドデータ送信結果：{r}," +
            $"シグナル：{signalFrame}, ラウンド: {roundCount}, スタートローカルフレーム:{startSceneFrame }");
    }

    RoundData recievedRoundData;

    void UnPackRoundData(byte[] bytes)
    {
        roundCount = bytes[1];
        startSceneFrame = BitConverter.ToInt32(bytes, 2);
        signalFrame = bytes[6];
        timeUpFrame = BitConverter.ToInt32(bytes, 7);

        var roundData = new RoundData(roundCount, startSceneFrame, signalFrame, timeUpFrame);

        if(roundData == null)
        {
            UnityEngine.Debug.Log($"受信したラウンドデータはヌルです");
            return;
        }

        SendRoundData(roundData,true);//ack送信

        UnityEngine.Debug.Log($"シグナル受信結果…シグナル：{signalFrame}, ラウンド：{roundCount}, スタートローカルフレーム{startSceneFrame}");
        
        recievedRoundData  = roundData;
        gotSignal = true;
    }


    void UnPackSignalAckData(byte[] bytes)
    {
        var _roundCount = bytes[1];
        var _startSceneFrame = BitConverter.ToInt32(bytes, 2);
        var _signalFrame = bytes[6];
        var _timeUpFrame = BitConverter.ToInt32(bytes, 7);

        if (roundCount != _roundCount 
            || startSceneFrame!= _startSceneFrame
            || signalFrame != _signalFrame
            || timeUpFrame != _timeUpFrame)
        {
            UnityEngine.Debug.Log($"相手のラウンドデータが異なっています。" +
                $"スタート {startSceneFrame}:{_startSceneFrame}, ラウンド{roundCount}:{_roundCount}"+
                $"シグナル {signalFrame}:{_signalFrame}, タイムアップ{timeUpFrame}:{_timeUpFrame}");
        }
        else
        {
            gotSignalAck = true;
        }
    }

    //メインゲーム：初期化処理===================================================
    public void ClearInputData()
    {
        gotSignal = false;
        gotSignalAck = false;
        latestRemoteInputFrame = -1;
        shotFrame_remote = -1;

        PeerInputData inputData = new();

        for (int i = 0; i < inputDatasMaxSize; i++)
        {
            inputDatas_local[i] = inputData;
            inputDatas_remote[i] = inputData;
        }
    }

    public void CloseConnection()
    {
        roundCount = 0;
        ClearInputData();

        state = PeerState.SLEEP;
        gotPing = false;
        gotPong = false;
        acceptConnection.Clear();

        if (p2pInterface == null) return;

        if (notifyPeerRequestId != 0)
        {
            UnityEngine.Debug.Log("リクエスト受け入れ解除");
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
