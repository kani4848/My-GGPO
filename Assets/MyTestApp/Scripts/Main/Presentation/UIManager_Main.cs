using Cysharp.Threading.Tasks;
using DG.Tweening;
using Mono.Cecil;
using System;
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
    public enum PlayerSide { P1, P2}
    [Header("メインゲーム")]
    [SerializeField] GameObject mainCanvas;

    [SerializeField] GameObject cutIn_p1;
    [SerializeField] GameObject cutIn_p2;

    [SerializeField] TextMeshProUGUI guidText;
    [SerializeField] GameObject resultMessage;
    TextMeshProUGUI resultText;

    [SerializeField] GameObject signal;
    [SerializeField] UnityEngine.UI.Image fillScreenCol_white;
    [SerializeField] UnityEngine.UI.Image fillScreenCol_black;

    [SerializeField] Transform resultLogRoot_p1;
    [SerializeField] Transform resultLogRoot_p2;
    Transform resultLogRoot_local;
    Transform resultLogRoot_remote;
    [SerializeField] GameObject resultLogPrefab;

    [SerializeField] CharacterController_Main chara_p1;
    [SerializeField] CharacterController_Main chara_p2;

    [SerializeField] CharacterController_Main chara_local;
    [SerializeField] CharacterController_Main chara_remote;

    [Header("メイン：ローカル")]
    [SerializeField] GameObject keyGuide_local;

    [Header("終了画面")]
    [SerializeField] GameObject winner;
    [SerializeField] UnityEngine.UI.Image hat_end;
    [SerializeField] UnityEngine.UI.Image chara_end;
    [SerializeField] UnityEngine.UI.Image uma;
    [SerializeField] float umaMoveY= 50;
    [SerializeField] float umaRotate = 10;
    [SerializeField] float umaAnimDuration = 0.2f;
    [SerializeField] GameObject endCanvas;
    [SerializeField] GameObject endMenu;

    private void Awake()
    {
        endCanvas.SetActive(false);
        mainCanvas.SetActive(true);
    }

    private void OnEnable()
    {
        MainGameEvent.SignalEvent += OnSignal;
    }

    private void OnDisable()
    {
        MainGameEvent.SignalEvent -= OnSignal;
    }

    public void Init(bool isOwner, int charaId_p1, int charaId_p2)
    {
        if (isOwner)
        {
            chara_p1.Init(true, charaId_p1);
            chara_p2.Init(false, charaId_p2);

            chara_local = chara_p1;
            chara_remote = chara_p2;
            resultLogRoot_local = resultLogRoot_p1;
            resultLogRoot_remote = resultLogRoot_p2;
        }
        else
        {
            chara_p1.Init(false, charaId_p1);
            chara_p2.Init(true, charaId_p2);

            chara_local = chara_p2;
            chara_remote = chara_p1;
            resultLogRoot_local = resultLogRoot_p2;
            resultLogRoot_remote = resultLogRoot_p1;
        }

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

        resultText = resultMessage.GetComponentInChildren<TextMeshProUGUI>();
        guidText.gameObject.SetActive(false);
        resultMessage.SetActive(false);


    }

    private void Start()
    {
        //終了画面の設定
        winner.transform.DOLocalMoveY(umaMoveY, umaAnimDuration)
            .SetRelative()
            .SetLoops(-1, LoopType.Yoyo);

        winner.transform.DOLocalRotate(new Vector3(0, 0, umaRotate), umaAnimDuration)
            .SetRelative()
            .SetLoops(-1, LoopType.Yoyo);

        uma.color = Color.plum;
    }

    public async UniTask RoundStart(bool IsFirstRound)
    {
        resultMessage.SetActive(false);
        await UniTask.Delay(TimeSpan.FromSeconds(1));

        if (IsFirstRound)
        {
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

        guidText.gameObject.SetActive(true);
    }

    void OnSignal()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SIGNAL);
        signal.SetActive(true);
        guidText.gameObject.SetActive(false);
    }

    public async UniTask OnWhiteOut()
    {
        guidText.gameObject.SetActive(false);

        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SHOT);
        signal.SetActive(false);
        
        await DOTween.Sequence()
            .Append(fillScreenCol_white.DOFade(1, 0))
            .Append(fillScreenCol_white.DOFade(0, 1))
            .AsyncWaitForCompletion()
            ;

        await UniTask.Delay(TimeSpan.FromSeconds(1));

    }

    public async UniTask OnPreResult(GameResult result)
    {
        
        switch (result)
        {
            case GameResult.NONE:
                break;
            case GameResult.WIN_LOCAL:
                chara_remote.GoDead();
                resultText.text = "P1 WIN";
                break;

            case GameResult.WIN_REMOTE:
                chara_local.GoDead();
                resultText.text = "P2 WIN";
                break;

            case GameResult.DOUBLE_KO:
                chara_p1.GoDead();
                chara_p2.GoDead();
                resultText.text = "DOUBLE KO";
                break;

            case GameResult.FLYING_LOCAL:
                chara_local.GoDead();
                resultText.text = "P1 WAS MISFIRE";
                break;

            case GameResult.FLYING_REMOTE:
                chara_remote.GoDead();
                resultText.text = "P2 WAS MISFIRE";
                break;
            case GameResult.FLYING_BOTH:
                chara_p1.GoDead();
                chara_p2.GoDead();
                resultText.text = "DOUBLE MISFIRE";
                break;

            case GameResult.TIME_UP:
                resultText.text = "TIME UP";
                break;
        }

        if (result != GameResult.TIME_UP) SoundManager.Instance.PlaySE(SE_Handler.SoundType.DOWN);

        await UniTask.Delay(TimeSpan.FromSeconds(1));
    }

    public void OnResult(MainGameResultData resultData)
    {
        if(resultData.gameResult == GameResult.TIME_UP)
        {
            signal.SetActive(false);

            chara_p1.OnTimeUp();
            chara_p2.OnTimeUp();
            resultText.text = "Time up";
            resultMessage.gameObject.SetActive(true);

            CreateResultLog(resultLogRoot_local, "--", false);
            CreateResultLog(resultLogRoot_remote, "--", false);
            return;
        }

        string localText = GetString(resultData.pressFrame_local);
        string remoteText = GetString(resultData.pressFrame_remote);

        bool localWin = resultData.gameResult == GameResult.WIN_LOCAL || resultData.gameResult == GameResult.FLYING_REMOTE;
        bool remoteWin = resultData.gameResult == GameResult.WIN_REMOTE|| resultData.gameResult == GameResult.FLYING_LOCAL;

        CreateResultLog(resultLogRoot_local, localText, localWin);
        CreateResultLog(resultLogRoot_remote, remoteText, remoteWin);

        resultLogRoot_p1.gameObject.SetActive(true);
        resultLogRoot_p2.gameObject.SetActive(true);

        chara_p1.ShowLife(true);
        chara_p2.ShowLife(true);
        resultMessage.gameObject.SetActive(true);


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

    }

    void CreateResultLog(Transform logRoot, string time, bool win)
    {
        var obj = Instantiate(resultLogPrefab, logRoot);
        var texts = obj.GetComponentsInChildren<TextMeshProUGUI>();

        texts[0].text = time;
        texts[1].text = win ? "○" : "×";
    }

    public async UniTask OnRoundReset()
    {
        fillScreenCol_black.DOFade(1, 1);
        await UniTask.Delay(TimeSpan.FromSeconds(2));

        resultMessage.SetActive(false);
        resultLogRoot_p1.gameObject.SetActive(false);
        resultLogRoot_p2.gameObject.SetActive(false);
        chara_p1.OnRestart();
        chara_p2.OnRestart();
        
        fillScreenCol_black.DOFade(0, 1);
    }

    public void OnGameEnd(PlayerSide playerSide)
    {
        CharacterController_Main winnerController = playerSide == PlayerSide.P1 ? chara_p1 : chara_p2;
        var imageData = winnerController.GetCharaImageData();

        hat_end.color = imageData.hatCol;
        chara_end.sprite = imageData.chara;

        mainCanvas.SetActive(false);
        endCanvas.SetActive(true);
        endMenu.SetActive(true);
    }
}