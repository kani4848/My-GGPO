using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using Unity.VisualScripting;

public sealed class LobbyUIManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] SearchLobbyUI searchUI;
    [SerializeField] JoinedLobbyUI inLobbyUI;
    [SerializeField] GameObject loading;

    [SerializeField] TextMeshProUGUI systemMessage;

    [Header("Dependencies")]
    [SerializeField] private LobbyService_search lobbyService; // 直前スクリプト（Join担当）

    // SearchResultsは Dictionary<Lobby, LobbyDetails>
    private Dictionary<Lobby, LobbyDetails> _cachedResults = new();

    public void Init()
    {
        ActivateLoadUI();
    }

    private void OnEnable()
    {
        LobbyEvent.lobbyStateChangedEvent += OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName += inLobbyUI.OnUserNameApplied;
        LobbyMemberEvent.Joined += inLobbyUI.OnJoined;
        LobbyMemberEvent.Left += inLobbyUI.OnLeft;
        LobbyMemberEvent.Death += inLobbyUI.OnDead;
        LobbyMemberEvent.Revive += inLobbyUI.OnRevive;
        LobbyMemberEvent.OwnerChanged += inLobbyUI.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat += inLobbyUI.HeartBeat;
    }

    private void OnDisable()
    {
        LobbyEvent.lobbyStateChangedEvent -= OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName -= inLobbyUI.OnUserNameApplied;
        LobbyMemberEvent.Joined -= inLobbyUI.OnJoined;
        LobbyMemberEvent.Left -= inLobbyUI.OnLeft;
        LobbyMemberEvent.Death -= inLobbyUI.OnDead;
        LobbyMemberEvent.Revive -= inLobbyUI.OnRevive;
        LobbyMemberEvent.OwnerChanged -= inLobbyUI.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat -= inLobbyUI.HeartBeat;
    }

    void OnChangeLobbyState(LobbyState state)
    {

        systemMessage.text = state.ToString();

        switch (state)
        {
            case LobbyState.InLobbySearchRoom:
                ActivatedSearchUI();
                break;

            case LobbyState.None:
            case LobbyState.CreateLobbyAndJoin:
            case LobbyState.Joining:
            case LobbyState.LeavingLobby:
                ActivateLoadUI();
                break;

            case LobbyState.Ready:
                inLobbyUI.OnReady();
                break;

            case LobbyState.Connecting:
                inLobbyUI.OnConnecting();
                loading.SetActive(true);
                break;

            case LobbyState.Connected:
                inLobbyUI.OnConnected();
                loading.SetActive(false);
                break;

                
            case LobbyState.SearchingLobby:
                loading.SetActive(true);
                break;
        }
    }

    //ロビー検索画面の起動
    void ActivatedSearchUI()
    {
        searchUI.Activated();
        inLobbyUI.Deactivated();
        loading.SetActive(false);
    }
    //ロード画面の起動
    void ActivateLoadUI()
    {
        searchUI.Deactivated();
        inLobbyUI.Deactivated();
        loading.SetActive(true);
    }

    public void ActivatedInLobbyUI(Lobby lobby)
    {
        searchUI.Deactivated();
        loading.SetActive(false);
        var customAtt = lobby.Attributes.FirstOrDefault(m => m.Key == LobbySceneManager.customKey);
        inLobbyUI.Activated(customAtt.AsString, lobby.Id, lobby.Members);
    }

    public void RefreshAvailableLobby(Dictionary<Lobby, LobbyDetails> lobbies, Action<Lobby,LobbyDetails> joinAction)
    {
        searchUI.RefreshList(lobbies, joinAction);
    }

    public string GetLobbyPath_Create()
    {
        return searchUI.GetLobbyPath_Create();
    }

    public string GetLobbyPath_Search()
    {
        return searchUI.GetLobbyPath_Search();
    }

    public void ClearAvairableLobby()
    {
        searchUI.ClearUI();
    }
}
