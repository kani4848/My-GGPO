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
using UnityEngine.Rendering.Universal;

public sealed class LobbyUIManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] LobbyLogInUI logInUI;
    [SerializeField] AvairableLobbyUI avairableLobby;
    [SerializeField] JoinedLobbyUI joinedLobby;
    [SerializeField] GameObject Loading;

    [SerializeField] TextMeshProUGUI systemMessage;

    [Header("Dependencies")]
    [SerializeField] private LobbyService lobbyService; // 直前スクリプト（Join担当）

    // SearchResultsは Dictionary<Lobby, LobbyDetails>
    private Dictionary<Lobby, LobbyDetails> _cachedResults = new();

    public void Init()
    {
        logInUI.Activated();
        avairableLobby.Deactivated();
        joinedLobby.Deactivated();
        Loading.SetActive(false);
    }

    private void OnEnable()
    {
        LobbyEvent.lobbyStateChangedEvent += OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName += joinedLobby.OnUserNameApplied;

        LobbyMemberEvent.Joined += joinedLobby.OnJoined;
        LobbyMemberEvent.Left += joinedLobby.OnLeft;
        LobbyMemberEvent.OwnerChanged += joinedLobby.OnOwnerChanged;
    }

    private void OnDisable()
    {
        LobbyEvent.lobbyStateChangedEvent -= OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName -= joinedLobby.OnUserNameApplied;

        LobbyMemberEvent.Joined -= joinedLobby.OnJoined;
        LobbyMemberEvent.Left -= joinedLobby.OnLeft;
        LobbyMemberEvent.OwnerChanged -= joinedLobby.OnOwnerChanged;
    }

    void OnChangeLobbyState(LobbyState state)
    {
        switch (state)
        {
            case LobbyState.None:
                systemMessage.text = "Log out";
                Loading.SetActive(false);
                logInUI.Activated();
                break;


            case LobbyState.LoggingIn:
                systemMessage.text = "Logging...";
                Loading.SetActive(true);
                logInUI.Deactivated();
                break;

            case LobbyState.LoggedIn:
                systemMessage.text = "LoggedIn";
                Loading.SetActive(false);
                avairableLobby.Activated();
                break;

            case LobbyState.LoggingOut:
                systemMessage.text = "Logging out...";
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;

            case LobbyState.CreateLobbyAndJoin:
                systemMessage.text = "Creating Lobby...";
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;


            case LobbyState.Joining:
                systemMessage.text = "Joining Lobby...";
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;


            case LobbyState.InLobby:
                systemMessage.text = "In Lobby";
                Loading.SetActive(false);
                break;

            case LobbyState.LeavingLobby:
                systemMessage.text = "Leaving Lobby..";
                Loading.SetActive(true);
                joinedLobby.Deactivated();
                break;

            case LobbyState.Searching:
                systemMessage.text = "Searching Lobby..";
                Loading.SetActive(true);
                break;
        }
    }

    public void SwitchJoinedLobbyScreen(LobbyData lobbyData, List<ProductUserId> puids)
    {
        avairableLobby.Deactivated();
        joinedLobby.Activated(lobbyData.path, lobbyData.id, puids);
    }

    public void RefreshAvailableLobby(List<LobbyData> lobbyDatas, Action<LobbyData> joinAction)
    {
        avairableLobby.RefreshList(lobbyDatas, joinAction);
    }

    public string GetUserName()
    {
        return logInUI.GetUserName();
    }

    public string GetLobbyPath_Create()
    {
        return avairableLobby.GetLobbyPath_Create();
    }

    public string GetLobbyPath_Search()
    {
        return avairableLobby.GetLobbyPath_Search();
    }

    public void ClearAvairableLobby()
    {
        avairableLobby.ClearUI();
    }
}
