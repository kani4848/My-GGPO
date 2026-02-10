using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.tvOS;
using PlayEveryWare.EpicOnlineServices.Samples;
using System.Threading;
using PlayEveryWare.EpicOnlineServices;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using NUnit.Framework;
using System.Linq;
using Cysharp.Threading.Tasks;

public enum PeerType
{
    None,
    Local,
    Remote,
}

public class PeerInputData
{
    public int frame;
    public int lasdAckFrame;
    public bool input;

    public PeerInputData(int _frame, int _lastAckFrame, bool _input)
    {
        frame = _frame;
        lasdAckFrame = _lastAckFrame;
        input = _input;
    }
}

public class Peer_Online
{
    // ===== Settings =====
    public string SOCKET_NAME = "GAME";
    public int PollIntervalMs = 200;
    public int HandshakeTimeoutMs = 6000;

    // Packet types
    private const byte PKT_SEED = 0;
    private const byte PKT_INPUT = 1;
    
    P2PInterface p2pInterface;

    private SocketId _socketId;

    ProductUserId myPuid;
    ProductUserId remotePuid;
    List<PeerInputData> inputDatas_local = new();
    List<PeerInputData> inputDatas_remote = new();

    int pressedFrame_local = -1;
    int pressedFrame_remote = -1;

    //受信専用のバイト型配列。4096つのバイトを許容。数が多いのは、byte型が重くないため
    //保存用ではなく、一時的な受け取りに使う。
    private readonly byte[] _recvBuffer = new byte[4096];

    SendPacketOptions sendPacketOptions;
    ReceivePacketOptions receivePacketOptions;

    SocketId socketId;
    ProductUserId outPeerId;
    byte outChannel;
    uint outBytesWritten;

    bool getSeed = false;
    uint _seed;

    public bool shot = false;

    IEosService eosService;

    public Peer_Online(PeerType peerType, IEosService _eosService)
    {
        eosService = _eosService;

        if (peerType == PeerType.Local)
        {
            myPuid = eosService.localPUID;
            remotePuid = eosService.remotePUID;
        }
        else
        {
            myPuid = eosService.remotePUID;
            remotePuid = eosService.localPUID;
        }

        Func<P2PInterface> _getP2P = () => EOSManager.Instance.GetEOSPlatformInterface().GetP2PInterface();
        p2pInterface = _getP2P?.Invoke();

        sendPacketOptions = new SendPacketOptions
        {
            LocalUserId = myPuid,
            RemoteUserId = remotePuid,
            SocketId = _socketId,
            Channel = 0,
            Reliability = PacketReliability.UnreliableUnordered,
            AllowDelayedDelivery = true,
        };

        receivePacketOptions = new ReceivePacketOptions
        {
            LocalUserId = eosService.myPuid,//送信側が設定した受信可能なPUIDと照合するID（基本的には自分）
            MaxDataSizeBytes = (uint)_recvBuffer.Length,//今回の受信で許容するサイズ。これを超えるパケットは受け取れない
            RequestedChannel = null,//受信するメッセージの種類をフィルタリング。GGPOは送信情報を種類分けしないので設定しない。
        };
    }

    public uint CreateAndSendSeed()
    {
        byte[] seedData = new byte[5];
        seedData[0] = PKT_SEED;
        _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        BitConverter.GetBytes(_seed).CopyTo(seedData, 1);
        sendPacketOptions.Data = seedData;
        p2pInterface.SendPacket(ref sendPacketOptions);

        return _seed;
    }

    public async UniTask<uint> WaitForSeedReply()
    {
        while (true)
        {
            ReceivePump();
            if(getSeed)return _seed;
            await UniTask.Yield();
        }
    }

    public async UniTask<bool> WaitAndRecieveReady()
    {
        while (true)
        {
            ReceivePump();
            if (inputDatas_remote.Count != 0) return true;
            await UniTask.Yield();
        }
    }

    public void MainLoop(int frame)
    {
        SendInput(frame);
        ReceivePump();
    }

    public void SendInput(int frame)
    {
        if (p2pInterface == null) return;
        if (inputDatas_remote.Count == 0) return;

        bool input;

        if (pressedFrame_local == -1)
        {
            input = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            pressedFrame_local = frame;
        }
        else
        {
            input = false;
        }

        int lastAckFrame = inputDatas_remote.Last().frame;
        byte[] buf = new byte[10];
        buf[0] = PKT_INPUT;
        BitConverter.GetBytes(frame).CopyTo(buf, 1);//入力フレーム
        BitConverter.GetBytes(lastAckFrame).CopyTo(buf, 5);//最後に受信した相手のフレーム
        buf[9] = Convert.ToByte(input);//入力内容

        sendPacketOptions.Data = buf;

        inputDatas_local.Add(new PeerInputData(frame, lastAckFrame, input));
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
                ref socketId, //EOSにおけるポート番号的なもの
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
            if (outBytesWritten < 10) continue;

            //受信データタイプを識別、タイプごとに処理を分岐
            byte type = _recvBuffer[0];
            if (type == PKT_SEED) UnPackSeedData(_recvBuffer);
            if (type == PKT_INPUT) UnPackInputData(_recvBuffer);
        }
    }
    void UnPackSeedData(byte[] bytes)
    {
        if (getSeed) return;
        getSeed = true;
        _seed = BitConverter.ToUInt32(bytes, 1);
    }

    PeerInputData UnPackInputData(byte[] bytes)
    {
        int remoteCurrentFrame = BitConverter.ToInt32(bytes, 1);
        int remoteAckFrame = BitConverter.ToInt32(bytes, 5);
        bool remoteInput = Convert.ToBoolean(bytes[9]);

        PeerInputData inputData = new PeerInputData(remoteCurrentFrame, remoteAckFrame, remoteInput);

        inputDatas_remote.Add(inputData);

        if (pressedFrame_remote == -1 && remoteInput)
        {
            pressedFrame_remote = remoteCurrentFrame;
        }

        return inputData;
    }

    public (int local, int remote) GetBothInPutData()
    {
        return (pressedFrame_local, pressedFrame_remote);
    }

    public void OnRoundReset()
    {
        inputDatas_local.Clear();
        inputDatas_remote.Clear();
        pressedFrame_local = -1;
        pressedFrame_remote = -1;
    }
}
