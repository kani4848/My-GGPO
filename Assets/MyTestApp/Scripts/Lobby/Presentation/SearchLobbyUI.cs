using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class SearchLobbyUI : MonoBehaviour
{
    [SerializeField] private RectTransform contentRoot; // VerticalLayoutGroup推奨
    [SerializeField] LobbyJoinButton lobbyJoinButtonPrefab;

    [SerializeField] UnityEngine.UI.Button quickBtn;
    [SerializeField] UnityEngine.UI.Button createBtn;
    [SerializeField] UnityEngine.UI.Button searchBtn;
    [SerializeField] UnityEngine.UI.Button titleBtn;

    [SerializeField] TMP_InputField lobbyPath_create;
    [SerializeField] TMP_InputField lobbyPath_search;


    [SerializeField] GameObject searchingUI;
    [SerializeField] GameObject noLobbies;
    public void Activated()
    {
        gameObject.SetActive(true);
        ActivatedButtons();
        ClearLobbyButtons(); 
        SwitchNoLobbiesText(false);
        SwitchSearcingUI(true);
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearLobbyButtons();
        DeactivatedButtons();
    }

    private void Update()
    {
        if (UiInputGuard.IsTypingInTextField()) return;

        if (Input.GetKeyDown(KeyCode.Z) && quickBtn.interactable)
        {
            quickBtn.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.X) && createBtn.interactable)
        {
            createBtn.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.C) && searchBtn.interactable)
        {
            searchBtn.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.V) && titleBtn.interactable)
        {
            titleBtn.onClick.Invoke();
        }
    }

    public void StartManualSearching()
    {
        SwitchNoLobbiesText(false);
        SwitchSearcingUI(true);
        ClearLobbyButtons();
        DeactivatedButtons();
    }
    public void ShowManualSearchResult(List<SearchedLobbyData> searchLobbyDatas)
    {
        RefreshList(searchLobbyDatas);
        ActivatedButtons();
    }

    public void ShowAutoSearchResult(List<SearchedLobbyData> searchLobbyDatas)
    {
        ClearLobbyButtons();
        RefreshList(searchLobbyDatas);
    }

    //処理パーツ=================================================
    void RefreshList(List<SearchedLobbyData> searchLobbyDatas)
    {
        SwitchSearcingUI(false);
        
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
    }

    void CreateLobbyButton(SearchedLobbyData lobbyData)
    {
        var btn = Instantiate(lobbyJoinButtonPrefab, contentRoot);
        
        btn.SetLobbyDatas(lobbyData);
    }

    void ClearLobbyButtons()
    {
        noLobbies.SetActive(false);

        if (contentRoot == null) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(contentRoot.GetChild(i).gameObject);
        }
    }

    void SwitchNoLobbiesText(bool active)
    {
        noLobbies.SetActive(active);
    }

    void SwitchSearcingUI(bool active)
    {
        searchingUI.SetActive(active);
    }

    void ActivatedButtons()
    {
        quickBtn.interactable = true;
        createBtn.interactable = true;
        searchBtn.interactable = true;
        titleBtn.interactable = true;

        quickBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseQuickMatch();
            DeactivatedButtons();
        });

        createBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseCreateLobby(lobbyPath_create.text);
            DeactivatedButtons();
        });

        searchBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseSearchLobby(lobbyPath_search.text);
            DeactivatedButtons();
        });

        titleBtn.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseGoTitle();
            DeactivatedButtons();
        });
    }

    void DeactivatedButtons()
    {
        quickBtn.interactable = false;
        createBtn.interactable = false;
        searchBtn.interactable = false;
        titleBtn.interactable = false;

        quickBtn.onClick.RemoveAllListeners();
        createBtn.onClick.RemoveAllListeners();
        searchBtn.onClick.RemoveAllListeners();
        titleBtn.onClick.RemoveAllListeners();
    }
}
