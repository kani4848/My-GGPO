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

public class PlayerPeer: IDisposable
{
    public enum PeerState
    {
        SLEEP,
     
        P2P_CONNECTING,
        P2P_CONNECTED,

        INPUT_TEST,
        HANDSHAKED,

        MAIN_GAME,
        MAIN_GAME_RESULT,
    }

    //保存情報
    public PeerState state { get; private set; } = PeerState.SLEEP;
    public double pingMs { get; private set; } = -1;

    PeerRouter router;

    const float timeOutSeconds = 5f;
    const float resendInterval = 0.2f;

    ProductUserId remoteId;

    public PlayerPeer()
    {
        router = new();
    }

    public void Dispose()
    {
        CloseConnection();
    }

    public void Tick()
    {
        if (state != PeerState.SLEEP)
        {
            router.ReceivePump();
        }

        switch (state)
        {
            case PeerState.P2P_CONNECTING:
                router.SendPing();
                break;

            case PeerState.P2P_CONNECTED:
                if (!router.OpponentIsAlive())
                {
                    state = PeerState.SLEEP;
                    CloseConnection();
                    LobbyMemberEvent.RaiseDeath(remoteId.ToString());
                    return;
                }

                pingMs = router.GetPingMs();
                break;
        }
    }

    //ｐ２ｐ接続フロー===================================================

    public async UniTask<bool> AcceptRequestP2P(ProductUserId remote, CancellationToken token)
    {
        state = PeerState.P2P_CONNECTING;

        bool r = await router.RegisterConnectionRequestAccept(remote, token);

        if (r)
        {
            remoteId = remote;
            state = PeerState.P2P_CONNECTED;
        }

        return r;
    }

    //ハンドシェイクフロー===================================================

    public async UniTask<bool> handShakeFlow(CancellationToken token)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        //オールレディは保証されている前提
        await UniTask.WaitUntil(() => state == PeerState.P2P_CONNECTED, cancellationToken: linkedCts.Token);
        
        state = PeerState.INPUT_TEST;

        float timeout = Time.time + timeOutSeconds;
        float sendInterval = Time.time;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Time.time >= timeout)
                {
                    return false;
                }

                if (Time.time >= sendInterval)
                {
                    router.SendInput(4649, false);
                    sendInterval += resendInterval;
                }

                if (router.gotInput)
                {
                    state = PeerState.HANDSHAKED;
                    ResetBuffers();
                    return true;
                }

                await UniTask.Yield();
            }
        }
        finally
        {
        }

        return false;
    }

    //メインゲームフロー===================================================
    public void SendInput(int frame, bool input)
    {
        router.SendInput(frame, input);
    }

    //インプット通信===================================================

    public bool TryGetRemoteInput()
    {
        return router.shotFrame_remote != -1;
    }

    public async UniTask<int> SendFinalInputAndWaitRemote(CancellationToken token)
    {
        UnityEngine.Debug.Log($"ファイナルインプット通信開始");
        
        state = PeerState.MAIN_GAME_RESULT;

        float sendInterval = Time.time;
        float timeOut = Time.time + timeOutSeconds / 3;

        while (!token.IsCancellationRequested)
        {
            //リザルト送信
            if (Time.time >= sendInterval)
            {
                router.SendInputResult();
                sendInterval = Time.time + 0.2f;
            }

            if(Time.time >= timeOut)
            {
                return -2;
            }

            //リザルトack受信
            if (router.gotInputResultAck)
            {
                break;
            }

            await UniTask.Yield();
        }

        timeOut = Time.time + timeOutSeconds / 3;

        //相手のリザルト受信
        while (!token.IsCancellationRequested)
        {
            if (Time.time >= timeOut) return -2;

            if (router.gotInputResult_remote) return router.shotFrame_remote;

            await UniTask.Yield();
        }

        return -2;
    }

    //メインゲーム：ラウンドデータ通信===================================================
    
    public async UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token)
    {
        router.SendRoundData(roundData);

        float sendInterval = Time.time;
        float timeout = Time.time + 5f;

        while (!token.IsCancellationRequested)
        {
            if (Time.time >= timeout)
            {
                UnityEngine.Debug.Log($"スタートフレーム送信タイムアウト");
                return false;
            }

            if (Time.time >= sendInterval)
            {
                router.SendRoundData(roundData);
                sendInterval = Time.time + 0.2f;
            }

            if (router.SharedRoundData()) return true;

            await UniTask.Yield();
        }

        return true;
    }

    public async UniTask<RoundData> WaitReceivingSignal(CancellationToken token)
    {
        float timeOut = Time.time + 5;

        var r = await UniTask.WhenAny(
            UniTask.WaitUntil(() => router.roundDataBuffer != null, cancellationToken: token),
            UniTask.WaitUntil(() => Time.time >= timeOut, cancellationToken: token)
        );

        if(r == 0 )
        {
            router.SendRoundData(router.roundDataBuffer, true);
            return router.roundDataBuffer;
        }
        else
        {
            UnityEngine.Debug.Log($"スタートフレーム受信タイムアウト");
            return null;
        }
    }

    //メインゲーム：初期化処理===================================================
    public void ResetBuffers()
    {
        router.ResetBuffers();
    }

    public void CloseConnection()
    {
        state = PeerState.SLEEP;
        ResetBuffers();
        router.CloseConnection();
        router.ResetBuffers();
    }
}
