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
    private const byte PKT_Seed = 0;
    private const byte PKT_SeedAck = 1;
    private const byte PKT_Input = 2;
    
    P2PInterface p2pInterface;

    private SocketId _socketId;

    bool isLobbyHost = false;

    ProductUserId localPuid;
    ProductUserId remotePuid;


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

    public PlayerPeer(ProductUserId _localPuid)
    {
        localPuid = _localPuid;
        _socketId = new SocketId { SocketName = SOCKET_NAME };

        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

        sendPacketOptions = new SendPacketOptions
        {
            LocalUserId = localPuid,
            RemoteUserId = remotePuid,//送信直前にも入れる
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.UnreliableUnordered,
            AllowDelayedDelivery = true,
        };

        receivePacketOptions = new ReceivePacketOptions
        {
            LocalUserId = localPuid,//送信側が設定した受信可能なPUIDと照合するID（基本的には自分）
            MaxDataSizeBytes = (uint)_recvBuffer.Length,//今回の受信で許容するサイズ。これを超えるパケットは受け取れない
            RequestedChannel = null,//受信するメッセージの種類をフィルタリング。GGPOは送信情報を種類分けしないので設定しない。
        };

        for(int i = 0; i < inputDatasMaxSize; i++)
        {
            inputFrames_local[i] = -1;
            inputFrames_remote[i] = -1;
        }
    }


    public async UniTask<bool> StartConnectToPeer(bool _isLobbyHost, ProductUserId _remotePuid, CancellationToken token)
    {
        isLobbyHost = _isLobbyHost;
        if (isLobbyHost) _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        remotePuid = _remotePuid;

        state = PeerState.SHARING_SEED;
        //相手の送るデータ受信を許可
        AcceptConnectionRequest();

        //ロビーデータ確認省略
        //メンバー人数確認省略
        //メンバー情報取得省略

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
            if (isLobbyHost &&　Time.time >= nextSendTime)
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


    public void Dispose()
    {
        CloseConnection();
    }

    void AcceptConnectionRequest()
    {
        //監視する接続要求を指定。ここでは自分あてに来るリクエストを指定
        var opt = new AddNotifyPeerConnectionRequestOptions()
        {
            LocalUserId = localPuid,
            SocketId = _socketId,
        };

        //上記条件に合うリクエストが来た時のコールバックを購読。戻り値は購読解除するときに必要
        //AddNotifyPeerConnectionRequestは一度アクセプトするとクリーンアップするまで飛んでこない
        notifyPeerRequestId = p2pInterface.AddNotifyPeerConnectionRequest(ref opt, null,
            //受信したリクエストの詳細を専用の構造体を用意して取得
            (ref OnIncomingConnectionRequestInfo data) =>
            {
                if (data.RemoteUserId != null)
                {
                    Accept(data.RemoteUserId);
                }
            }
        );

        //リクエスト受け入れを許可
        bool Accept(ProductUserId puid)
        {
            //ここで指定したremotePuidとソケットIDがAddNotifyPeerConnectionRequestで接続済みと判定される
            var _opt = new AcceptConnectionOptions
            {
                LocalUserId = localPuid,
                RemoteUserId = puid,
                SocketId = _socketId
            };

            var r = p2pInterface.AcceptConnection(ref _opt);
            return r == Result.Success || r == Result.AlreadyPending;
        }
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
                    LocalUserId = localPuid,
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

    int lastAckFrame = -1;

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

}
