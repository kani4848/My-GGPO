using TMPro;
using UnityEngine;
using Epic.OnlineServices.Lobby;
using NUnit.Framework;
using PlayEveryWare.EpicOnlineServices.Samples;
using System.Collections.Generic;
using System;

public class AvairableLobbyUI : MonoBehaviour
{

    [SerializeField] private RectTransform contentRoot; // VerticalLayoutGroupêÑèß
    [SerializeField] private UnityEngine.UI.Button buttonPrefab;
    
    [SerializeField] UnityEngine.UI.Button createBtn;
    [SerializeField] UnityEngine.UI.Button searchBtn;
    [SerializeField] UnityEngine.UI.Button refleshBtn;
    [SerializeField] UnityEngine.UI.Button logOutBtn;

    [SerializeField] TMP_InputField lobbyPath_create;
    [SerializeField] TMP_InputField lobbyPath_search;

    [SerializeField] GameObject noLobbies;

    public void Activated()
    {
        gameObject.SetActive(true);
        noLobbies.SetActive(false);
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearUI();
    }
    
    public void RefreshList(List<LobbyData> lobbyDatas, Action<LobbyData> joinAction)
    {
        noLobbies.SetActive(lobbyDatas.Count <= 0);

        foreach (var lobbyData in lobbyDatas)
        {
            CreateLobbyButton(lobbyData, joinAction);
        }

        ActivatedButtons();
    }

    private void CreateLobbyButton(LobbyData lobbyData, Action<LobbyData> joinAction)
    {
        var btn = Instantiate(buttonPrefab, contentRoot);
        var text = btn.GetComponentInChildren<TextMeshProUGUI>();

        text.text = $"({lobbyData.avairableSlots}/{lobbyData.maxLobbyMembers})";

        btn.onClick.AddListener(() =>
        {
            joinAction(lobbyData);
        });
    }

    public void ClearUI()
    {
        noLobbies.SetActive(false);

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
        logOutBtn.interactable =false;
    }

    void ActivatedButtons()
    {
        createBtn.interactable = true;
        searchBtn.interactable = true;
        refleshBtn.interactable = true;
        logOutBtn.interactable = true;
    }
}
