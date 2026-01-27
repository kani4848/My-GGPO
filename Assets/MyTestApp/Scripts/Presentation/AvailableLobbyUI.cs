using TMPro;
using UnityEngine;
using Epic.OnlineServices.Lobby;
using NUnit.Framework;
using PlayEveryWare.EpicOnlineServices.Samples;
using System.Collections.Generic;

public class AvairableLobbyUI : MonoBehaviour
{

    [SerializeField] private RectTransform contentRoot; // VerticalLayoutGroupêÑèß
    [SerializeField] private UnityEngine.UI.Button buttonPrefab;
    
    [SerializeField] UnityEngine.UI.Button createBtn;
    [SerializeField] UnityEngine.UI.Button searchBtn;
    [SerializeField] UnityEngine.UI.Button refleshBtn;

    [SerializeField] TMP_InputField lobbyPath_create;
    [SerializeField] TMP_InputField lobbyPath_search;


    public void Activated()
    {
        gameObject.SetActive(true);
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
    }
    
    public void RefreshList(List<LobbyData> lobbyDatas)
    {
        foreach (var lobbyData in lobbyDatas)
        {
            CreateLobbyButton(lobbyData);
        }

        ActivatedButtons();
    }

    private void CreateLobbyButton(LobbyData lobbyData)
    {
        var btn = Instantiate(buttonPrefab, contentRoot);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();

        text.text = $"{lobbyData.id} ({lobbyData.avairableSlots}/{lobbyData.maxLobbyMembers})";

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            lobbyData.joinAction();
        });
    }

    public void ClearUI()
    {
        if (contentRoot == null) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(contentRoot.GetChild(i).gameObject);
        }

        DeactivatedButtons();
    }

    public string GetLobbyPath_Create()
    {
        return lobbyPath_create.text;
    }

    public string GetLobbyPath_Search()
    {
        return lobbyPath_search.text;
    }


    void DeactivatedButtons()
    {
        createBtn.interactable = false;
        searchBtn.interactable = false;
        refleshBtn.interactable = false;
    }

    void ActivatedButtons()
    {
        createBtn.interactable = true;
        searchBtn.interactable = true;
        refleshBtn.interactable = true;
    }
}
