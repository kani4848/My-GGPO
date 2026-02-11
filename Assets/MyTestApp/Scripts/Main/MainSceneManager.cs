using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

public class MainSceneManager : MonoBehaviour, IMainSceneManager
{
    public MainGameState state { get; set; } = MainGameState.NONE;
    [SerializeField] UIManager_Main uiManager;

    MainGameSystem gameSystem;
    MainFlow_p2pTest p2p;
    CancellationTokenSource cts;

    int mainGameFrameCount = 0;

    System.Random rand;

    Action<int> fixedUpdateAction = null;

    private void OnDisable()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    GameMode mode;

    IEosService eosService;
    ICharaImageHandler charaImageHandler;

    private void Awake()
    {
        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Main);
        Application.targetFrameRate = 60;
        IGameManager.mainSceneManager = this;
        gameSystem = new();
    }

    public async UniTask StartFlow(ICharaImageHandler _charaImageHandler, IEosService _eosService, GameMode gameMode)
    {
        state = MainGameState.INITIALIZE;

        charaImageHandler = _charaImageHandler;
        mode = gameMode;
        eosService = _eosService;

        if (mode == GameMode.Online)
        {
            rand = await GetRandWithSeed();
        }
        else
        {
            rand = new System.Random();
        }

        var charaData_local = charaImageHandler.GetCharaImageData_local();
        var charaData_random = mode == GameMode.Solo ?
            charaImageHandler.GetCpuImageDataByLevel(0) : charaImageHandler.GetCharaImageData_notLocal();

        uiManager.Init(mode, true, charaData_local, charaData_random);

        switch (mode)
        {
            case GameMode.Online:
                break;
            case GameMode.Solo:
                await GameLoop_Solo();
                break;
            case GameMode.Local:
                await GameLoop_Local();
                break;
            default:
                //await UniTask.Delay(10);
                break;
        }
    }

    public async UniTask<System.Random> GetRandWithSeed()
    {
        uint seed;
        
        p2p = new MainFlow_p2pTest();
        p2p.Init();
        if (p2p.isOwner)
        {
            seed = p2p.CreateAndSendSeed();
            await p2p.WaitForSeedReply();
        }
        else
        {
            seed = await p2p.WaitForRecievingSeed();
        }

        return new System.Random((int)seed);
    }

    void RoundSetUp()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = new();

        shotFrame_p1 = -1;
        shotFrame_p2 = -1;
        mainGameFrameCount = 0;

        //ラウンドセットアップ。相手と通信できるまで待機
        state = MainGameState.ROUND_SETUP;

        int nextSignal = rand.Next(0, 180);
        gameSystem.SetUpRound(nextSignal);
    }

    async UniTask WaitRoundReady()
    {

        p2p.SendRoundReadyMsg();//切り替え部分
        await p2p.WaitAndRecieveReady();//切り替え部分4
    }

    async UniTask RoundStart()
    {
        //ゲーム開始演出
        state = MainGameState.ROUND_START;
        await uiManager.RoundStart();
    }

    async UniTask GameLoop_Solo()
    {
        triggerAction_1p = () => UnityEngine.Input.GetKeyDown(KeyCode.Z);
        triggerAction_2p = () => mainGameFrameCount == gameSystem.GetCpuTriggerFrame();

        bool gameClear = false;
        bool gameOver = false;
        bool win = true;
        int cpuLv = 0;

        while (true)
        {
            RoundSetUp();
            if (win) await uiManager.ShowSoloModeStageLevel(cpuLv + 1);
            await RoundStart();
            await MainLoop(cts.Token);

            //白飛びステート
            if (state == MainGameState.SHOT_WHITE_OUT)
            {
                await WhiteOut();
            }

            var roundResult = await Result_Local();

            switch (roundResult.roundResult)
            {
                //勝利
                case RoundResult.WIN_P1:
                    win = true;
                    cpuLv = gameSystem.CpuLevelUp();
                    gameClear = cpuLv == -1;
                    break;

                //負け：
                case RoundResult.WIN_P2:
                case RoundResult.FLYING_P1:
                case RoundResult.DOUBLE_KO:
                    win = false;
                    var lifeLeft = gameSystem.LoseSoloModeLife();
                    gameOver = lifeLeft <= 0;

                    if (gameOver)
                    {
                        await uiManager.OnGameOver_Solo();
                        break;
                    }

                    //リトライメニュー表示
                    bool retry = await uiManager.WaitRetryRequest_Solo();
                    if (!retry) gameOver = true;
                    break;
            }

            if (gameOver || gameClear) break;

            await uiManager.OnRoundReset(win, false);

            if (win)
            {
                var cpuImageData = charaImageHandler.GetCpuImageDataByLevel(cpuLv);
                uiManager.UpdateCpuImage(cpuImageData);
            }
        }

        state = MainGameState.END_MENU;
        if(gameClear) await uiManager.OnGameClear_Solo();
    }

    async UniTask GameLoop_Local()
    {
        triggerAction_1p = () => UnityEngine.Input.GetKeyDown(KeyCode.Z);
        triggerAction_2p = () => UnityEngine.Input.GetKeyDown(KeyCode.P);

        while (true)
        {
            MatchResult matchResult = MatchResult.NONE;

            while (true)
            {
                RoundSetUp();
                await RoundStart();
                
                await MainLoop(cts.Token);

                //白飛びステート
                if (state == MainGameState.SHOT_WHITE_OUT)
                {
                    await WhiteOut();
                }

                var result = await Result_Local();

                matchResult = gameSystem.CheckMatchResult(result.roundResult);
                if (matchResult != MatchResult.NONE) break;

                await uiManager.OnRoundReset(false, false);
            }

            state = MainGameState.END_MENU;

            bool rematch = await uiManager.OnGameEnd_Local(matchResult);

            if (rematch)
            {
                gameSystem.OnRematch();
                await uiManager.OnRoundReset(true, true);
                state = MainGameState.ROUND_SETUP;
            }
            else
            {
                state = MainGameState.GO_TITLE;
                break;
            }
        }
    }


    int shotFrame_p1 = -1;
    int shotFrame_p2 = -1;
    Func<bool> triggerAction_1p;
    Func<bool> triggerAction_2p;

    async UniTask MainLoop(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;

        bool timeUp = false;

        fixedUpdateAction = (frame) =>
        {
            if (shotFrame_p1 == -1&& triggerAction_1p())
            {
                shotFrame_p1 = frame;
            }

            if (shotFrame_p2 == -1 && triggerAction_2p())
            {
                shotFrame_p2 = frame;
            }

            timeUp = gameSystem.RaiseTimeUp(mainGameFrameCount);

            if (gameSystem.RaiseSignal(frame))MainGameEvent.RaiseSignal();

            mainGameFrameCount++;
        };

        await UniTask.WaitUntil(() => shotFrame_p1 != -1 || shotFrame_p2  != -1 || timeUp, cancellationToken: token);

        state = timeUp ? MainGameState.RESULT : MainGameState.SHOT_WHITE_OUT;
    }

    async UniTask<MainGameResultData> Result_Local()
    {
        //リザルトステート
        state = MainGameState.RESULT;
        fixedUpdateAction = null;
        MainGameResultData result = gameSystem.CheckResult(mainGameFrameCount, shotFrame_p1, shotFrame_p2);
        await uiManager.OnPreResult(result.roundResult);
        await uiManager.OnResult(result);
        return result;
    }

    async UniTask WhiteOut()
    {
        fixedUpdateAction = (frame) =>
        {
            if (shotFrame_p1 == -1 && triggerAction_1p())
            {
                shotFrame_p1 = mainGameFrameCount;
            }

            if (shotFrame_p2 == -1 && triggerAction_2p())
            {
                shotFrame_p2 = mainGameFrameCount;
            }

            mainGameFrameCount++;
        };

        await uiManager.OnWhiteOut();
    }

    
    /*
    async UniTask RoundReset()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(2));

        //モードユニーク部分＝＝＝＝＝
        p2p?.OnRoundReset();
        //モードユニーク部分＝＝＝＝＝


        await uiManager.OnRoundReset();
    }
    */

    void FixedUpdate()
    {
        fixedUpdateAction?.Invoke(mainGameFrameCount);

        /*
        switch (state)
        {
            case MainGameState.MAIN_GAME:
                bool timeUp = gameSystem.MainLoop(mainGameFrameCount);

                if (timeUp)
                {
                    state = MainGameState.RESULT;
                    return;
                }

                //ユニーク
                bool pressed_local = p2p.SendAndSaveLocalInput(mainGameFrameCount);
                bool pressed_remote = p2p.RecieveAndSaveRemoteInput();
                //ユニーク

                if (pressed_local || pressed_remote)
                {
                    state = MainGameState.SHOT_WHITE_OUT;
                    return;
                }

                mainGameFrameCount++;
                break;

            //ショット後のホワイトアウト中にも入力を記録、誤差修正
            case MainGameState.SHOT_WHITE_OUT:

                //ユニーク
                p2p.SendAndSaveLocalInput(mainGameFrameCount);
                p2p.RecieveAndSaveRemoteInput();
                //ユニーク

                mainGameFrameCount++;
                break;
        }
         */
    }

    async UniTask GameLoop_Online()
    {
        while (true)
        {
            RoundSetUp();

            await WaitRoundReady();

            await RoundStart();

            await MainLoop_Online(cts.Token);

            //白飛びステート
            if (state == MainGameState.SHOT_WHITE_OUT)
            {
                await WhiteOut();
            }

            var result = await Result_Online();


            //await RoundReset();
        }

        //勝利画面
        //ロビー、タイトル、クイックマッチするか選択
        //クイックマッチならマッチング処理
    }

    async UniTask MainLoop_Online(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;
        Peer_Online peer = new Peer_Online(PeerType.Local, eosService);
        fixedUpdateAction = peer.MainLoop;

        await UniTask.WaitUntil(() => peer.shot, cancellationToken: token);
    }
    async UniTask<MainGameResultData> Result_Online()
    {
        //リザルトステート
        state = MainGameState.RESULT;

        //モードユニーク部分＝＝＝＝＝
        var pressedFrameData = p2p.GetBothInput();
        //モードユニーク部分＝＝＝＝＝

        MainGameResultData result = gameSystem.CheckResult(mainGameFrameCount, pressedFrameData.local, pressedFrameData.remote);

        await uiManager.OnPreResult(result.roundResult);
        await uiManager.OnResult(result);

        return result;
    }

    public void GoLobby()
    {
        state = MainGameState.GO_LOBBY;
    }

}
