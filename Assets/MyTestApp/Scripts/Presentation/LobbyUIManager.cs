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
        LobbyMemberEvent.Death += joinedLobby.OnDead;
        LobbyMemberEvent.OwnerChanged += joinedLobby.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat += joinedLobby.HeartBeat;
    }

    private void OnDisable()
    {
        LobbyEvent.lobbyStateChangedEvent -= OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName -= joinedLobby.OnUserNameApplied;

        LobbyMemberEvent.Joined -= joinedLobby.OnJoined;
        LobbyMemberEvent.Left -= joinedLobby.OnLeft;
        LobbyMemberEvent.Death -= joinedLobby.OnDead;
        LobbyMemberEvent.OwnerChanged -= joinedLobby.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat -= joinedLobby.HeartBeat;
    }

    void OnChangeLobbyState(LobbyState state)
    {

        systemMessage.text = state.ToString();

        switch (state)
        {
            case LobbyState.None:
                Loading.SetActive(false);
                logInUI.Activated();
                break;


            case LobbyState.LoggingIn:
                Loading.SetActive(true);
                logInUI.Deactivated();
                break;

            case LobbyState.LoggedIn:
                Loading.SetActive(false);
                avairableLobby.Activated();
                break;

            case LobbyState.LoggingOut:
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;

            case LobbyState.CreateLobbyAndJoin:
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;


            case LobbyState.Joining:
                Loading.SetActive(true);
                avairableLobby.Deactivated();
                break;


            case LobbyState.InLobby:
                Loading.SetActive(false);
                break;

            case LobbyState.LeavingLobby:
                Loading.SetActive(true);
                joinedLobby.Deactivated();
                break;

            case LobbyState.Searching:
                Loading.SetActive(true);
                break;
        }
    }

    public void SwitchJoinedLobbyScreen(LobbyData lobbyData, List<LobbyMember> members)
    {
        avairableLobby.Deactivated();
        joinedLobby.Activated(lobbyData.path, lobbyData.id, members);
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
