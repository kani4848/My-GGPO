using Cysharp.Threading.Tasks;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System;

public class TitleSceneManager : MonoBehaviour, ITitleSceneManager
{
    public TitleState state { get;  set; } = TitleState.None;
    [SerializeField] TMP_InputField playerNameField;
    [SerializeField] Button onlineButton;
    [SerializeField] Button soloButton;
    [SerializeField] Button localButton;

    [SerializeField] Image hat;
    [SerializeField] Image chara;
    [SerializeField] Image uma;

    ICharaImageHandler charaHandler;

    void Awake()
    {
        DeactivateButtons();
        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Title);
        IGameManager.titleSceneManager = this;

        hat.gameObject.SetActive(false);
        chara.gameObject.SetActive(false);
        uma.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!onlineButton.interactable) return;
    
        if (Input.GetKeyDown(KeyCode.Z))
        {
            onlineButton.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            soloButton.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            localButton.onClick.Invoke();
            return;
        }
    }

    public void Init(PlayerData playerData)
    {
        var charaImageData = new PlayerImageData(playerData.charaId, playerData.hatCol, playerData.umaCol);

        hat.color = charaImageData.hatCol;
        chara.sprite = CharaImageHandler.Instance.GetCharaSpriteById(charaImageData.charaId);
        uma.color = charaImageData.umaCol;


        hat.gameObject.SetActive(true);
        chara.gameObject.SetActive(true);
        uma.gameObject.SetActive(true);

        ActivateButtons();
    }

    public void GoOnline()
    {
        ExitScene(TitleState.GoOnline).Forget();

    }

    public void GoLocal() 
    {
        ExitScene(TitleState.GoLocal).Forget();

    }
    public void GoSolo() 
    {
        ExitScene(TitleState.GoSolo).Forget();
    }

    [SerializeField] Image black;

    async UniTask ExitScene(TitleState _state)
    {
        DeactivateButtons();

        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SHOT);
        await DOTween.Sequence()
            .Append(black.DOFade(1, 1))
            .AsyncWaitForCompletion()
            ;

        state = _state;
    }

    void DeactivateButtons()
    {
        onlineButton.interactable = false;
        soloButton.interactable = false;
        localButton.interactable = false;
    }

    void ActivateButtons()
    {
        onlineButton.interactable = true;
        soloButton.interactable = true;
        localButton.interactable = true;
    }

    public string GetPlayerName()
    {
        string playerName = playerNameField.text;

        //結果＞0 : true ; false : true : true
       // Debug.Log($"{playerNameField.text.Length}:{playerNameField.text == ""}:{playerNameField.text == default}:{string.IsNullOrWhiteSpace(playerNameField.text)}:{string.IsNullOrEmpty(playerNameField.text)}");

        return playerName == "" ? "no name" : playerName;
    }
}
