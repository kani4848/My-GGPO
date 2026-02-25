using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUi_QuickMatch : MonoBehaviour
{
    [SerializeField] LobbyMemberNamePlate namePlate_local;
    [SerializeField] LobbyMemberNamePlate namePlate_remote;
   
    [SerializeField] TextMeshProUGUI loadingText;
    [SerializeField] GameObject loadingUI;

    [SerializeField] Button cancelBtn;

    private void OnDisable()
    {
        DeactivatedButton();
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

    public void UpdateNamePlate(PlayerData memberData)
    {
        if (namePlate_local.puid.text == memberData.puid.ToString()) return;

        namePlate_remote.UpdateImage(memberData);
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
        DeactivatedButton();
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
        DeactivatedButton();
    }

    public void ActivateButton()
    {
        cancelBtn.interactable = true;
        cancelBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseCancelQuickMatch();
            DeactivatedButton();
        });
    }

    public void DeactivatedButton()
    {
        cancelBtn.interactable = false;
        cancelBtn.onClick.RemoveAllListeners();
    }
}
