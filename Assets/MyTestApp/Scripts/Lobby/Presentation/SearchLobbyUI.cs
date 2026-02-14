using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class SearchLobbyUI : MonoBehaviour
{
    [SerializeField] private RectTransform contentRoot; // VerticalLayoutGroup推奨
    [SerializeField] private UnityEngine.UI.Button buttonPrefab;
    
    [SerializeField] UnityEngine.UI.Button createBtn;
    [SerializeField] UnityEngine.UI.Button searchBtn;
    [SerializeField] UnityEngine.UI.Button logOutBtn;

    [SerializeField] TMP_InputField lobbyPath_create;
    [SerializeField] TMP_InputField lobbyPath_search;

    [SerializeField] GameObject noLobbies;

    public void Activated()
    {
        gameObject.SetActive(true);
        noLobbies.SetActive(false);
        ActivatedButtons();
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearUI();
    }
    
    public void RefreshList(List<SearchedLobbyData> searchLobbyDatas)
    {
        if (searchLobbyDatas == null) return;

        noLobbies.SetActive(searchLobbyDatas.Count == 0);

        foreach (var lobbyData in searchLobbyDatas)
        {
            CreateLobbyButton(lobbyData);
        }

        ActivatedButtons();
    }

    private void CreateLobbyButton(SearchedLobbyData lobbyData)
    {
        var btn = Instantiate(buttonPrefab, contentRoot);
        var buttonText = btn.GetComponentInChildren<TextMeshProUGUI>();

        buttonText.text = $"({lobbyData.ownerName})";

        btn.onClick.AddListener(() =>
        {
            LobbyEvent.RaiseRequestJoinLobby(lobbyData.lobbyId);
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
        logOutBtn.interactable =false;
    }

    void ActivatedButtons()
    {
        createBtn.interactable = true;
        searchBtn.interactable = true;
        logOutBtn.interactable = true;
    }
}
