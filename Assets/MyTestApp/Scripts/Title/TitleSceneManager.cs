using Cysharp.Threading.Tasks;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TitleState
{
    None,
    End,
}

public class TitleSceneManager : MonoBehaviour, ITitleSceneManager
{
    public TitleState state { get; private set; } = TitleState.None;
    [SerializeField] TextMeshProUGUI playerNameField;
    [SerializeField] Button onlineButton;
    [SerializeField] Button soloButton;
    [SerializeField] Button localButton;

    void Awake()
    {
        DeactivateButtons();
    }

    private void Start()
    {
        IGameManager.titleSceneManager = this;
        SoundManager.Instance.PlayBgm(BgmHandler.BgmType.Title);
        ActivateButtons();
    }

    public void ExitScene()
    {
        DeactivateButtons();
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.SHOT);
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

        return playerName == "" ? "no name" : playerName;
    }
}
