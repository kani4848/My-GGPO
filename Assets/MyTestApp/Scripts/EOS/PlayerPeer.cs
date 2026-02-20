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

    //ラウンド単位の保存情報
    int latestRemoteInputFrame = -1;
    int shotFrame_local = -1;
    int shotFrame_remote = -1;

    PeerRouter router;

    float timeOutSeconds = 5;

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
            router.Tick();
            pingMs = router.GetPingMs();
        }
    }

    //ｐ２ｐ接続フロー===================================================
    public async UniTask<bool> AcceptRequestP2P(ProductUserId remote, CancellationToken token)
    {
        state = PeerState.P2P_CONNECTING;

        bool r = await router.RegisterConnectionRequestAccept(remote, token);

        if (r)
        {
            state = PeerState.P2P_CONNECTED;
        }

        return r;
    }

    //ハンドシェイクフロー===================================================

    public async UniTask<bool> handShakeFlow(CancellationToken token)
    {
        state = PeerState.INPUT_TEST;

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        //オールレディは保証されている前提
        await UniTask.WaitUntil(() => state == PeerState.P2P_CONNECTED, cancellationToken: linkedCts.Token);

        float timeout = Time.time + timeOutSeconds;
        float sendInterval = Time.time;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Time.time > timeout)
                {
                    return false;
                }

                if (Time.time >= sendInterval)
                {
                    router.SendInput(4649, true);
                    sendInterval += 0.2f;
                }

                if (router.gotInput)
                {
                    state = PeerState.HANDSHAKED;
                    ClearInputData();
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

    async UniTask MainGameLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                //ラウンドスタートバリア
                //メインゲームループ
                //リザルトバリア
            }
        }
        finally
        {

        }

        //マッチ終了

        CloseConnection();
    }

    //共通===================================================

    //インプット通信===================================================

    bool gotFinalInputAck = false;

    public int TryGetRemoteShotFrame()
    {
        return shotFrame_remote;
    }

    public async UniTask<int> SendFinalInputAndWaitRemote(CancellationToken token)
    {
        UnityEngine.Debug.Log($"ファイナルインプット通信開始");
        
        state = PeerState.MAIN_GAME_RESULT;

        gotFinalInputAck = false;

        float sendInterval = Time.time;
        float timeOut = Time.time + 5f;

        while (!token.IsCancellationRequested)
        {
            if(Time.time >= sendInterval)
            {
                router.SendFinalInput();
                sendInterval = Time.time + 0.2f;
            }

            if(Time.time >= timeOut)
            {
                return -2;
            }

            if (gotFinalInputAck)
            {
                state = PeerState.MAIN_GAME;
                return shotFrame_remote;
            }

            await UniTask.Yield();
        }

        return -2;
    }

    //メインゲーム：ラウンドデータ通信===================================================
    
    int roundCount = 0;
    int signalFrame = -1;
    int timeUpFrame = -1;

    public async UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token)
    {
        roundCount = roundData.roundCount;
        signalFrame = roundData.signalFrame;
        timeUpFrame = roundData.timeUpFrame;

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

        router.SendRoundData(router.roundDataBuffer, true);

        if(r == 0 )
        {
            return router.roundDataBuffer;
        }
        else
        {
            UnityEngine.Debug.Log($"スタートフレーム受信タイムアウト");
            return null;
        }
    }

    //メインゲーム：初期化処理===================================================
    public void ClearInputData()
    {
        latestRemoteInputFrame = -1;
        shotFrame_remote = -1;
    }

    public void CloseConnection()
    {
        ClearInputData();

        state = PeerState.SLEEP;

        router.CloseConnection();
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
