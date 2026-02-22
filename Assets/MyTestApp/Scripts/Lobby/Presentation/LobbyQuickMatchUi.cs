using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyQuickMatchUi : MonoBehaviour
{
    [SerializeField] LobbyMemberNamePlate namePlate_local;
    [SerializeField] LobbyMemberNamePlate namePlate_remote;
   
    [SerializeField] TextMeshProUGUI loadingText;
    [SerializeField] GameObject loadingUI;

    [SerializeField] Button cancelBtn;

    private void OnDisable()
    {
        DeativatedButton();
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.X) && cancelBtn.interactable)
        {
            cancelBtn.onClick.Invoke();
        }
    }

    public void Activate(PlayerData localPlayerData)
    {
        gameObject.SetActive(true);
        loadingUI.SetActive(true);
        namePlate_local.gameObject.SetActive(true);
        namePlate_remote.gameObject.SetActive(false);
        ActivateButton();
        ChangeLoadingText("searching the bounty heads...");
        namePlate_local.UpdateImage(localPlayerData);
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
        DeativatedButton();
    }

    public void ChangeLoadingText(string text)
    {
        loadingText.text = text;
    }

    public void OnFindOpponent(PlayerData remotePlayerData)
    {
        loadingUI.SetActive(false);
        namePlate_remote.gameObject.SetActive(true);
        namePlate_remote.UpdateImage(remotePlayerData);
    }

    public void ActivateButton()
    {
        cancelBtn.interactable = true;
        cancelBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseCancelQuickMatch();
            DeativatedButton();
        });
    }

    public void DeativatedButton()
    {
        cancelBtn.interactable = false;
        cancelBtn.onClick.RemoveAllListeners();
    }
}
