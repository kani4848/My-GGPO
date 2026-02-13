using Cysharp.Threading.Tasks;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;

using DG.Tweening.CustomPlugins;
using Mono.Cecil;
using System;
using System.Threading;
using System.Xml.Serialization;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;
using static UIManager_Main;

public class UIManager_Main : MonoBehaviour
{
    [Header("メインゲーム")]
    [SerializeField] GameObject mainCanvas;

    [SerializeField] GameObject keyGuides;
    [SerializeField] GameObject keyGuid_1p;
    [SerializeField] GameObject keyGuid_2p;

    [SerializeField] GameObject cutIn_p1;
    [SerializeField] GameObject cutIn_p2;

    [SerializeField] GameObject signal;
    [SerializeField] UnityEngine.UI.Image fillScreenCol_white;
    [SerializeField] UnityEngine.UI.Image fillScreenCol_black;

    [SerializeField] CharacterController_Main chara_p1;
    [SerializeField] CharacterController_Main chara_p2;

    [Header("ラウンドリザルト")]
    [SerializeField] GameObject roundResultUI;
    [SerializeField] TextMeshProUGUI roundResultMessageText;

    [SerializeField] Transform resultLogRoot_p1;
    [SerializeField] Transform resultLogRoot_p2;
    [SerializeField] GameObject resultLogPrefab;

    [Header("終了画面")]
    [SerializeField] GameObject winner;
    [SerializeField] UnityEngine.UI.Image hat_winner;
    [SerializeField] UnityEngine.UI.Image chara_winner;
    [SerializeField] UnityEngine.UI.Image uma_winner;
    [SerializeField] float umaMoveY= 50;
    [SerializeField] float umaRotate = 10;
    [SerializeField] float umaAnimDuration = 0.2f;
    [SerializeField] GameObject endCanvas;

    [Header("ソロモード")]
    [SerializeField] TextMeshProUGUI stageLeveText;
    [SerializeField] Button retryButton_solo;
    [SerializeField] Button goTitleButton_solo;

    [Header("ローカルモード")]
    [SerializeField] Button rematchButton_local;
    [SerializeField] Button goTitleButton_local;

    [Header("オンラインモード")]
    [SerializeField] Button quickMatchButton_online;
    [SerializeField] Button goLobbyButton_online;
    [SerializeField] Button goTitleButton_online;
    [SerializeField] GameObject searchingUI;

    bool walkAnim = true;

    private void Awake()
    {
        endCanvas.SetActive(false);
        mainCanvas.SetActive(true);

        roundResultUI.SetActive(false);
        keyGuides.SetActive(false);
        signal.SetActive(false);

        ActivateButtons_Local(false);
        ActivateButtons_Solo(false);

        stageLeveText.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        MainGameEvent.SignalEvent += OnSignal;
    }

    private void OnDisable()
    {
        MainGameEvent.SignalEvent -= OnSignal;
    }

