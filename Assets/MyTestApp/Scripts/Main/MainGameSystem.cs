using System;

public static class MainGameEvent
{
    public static event Action SignalEvent;
    public static void RaiseSignal() => SignalEvent?.Invoke();
}

public enum RoundResult
{
    NONE = 0,

    WIN_P1 = 1,
    WIN_P2 = 2,
    DOUBLE_KO = 3,

    FLYING_P1 = 4,
    FLYING_P2 = 5,
    FLYING_BOTH = 6,

    TIME_UP = 7,
}

public enum MatchResult
{
    NONE = 0,
    WIN_P1 = 1,
    WIN_P2 = 2,
    DRAW = 3,
}

public struct MainGameResultData
{
    public int finishFrame;
    public int signalFrame;
    public int pressFrame_p1;
    public int pressFrame_p2;
    public RoundResult roundResult;
}

public sealed class MainGameSystem
{
    private int _signalFrame;
    const int afterSignalDuration = 90;
    int timeUpFrame = 0;
    const int minSignalFrame = 120;

    const int maxLife = 3;
    int life_p1 = maxLife;
    int life_p2 = maxLife;

    public PlayerSide winnerSide { get; private set; } = PlayerSide.NONE;

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

    public bool RaiseTimeUp(int currentFrame)
    {
        return currentFrame == timeUpFrame;
    }

    public bool RaiseSignal(int currentFrame)
    {
        return currentFrame == _signalFrame;
    }

    public MainGameResultData CheckResult(int mainFrameCount, int pressedFrame_p1, int pressedFrame_p2)
    {
        //フライング判定
        bool fying_p1 = CheckFlying(pressedFrame_p1);
        bool flying_p2 = CheckFlying(pressedFrame_p2);


        // 結果判定（両者が押したら確定。もしくは任意のタイムアウトでも良い）
        var result = new MainGameResultData
        {
            finishFrame = mainFrameCount,
            signalFrame = _signalFrame,
            pressFrame_p1 = fying_p1 ? -2 : pressedFrame_p1,
            pressFrame_p2 = flying_p2 ? -2 : pressedFrame_p2,
            roundResult = CheckGameResult(),
        };

        return result;

        RoundResult CheckGameResult()
        {
            //時間切れ
            if (pressedFrame_p1 == -1 && pressedFrame_p2 == -1) return RoundResult.TIME_UP;

            // 早押しがいる場合は即負け（簡易ルール）
            if (fying_p1 && !flying_p2) return RoundResult.FLYING_P1;
            if (!fying_p1 && flying_p2) return RoundResult.FLYING_P2;
            if (fying_p1 && flying_p2) return RoundResult.FLYING_BOTH;

            // 通常：押したフレームが小さい方が勝ち
            if (pressedFrame_p1 >= 0 && pressedFrame_p2 == -1) return RoundResult.WIN_P1;
            if (pressedFrame_p2 >= 0 && pressedFrame_p1 == -1) return RoundResult.WIN_P2;

            if (pressedFrame_p1 < pressedFrame_p2) return RoundResult.WIN_P1;
            if (pressedFrame_p1 > pressedFrame_p2) return RoundResult.WIN_P2;

            if (pressedFrame_p1 == pressedFrame_p2) return RoundResult.DOUBLE_KO;

            return RoundResult.NONE;
        }

        bool CheckFlying(int pressedFrame)
        {
            if (pressedFrame == -1) return false;
            return pressedFrame < _signalFrame;
        }
    }

    public MatchResult CheckMatchResult(RoundResult result)
    {
        switch(result)
        {
            case RoundResult.FLYING_P1:
            case RoundResult.WIN_P2:
                life_p1--;
                break;
            case RoundResult.FLYING_P2:
            case RoundResult.WIN_P1:
                life_p2--;
                break;
            case RoundResult.FLYING_BOTH:
            case RoundResult.DOUBLE_KO:
            case RoundResult.TIME_UP:
                life_p1--;
                life_p2--;
                break;
        }

        roundCount++;

        if (life_p1 == 0 && life_p2 == 0) return MatchResult.DRAW;
        if (life_p1 == 0) return MatchResult.WIN_P2;
        if (life_p2 == 0) return MatchResult.WIN_P1;
        return MatchResult.NONE;
    }

    public void OnRematch()
    {
        roundCount = 0;
        life_p1 = maxLife;
        life_p2 = maxLife;
    }

    int cpuLv = 0;
    readonly int[] cpuTriggerFrames = { 
        90, //az
        60, //hel
        50, //cer1
        40, //cer2
        30, //mali
        25, //zd
        21, //pan
        19, //mod
        17, //luc
        15, //jud
        13, //bel
        11, //jus
        8,  //lor
            };

    public int GetCpuTriggerFrame()
    {
        return cpuTriggerFrames[cpuLv] + _signalFrame;
    }

    public int CpuLevelUp()
    {
        cpuLv++;
        return cpuLv >= cpuTriggerFrames.Length ?  -1 : cpuLv;
    }

    const int soloModeLife_max = 5;
    int currentSoloModeLife = soloModeLife_max;

    public int LoseSoloModeLife()
    {
        currentSoloModeLife--;
        return currentSoloModeLife;
    }
}