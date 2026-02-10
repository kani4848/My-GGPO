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

    private void Awake()
    {
        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Main);
        Application.targetFrameRate = 60;
        IGameManager.mainSceneManager = this;
        gameSystem = new();
    }

    public async UniTask StartFlow(IEosService _eosService, GameMode gameMode)
    {
        state = MainGameState.INITIALIZE;

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

        InitWithRand();

        switch (mode)
        {
            case GameMode.Online:
                break;
            case GameMode.Solo:
                //await StartSoloMode();
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

    void InitWithRand()
    {
        int chara_p1 = rand.Next(0, 13);
        int chara_p2 = rand.Next(0, 13);
        if (chara_p1 == chara_p2)
        {
            chara_p2 = chara_p1 + 1 > 12 ? 0 : chara_p1 + 1;
        }

        //uiManager.Init(p2p.isOwner, chara_p1, chara_p2);
        uiManager.Init(true, chara_p1, chara_p2);
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

            if (gameSystem.CheckRestart(result.gameResult)) break;

            await RoundReset();
        }

        //勝利画面
        //ロビー、タイトル、クイックマッチするか選択
        //クイックマッチならマッチング処理
    }

    async UniTask GameLoop_Solo()
    {
        while (true)
        {
            RoundSetUp();

            await RoundStart();

            await MainLoop_Solo(cts.Token);

            //白飛びステート
            if (state == MainGameState.SHOT_WHITE_OUT)
            {
                await WhiteOut();
            }

            var result = await Result_Online();

            if (gameSystem.CheckRestart(result.gameResult)) break;

            await RoundReset();

            //ステージアップデート
        }

        //プレイヤー敗北ならリトライ画面
        //ゲームクリアなら勝利画面
    }



    void RoundSetUp()
    {
        cts = new CancellationTokenSource();

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
        await uiManager.RoundStart(gameSystem.roundCount == 0);
    }

    async UniTask MainLoop_Online(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;
        Peer_Online peer = new Peer_Online(PeerType.Local, eosService);
        fixedUpdateAction = peer.MainLoop;

        await UniTask.WaitUntil(() => peer.shot, cancellationToken: token);
    }


    async UniTask GameLoop_Local()
    {
        while (true)
        {
            while (true)
            {
                RoundSetUp();

                await RoundStart();

                await MainLoop_Local(cts.Token);

                //白飛びステート
                if (state == MainGameState.SHOT_WHITE_OUT)
                {
                    await WhiteOut();
                }

                var result = await Result_Local();

                await RoundReset();

                if (gameSystem.CheckRestart(result.gameResult)) break;

            }

            state = MainGameState.END_MENU;
            SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Title);
            uiManager.OnGameEnd(UIManager_Main.PlayerSide.P1);

            await UniTask.WaitUntil(() => state != MainGameState.END_MENU, cancellationToken: cts.Token);

            if (state == MainGameState.GO_TITLE) break;
        }
    }


    int shotFrame_p1 = -1;
    int shotFrame_p2 = -1;

    async UniTask MainLoop_Local(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;

        bool timeUp = false;

        fixedUpdateAction = (frame) =>
        {
            if (shotFrame_p1 == -1&& UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                shotFrame_p1 = mainGameFrameCount;
            }

            if (shotFrame_p2 == -1 && UnityEngine.Input.GetKeyDown(KeyCode.Return))
            {
                shotFrame_p2 = mainGameFrameCount;
            }

            timeUp = gameSystem.MainLoop(mainGameFrameCount);

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

        await uiManager.OnPreResult(result.gameResult);
        uiManager.OnResult(result);

        return result;
    }

    async UniTask MainLoop_Solo(CancellationToken token)
    {
        state = MainGameState.MAIN_GAME;
        int shotFrame = -1;

        fixedUpdateAction = (frame) =>
        {
            bool shot_player = UnityEngine.Input.GetKeyDown(KeyCode.Space);
            bool shot_cpu = false;
            shotFrame = frame;
        };

        await UniTask.WaitUntil(() => shotFrame != -1, cancellationToken: token);
    }

    async UniTask WhiteOut()
    {
        fixedUpdateAction = (frame) =>
        {
            if (shotFrame_p1 == -1 && UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                shotFrame_p1 = mainGameFrameCount;
            }

            if (shotFrame_p2 == -1 && UnityEngine.Input.GetKeyDown(KeyCode.Return))
            {
                shotFrame_p2 = mainGameFrameCount;
            }

            mainGameFrameCount++;
        };

        await uiManager.OnWhiteOut();
    }

    async UniTask<MainGameResultData> Result_Online()
    {
        //リザルトステート
        state = MainGameState.RESULT;

        //モードユニーク部分＝＝＝＝＝
        var pressedFrameData = p2p.GetBothInput();
        //モードユニーク部分＝＝＝＝＝

        MainGameResultData result = gameSystem.CheckResult(mainGameFrameCount, pressedFrameData.local, pressedFrameData.remote);

        await uiManager.OnPreResult(result.gameResult);
        uiManager.OnResult(result);

        return result;
    }


    async UniTask RoundReset()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(2));

        shotFrame_p1 = -1;
        shotFrame_p2 = -1;

        //モードユニーク部分＝＝＝＝＝
        p2p?.OnRoundReset();
        //モードユニーク部分＝＝＝＝＝

        cts?.Cancel();
        cts?.Dispose();
        cts = null;

        mainGameFrameCount = 0;
        await uiManager.OnRoundReset();
    }

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

    public void Rematch()
    {
        state = MainGameState.ROUND_SETUP;
    }

    public void GoLobby()
    {
        state = MainGameState.GO_LOBBY;
    }

    public void GoTitle()
    {
        state = MainGameState.GO_TITLE;
    }
}
