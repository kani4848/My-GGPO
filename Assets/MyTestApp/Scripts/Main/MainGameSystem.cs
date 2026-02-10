using Epic.OnlineServices;
using System;
using UnityEditor.ShaderKeywordFilter;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

public static class MainGameEvent
{
    public static event Action SignalEvent;
    public static void RaiseSignal() => SignalEvent?.Invoke();
}

public enum GameResult
{
    NONE = 0,

    WIN_LOCAL = 1,
    WIN_REMOTE = 2,
    DOUBLE_KO = 3,

    FLYING_LOCAL = 4,
    FLYING_REMOTE = 5,
    FLYING_BOTH = 6,

    TIME_UP = 7,
}

public struct MainGameResultData
{
    public int finishFrame;
    public int signalFrame;
    public int pressFrame_local;
    public int pressFrame_remote;
    public GameResult gameResult;
}

public sealed class MainGameSystem
{
    private int _signalFrame;
    private XorShift32 _rng;
    const int afterSignalDuration = 90;
    int timeUpFrame = 0;
    const int minSignalFrame = 120;

    const int maxLife = 3;
    int life_local = maxLife;
    int life_remote = maxLife;

    public int roundCount { get; private set; } = 0;

    public void SetUpRound(int signal)
    {
        // 決定論：seedと計算式が同じなら、どの端末でも同じSignalFrameになる
        //_rng = new XorShift32(seed);
        //int r = (int)(_rng.Next() % (uint)Math.Max(1, randomFrameRange));

        _signalFrame = minSignalFrame + signal;
        timeUpFrame = _signalFrame + afterSignalDuration;

        //UnityEngine.Debug.Log($"next signal: {_signalFrame}");
    }

    public bool MainLoop(int currentFrame)
    {
        if (currentFrame >= timeUpFrame) return true;
        if (currentFrame == _signalFrame) MainGameEvent.RaiseSignal();
        return false;
    }

    public MainGameResultData CheckResult(int mainFrameCount, int localPressedFrame, int remotePressedFrame)
    {
        //フライング判定
        bool localFlying = CheckFlying(localPressedFrame);
        bool remoteFlying = CheckFlying(remotePressedFrame);


        // 結果判定（両者が押したら確定。もしくは任意のタイムアウトでも良い）
        var result = new MainGameResultData
        {
            finishFrame = mainFrameCount,
            signalFrame = _signalFrame,
            pressFrame_local = localFlying ? -2 : localPressedFrame,
            pressFrame_remote = remoteFlying ? -2 : remotePressedFrame,
            gameResult = CheckGameResult(),
        };

        //UnityEngine.Debug.Log($"{result.gameResult},{result.pressFrame_local},{result.pressFrame_remote}");

        return result;

        GameResult CheckGameResult()
        {
            //時間切れ
            if (localPressedFrame == -1 && remotePressedFrame == -1) return GameResult.TIME_UP;

            // 早押しがいる場合は即負け（簡易ルール）
            if (localFlying && !remoteFlying) return GameResult.FLYING_LOCAL;
            if (!localFlying && remoteFlying) return GameResult.FLYING_REMOTE;
            if (localFlying && remoteFlying) return GameResult.FLYING_BOTH;

            // 通常：押したフレームが小さい方が勝ち
            if (localPressedFrame >= 0 && remotePressedFrame == -1) return GameResult.WIN_LOCAL;
            if (remotePressedFrame >= 0 && localPressedFrame == -1) return GameResult.WIN_REMOTE;

            if (localPressedFrame < remotePressedFrame) return GameResult.WIN_LOCAL;
            if (localPressedFrame > remotePressedFrame) return GameResult.WIN_REMOTE;

            if (localPressedFrame == remotePressedFrame) return GameResult.DOUBLE_KO;

            return GameResult.NONE;
        }

        bool CheckFlying(int pressedFrame)
        {
            if (pressedFrame == -1) return false;
            return pressedFrame < _signalFrame;
        }
    }

    public bool CheckRestart(GameResult result)
    {
        switch(result)
        {
            case GameResult.FLYING_LOCAL:
            case GameResult.WIN_REMOTE:
                life_local--;
                break;
            case GameResult.FLYING_REMOTE:
            case GameResult.WIN_LOCAL:
                life_remote--;
                break;
            case GameResult.FLYING_BOTH:
            case GameResult.DOUBLE_KO:
            case GameResult.TIME_UP:
                life_local--;
                life_remote--;
                break;
        }

        roundCount++;

        return life_remote <= 0 || life_local <= 0;
    }

    private struct XorShift32
    {
        private uint _x;

        public XorShift32(uint seed)
        {
            _x = seed == 0 ? 2463534242u : seed;
        }

        public uint Next()
        {
            // 決定論：C#のuint演算はプラットフォーム差が出にくい
            uint x = _x;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _x = x;
            return x;
        }
    }
}