    private void Update()
    {
        if (goTitleButton_solo.interactable)
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                retryButton_solo.onClick.Invoke();
                return;
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                goTitleButton_solo.onClick.Invoke();
                return;
            }
        }

        if (goTitleButton_local.interactable)
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                rematchButton_local.onClick.Invoke();
                return;
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                goTitleButton_local.onClick.Invoke();
                return;
            }
        }
    }

    //モード共通===============================================================================

    private void Start()
    {
        //終了画面の設定
        winner.transform.DOLocalMoveY(umaMoveY, umaAnimDuration)
            .SetRelative()
            .SetLoops(-1, LoopType.Yoyo);

        winner.transform.DOLocalRotate(new Vector3(0, 0, umaRotate), umaAnimDuration)
            .SetRelative()
            .SetLoops(-1, LoopType.Yoyo);
    }

    public void Init(GameMode mode, bool isOwner, PlayerImageData charaImageData_p1, PlayerImageData charaImageData_p2)
    {
        switch (mode)
        {
            case GameMode.Online:
                chara_p1.Init(isOwner, charaImageData_p1);
                chara_p2.Init(!isOwner, charaImageData_p2);
                break;

            case GameMode.Local:
                chara_p1.Init(false, charaImageData_p1);
                chara_p2.Init(false, charaImageData_p2);
                break;

            case GameMode.Solo:
                chara_p1.Init(true, charaImageData_p1, true);
                chara_p2.Init(false, charaImageData_p2, true);
                break;
        }

        ResetLogs();

        SetUpPlayerGuide();

        keyGuides.SetActive(false);
        roundResultUI.SetActive(false);

        void SetUpPlayerGuide()
        {
            switch (mode)
            {
                case GameMode.Online:
                    if (isOwner)
                    {
                        keyGuid_2p.SetActive(false);
                    }
                    else
                    {
                        keyGuid_1p.SetActive(false);
                        keyGuid_2p.GetComponentInChildren<TextMeshProUGUI>().text = "space key";
                    }
                    break;

                case GameMode.Local:
                    break;

                case GameMode.Solo:
                    keyGuid_2p.SetActive(false);
                    break;
            }
        }
    }
    public async UniTask RoundStart()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(1));

        if (walkAnim)
        {
            walkAnim = false;
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.WALK);
            chara_p1.WalkAnimation().Forget();
            await chara_p2.WalkAnimation();
        }
        else
        {
            await UniTask.Delay(TimeSpan.FromSeconds(1));
        }

        SoundManager.Instance.PlaySE(SE_Handler.SoundType.START);
        chara_p1.CutInAnimation().Forget();
        await chara_p2.CutInAnimation();

        keyGuides.SetActive(true);
    }

    void OnSignal()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SIGNAL);
        signal.SetActive(true);
    }

    public async UniTask OnWhiteOut()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SHOT);
        signal.SetActive(false);
        keyGuides.SetActive(false);

        await DOTween.Sequence()
            .Append(fillScreenCol_white.DOFade(1, 0))
            .Append(fillScreenCol_white.DOFade(0, 1))
            .AsyncWaitForCompletion()
            ;

    }

    public async UniTask OnPreResult(RoundResult result)
    {
        switch (result)
        {
            case RoundResult.WIN_P1:
            case RoundResult.WIN_P2:
            case RoundResult.DOUBLE_KO:
                await UniTask.Delay(TimeSpan.FromSeconds(1));
                break;
        }

        if (result != RoundResult.TIME_UP) SoundManager.Instance.PlaySE(SE_Handler.SoundType.DOWN);

        switch (result)
        {
            case RoundResult.NONE:
                break;
            case RoundResult.WIN_P1:
                chara_p2.GoDead();
                roundResultMessageText.text = "P1 WIN";
                break;

            case RoundResult.WIN_P2:
                chara_p1.GoDead();
                roundResultMessageText.text = "P2 WIN";
                break;

            case RoundResult.DOUBLE_KO:
                chara_p1.GoDead();
                chara_p2.GoDead();
                roundResultMessageText.text = "DOUBLE KO";
                break;

            case RoundResult.FLYING_P1:
                chara_p1.GoDead();
                roundResultMessageText.text = "P1 WAS MISFIRE";
                break;

            case RoundResult.FLYING_P2:
                chara_p2.GoDead();
                roundResultMessageText.text = "P2 WAS MISFIRE";
                break;
            case RoundResult.FLYING_BOTH:
                chara_p1.GoDead();
                chara_p2.GoDead();
                roundResultMessageText.text = "DOUBLE MISFIRE";
                break;

            case RoundResult.TIME_UP:
                roundResultMessageText.text = "TIME UP";
                break;
        }
        await UniTask.Delay(TimeSpan.FromSeconds(1));
    }

    public async UniTask OnResult(MainGameResultData resultData)
    {
        roundResultUI.SetActive(true);

        if (resultData.roundResult == RoundResult.TIME_UP)
        {
            signal.SetActive(false);

            chara_p1.OnTimeUp();
            chara_p2.OnTimeUp();

            CreateResultLog(resultLogRoot_p1, "--", false);
            CreateResultLog(resultLogRoot_p2, "--", false);

            await UniTask.Delay(TimeSpan.FromSeconds(2));
            return;
        }

        string localText = GetString(resultData.pressFrame_p1);
        string remoteText = GetString(resultData.pressFrame_p2);

        bool win_p1 = resultData.roundResult == RoundResult.WIN_P1 || resultData.roundResult == RoundResult.FLYING_P2;
        bool win_p2 = resultData.roundResult == RoundResult.WIN_P2 || resultData.roundResult == RoundResult.FLYING_P1;

        CreateResultLog(resultLogRoot_p1, localText, win_p1);
        CreateResultLog(resultLogRoot_p2, remoteText, win_p2);

        chara_p1.ShowLife(true);
        chara_p2.ShowLife(true);


        string GetString(int time)
        {
            switch (time)
            {
                default:
                    return (time - resultData.signalFrame).ToString();

                case -1:
                    return "--";

                case -2:
                    return "Mis";
            }
        }

        await UniTask.Delay(TimeSpan.FromSeconds(2));
    }

    void ResetLogs()
    {
        var logs_p1 = resultLogRoot_p1.GetComponentsInChildren<Transform>();
        foreach (Transform log in logs_p1)
        {
            if (log == resultLogRoot_p1.transform) continue;
            Destroy(log.gameObject);
        }

        var logs_p2 = resultLogRoot_p2.GetComponentsInChildren<Transform>();
        foreach (Transform log in logs_p2)
        {
            if (log == resultLogRoot_p2.transform) continue;
            Destroy(log.gameObject);
        }
    }

    void CreateResultLog(Transform logRoot, string time, bool win)
    {
        var obj = Instantiate(resultLogPrefab, logRoot);
        var texts = obj.GetComponentsInChildren<TextMeshProUGUI>();

        texts[0].text = time;
        texts[1].text = win ? "○" : "×";
    }

    public async UniTask OnRoundReset(bool _goBackChara, bool logReset)
    {
        await fillScreenCol_black.DOFade(1, 0.5f);

        ActivateButtons_Local(false);
        ActivateButtons_Solo(false);

        endCanvas.SetActive(false);
        mainCanvas.SetActive(true);
        roundResultUI.SetActive(false);

        if (_goBackChara)
        {
            walkAnim = true;
            chara_p1.StepBack();
            chara_p2.StepBack();
            chara_p1.OnRestart();
            chara_p2.OnRestart();
        }
        else
        {
            walkAnim = false;
            chara_p1.OnRestart();
            chara_p2.OnRestart();
        }

        if (logReset) ResetLogs();

        await UniTask.Delay(TimeSpan.FromSeconds(0.5f));
        await fillScreenCol_black.DOFade(0, 0.5f);
    }

    public async UniTask ExitScene()
    {
        await fillScreenCol_black.DOFade(1, 1.5f);
    }


    //ローカルモード===============================================================================
    void ActivateButtons_Local(bool active)
    {
        rematchButton_local.gameObject.SetActive(active);
        goTitleButton_local.gameObject.SetActive(active);

        rematchButton_local.interactable = active;
        goTitleButton_local.interactable = active;

        if (!active)
        {
            rematchButton_local.onClick.RemoveAllListeners();
            goTitleButton_local.onClick.RemoveAllListeners();
        }
    }

    public UniTask<bool> OnGameEnd_Local(MatchResult matchResult)
    {
        keyGuides.SetActive(false);
        roundResultUI.SetActive(true);

        switch (matchResult)
        {
            case MatchResult.WIN_P1:
            case MatchResult.WIN_P2:

                SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Title);
                mainCanvas.SetActive(false);
                endCanvas.SetActive(true);

                CharacterController_Main winnerController = matchResult == MatchResult.WIN_P1 ? chara_p1 : chara_p2;
                roundResultMessageText.text = matchResult == MatchResult.WIN_P1 ? "P1 survived!" : "P2 survived!";
                var imageData = winnerController.GetCharaImageData();
                hat_winner.color = imageData.hatCol;
                chara_winner.sprite = CharaImageHandler.Instance.GetCharaSpriteById(imageData.charaId);
                break;

            case MatchResult.DRAW:
                roundResultMessageText.text = "draw";
                break;
        }

        ActivateButtons_Local(true);

        var tcs = new UniTaskCompletionSource<bool>();

        rematchButton_local.onClick.AddListener(
            () => {
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                tcs.TrySetResult(true);
                ActivateButtons_Local(false);
            });

        goTitleButton_local.onClick.AddListener(
            () => {
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                tcs.TrySetResult(false);
                ActivateButtons_Local(false);
            });

        return tcs.Task;
    }


    //ソロモード===============================================================================
    void ActivateButtons_Solo(bool active)
    {
        retryButton_solo.gameObject.SetActive(active);
        goTitleButton_solo.gameObject.SetActive(active);

        retryButton_solo.interactable = active;
        goTitleButton_solo.interactable = active;

        if (!active)
        {
            retryButton_solo.onClick.RemoveAllListeners();
            goTitleButton_solo.onClick.RemoveAllListeners();
        }
    }

    public UniTask<bool> OnGameOver_Solo()
    {
        keyGuides.SetActive(false);
        roundResultUI.SetActive(true);
        roundResultMessageText.text = "game over";

        ActivateButtons_Solo(true);

        var tcs = new UniTaskCompletionSource<bool>();

        retryButton_solo.gameObject.SetActive(false);
        goTitleButton_solo.onClick.AddListener(
            () => {
                tcs.TrySetResult(true);
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                ActivateButtons_Local(false);
            });

        return tcs.Task;
    }

    public UniTask<bool> OnGameClear_Solo()
    {
        keyGuides.SetActive(false);
        roundResultUI.SetActive(true);

        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Title);

        mainCanvas.SetActive(false);
        endCanvas.SetActive(true);

        roundResultMessageText.text = "you are the gratest cow devil!";
        var imageData = chara_p1.GetCharaImageData();
        hat_winner.color = imageData.hatCol;
        chara_winner.sprite = CharaImageHandler.Instance.GetCharaSpriteById(imageData.charaId);
        uma_winner.color = imageData.umaCol;

        ActivateButtons_Solo(true);

        var tcs = new UniTaskCompletionSource<bool>();

        retryButton_solo.gameObject.SetActive(false);
        goTitleButton_solo.onClick.AddListener(
            () => {
                tcs.TrySetResult(true);
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                ActivateButtons_Local(false);
            });

        return tcs.Task;
    }

    public void UpdateCpuImage(PlayerImageData data)
    {
        chara_p2.UpdateCharaImage(data);
    }

    public UniTask<bool> WaitRetryRequest_Solo()
    {
        var tcs = new UniTaskCompletionSource<bool>();

        ActivateButtons_Solo(true);

        retryButton_solo.onClick.AddListener(
            () => {
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                ActivateButtons_Solo(false);
                tcs.TrySetResult(true);
            });

        goTitleButton_solo.onClick.AddListener(
            () => {
                SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
                ActivateButtons_Solo(false);
                tcs.TrySetResult(false);
            });

        return tcs.Task;
    }

    public async UniTask ShowSoloModeStageLevel(int cpuLv)
    {
        stageLeveText.gameObject.SetActive(true);
        stageLeveText.text = $"-stage {cpuLv}-";
        await UniTask.Delay(TimeSpan.FromSeconds(2f));
        stageLeveText.gameObject.SetActive(false);
    }


    //オンラインモード===============================================================================
    public UniTask<MainGameState> ActivateEndMenuButtons_Online()
    {
        quickMatchButton_online.gameObject.SetActive(true);
        goLobbyButton_online.gameObject.SetActive(true);
        goTitleButton_online.gameObject.SetActive(true);

        quickMatchButton_online.interactable = true;
        goLobbyButton_online.interactable = true;
        goTitleButton_online.interactable = true;

        var endMenuTask = new UniTaskCompletionSource<MainGameState>();

        quickMatchButton_online.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.QUICK_MATCH);
            DeacetivateEndMenuButtons_Online();
            searchingUI.SetActive(true);
        });

        goLobbyButton_online.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.GO_LOBBY);
            DeacetivateEndMenuButtons_Online();
        });

        goTitleButton_online.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.GO_TITLE); 
            DeacetivateEndMenuButtons_Online(); 
        });

        return endMenuTask.Task;
    }

    public void DeacetivateEndMenuButtons_Online()
    {
        quickMatchButton_online.gameObject.SetActive(false);
        goLobbyButton_online.gameObject.SetActive(false);
        goTitleButton_online.gameObject.SetActive(false);

        quickMatchButton_online.interactable = false;
        goLobbyButton_online.interactable = false;
        goTitleButton_online.interactable = false;

        quickMatchButton_online.onClick.RemoveAllListeners();
        goLobbyButton_online.onClick.RemoveAllListeners();
        goTitleButton_online.onClick.RemoveAllListeners();
    }
}
