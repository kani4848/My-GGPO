using TMPro;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class SearchLobbyUI : MonoBehaviour
{
    [SerializeField] private RectTransform contentRoot; // VerticalLayoutGroup推奨
    [SerializeField] LobbyJoinButton lobbyJoinButtonPrefab;


    [SerializeField] UnityEngine.UI.Button quickBtn;
    [SerializeField] UnityEngine.UI.Button createBtn;
    [SerializeField] UnityEngine.UI.Button searchBtn;
    [SerializeField] UnityEngine.UI.Button logOutBtn;

    [SerializeField] TMP_InputField lobbyPath_create;
    [SerializeField] TMP_InputField lobbyPath_search;

    [SerializeField] GameObject noLobbies;
    public void Activated()
    {
        gameObject.SetActive(true);
        ActivatedButtons();
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearUI();
    }
    
    public void RefreshList(List<SearchedLobbyData> searchLobbyDatas)
    {
        if (searchLobbyDatas == null || searchLobbyDatas.Count == 0)
        {
            noLobbies.SetActive(true);
            return;
        }

        noLobbies.SetActive(false);

        foreach (var lobbyData in searchLobbyDatas)
        {
            CreateLobbyButton(lobbyData);
        }

        ActivatedButtons();
    }

    private void CreateLobbyButton(SearchedLobbyData lobbyData)
    {
        var btn = Instantiate(lobbyJoinButtonPrefab, contentRoot);
        
        btn.SetLobbyDatas(lobbyData);
    }

    public void ClearUI()
    {
        noLobbies.SetActive(false);

        if (contentRoot == null) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(contentRoot.GetChild(i).gameObject);
        }
    }

    public string GetLobbyPath_Create()
    {
        return lobbyPath_create.text;
    }

    public string GetLobbyPath_Search()
    {
        return lobbyPath_search.text;
    }


    public void DeactivatedButtons()
    {
        quickBtn.interactable = false;
        createBtn.interactable = false;
        searchBtn.interactable = false;
        logOutBtn.interactable =false;
    }

    void ActivatedButtons()
    {
        quickBtn.interactable = true;
        createBtn.interactable = true;
        searchBtn.interactable = true;
        logOutBtn.interactable = true;
    }
}
