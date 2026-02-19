using Cysharp.Threading.Tasks;
using GluonGui.Dialog;
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

    int sceneFrameCount = 0;//リモートとスタートを合わせるためのフレーム
    int mainGameFrameCount = 0;

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

    private void Start()
    {
        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Main);
        Application.targetFrameRate = 60;
        IGameManager.mainSceneManager = this;
        gameSystem = new();
    }

    public async UniTask<MainGameState> StartFlow
        (IEosService _eosService, ICharaImageHandler _charaImageHandler, GameMode gameMode)
    {
        state = MainGameState.INITIALIZE;

        charaImageHandler = _charaImageHandler;
        mode = gameMode;
        eosService = _eosService;

        PlayerImageData opponentImageData = new();

        switch (mode)
        {
            case GameMode.Online:
                var remotePlayerData = eosService.GetRemotePlayerData();
                opponentImageData= remotePlayerData.imageData;
                break;

            case GameMode.Solo:
                opponentImageData.charaId = 0;
                break;
        }
        
        uiManager.Init(mode, true, eosService.GetLocalPlayerData().imageData, opponentImageData);

        switch (mode)
        {
            case GameMode.Online:
                await GameLoop_Online();
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

        await uiManager.ExitScene();

        return state;
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

            gameSystem.CreateRoundData();

            if (win) await uiManager.ShowSoloModeStageLevel(cpuLv + 1);
            await RoundStart();
            bool flying =  await MainLoop(cts.Token);

            //白飛びステート
            if (state == MainGameState.SHOT_WHITE_OUT)
            {
                await WhiteOut(flying);
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
                var cpuImageData = new PlayerImageData (cpuLv);
                uiManager.UpdateCpuImage(cpuImageData);
            }
        }

        state = MainGameState.END_MENU;
        if(gameClear) await uiManager.OnGameClear_Solo();
    }

    //ローカルモード==================================================

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

                gameSystem.CreateRoundData();

                await RoundStart();
                bool flying = await MainLoop(cts.Token);

                //白飛びステート
                if (state == MainGameState.SHOT_WHITE_OUT)
                {
                    await WhiteOut(flying);
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


    //オンラインモード==================================================
    async UniTask GameLoop_Online()
    {
        triggerAction_1p = () =>
        {
            bool p1Input = UnityEngine.Input.GetKeyDown(KeyCode.Z);
            eosService.SendInput(mainGameFrameCount, p1Input);
            return p1Input;
        };

        triggerAction_2p = () => eosService.GetRemoteInputImmediately().input;

        while (true)
        {
            MatchResult matchResult = MatchResult.NONE;

            //対戦ループ
            while (true)
            {
                RoundSetUp();

                RoundData roundData;
                bool roundDataShared = false;

                if (eosService.GetLocalPlayerData().isOwner)
                {
                    roundData = gameSystem.CreateRoundData();
                    roundDataShared = await eosService.SendRoundDataAndWaitAck(roundData, cts.Token);//リモートと足並みをそろえる
                }
                else
                {
                    roundData = await eosService.WaitReceivingRoundData(cts.Token);
                    roundDataShared = roundData != null;
                }

                if (!roundDataShared)
                {
                    Debug.Log("シグナル共有失敗");
                    await OnError();
                    return;
                }

                Debug.Log("シグナル共有成功");
                gameSystem.SetRoundData(roundData);

                await UniTask.WaitUntil(
                    () => gameSystem.RoundStart(sceneFrameCount), PlayerLoopTiming.FixedUpdate ,cancellationToken: cts.Token);
                await RoundStart();
                bool flying = await MainLoop(cts.Token);

                //白飛びステート
                if (state == MainGameState.SHOT_WHITE_OUT)
                {
                    await WhiteOut(flying);
                }

                //インプットデータを共有
                shotFrame_p2 = await eosService.SendFinalInputAndWaitRemoteFinalInput(cts.Token);
                if(shotFrame_p2 == -2)
                {
                    Debug.Log("インプットフレーム共有失敗");
                    await OnError();
                    return;
                }

                var result = await ConfirmResult_Online();

                matchResult = gameSystem.CheckMatchResult(result.roundResult);
                if (matchResult != MatchResult.NONE) break;

                eosService.OnRoundReset();//ピアに保存しているインプットデータをリセット

                await uiManager.OnRoundReset(false, false);
            }

            state = MainGameState.END_MENU;

            //エンド画面ループ
            while (true)
            {
                uiManager.ShowGameEndScreen_Online(matchResult);
                await eosService.CloseConnection();
                state = await uiManager.OnGameEnd_Online();

                if (state != MainGameState.QUICK_MATCH) return;//クイックマッチしないならループ終了
                bool findPlayer = await eosService.StartQuickMatch();

                if (findPlayer)
                {
                    //見つかったらbreak;
                    break;
                }
                else
                {
                    //タイムアウトならループ
                    continue;
                }
            }
        }
    }

    async UniTask<MainGameResultData> ConfirmResult_Online()
    {
        state = MainGameState.RESULT;
        fixedUpdateAction = null;

        MainGameResultData result = gameSystem.CheckResult(mainGameFrameCount, shotFrame_p1, shotFrame_p2);
        await uiManager.OnPreResult(result.roundResult);
        await uiManager.OnResult(result);
        return result;
    }

    async UniTask OnError()
    {
        uiManager.ShowErrorWindow();
        await eosService.CloseConnection();
        state = MainGameState.GO_LOBBY;

        triggerAction_1p = null;
        triggerAction_2p = null;

        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    //モード共通==================================================
    int shotFrame_p1 = -1;
    int shotFrame_p2 = -1;
    Func<bool> triggerAction_1p;
    Func<bool> triggerAction_2p;

    async UniTask<bool> MainLoop(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;

        bool timeUp = false;
        bool signal = false;

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

            if (gameSystem.RaiseSignal(frame))
            {
                signal = true;
                MainGameEvent.RaiseSignal();
            }

            mainGameFrameCount++;
        };

        await UniTask.WaitUntil(() => shotFrame_p1 != -1 || shotFrame_p2  != -1 || timeUp, cancellationToken: token);

        state = timeUp ? MainGameState.RESULT : MainGameState.SHOT_WHITE_OUT;
        
        //フライングを判定
        return !signal && state == MainGameState.SHOT_WHITE_OUT;
    }

    async UniTask WhiteOut(bool flying)
    {
        fixedUpdateAction = (frame) =>
        {
            if (flying) return;
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

    void FixedUpdate()
    {
        fixedUpdateAction?.Invoke(mainGameFrameCount);
        if (mode == GameMode.Online) sceneFrameCount++;
    }
}
