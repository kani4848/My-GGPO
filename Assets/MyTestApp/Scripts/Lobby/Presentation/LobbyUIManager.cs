using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class LobbyUIManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] SearchLobbyUI searchUI;
    [SerializeField] JoinedLobbyUI inLobbyUI;
    [SerializeField] GameObject loading;
    [SerializeField] TextMeshProUGUI systemMessage;

    public void Init()
    {
        ActivateLoadUI();
    }
    private void OnEnable()
    {
        LobbyEvent.lobbyStateChangedEvent += OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName += inLobbyUI.OnMemberDataUpdate;
        LobbyMemberEvent.Joined += inLobbyUI.OnJoined;
        LobbyMemberEvent.Left += inLobbyUI.OnLeft;
        LobbyMemberEvent.Death += inLobbyUI.OnDisconnect;
        LobbyMemberEvent.Revive += inLobbyUI.OnRevive;
        LobbyMemberEvent.OwnerChanged += inLobbyUI.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat += inLobbyUI.HeartBeat;
        LobbyMemberEvent.Ready += inLobbyUI.OnReady;
    }

    private void OnDisable()
    {
        LobbyEvent.lobbyStateChangedEvent -= OnChangeLobbyState;

        LobbyMemberEvent.AppliedUserName -= inLobbyUI.OnMemberDataUpdate;
        LobbyMemberEvent.Joined -= inLobbyUI.OnJoined;
        LobbyMemberEvent.Left -= inLobbyUI.OnLeft;
        LobbyMemberEvent.Death -= inLobbyUI.OnDisconnect;
        LobbyMemberEvent.Revive -= inLobbyUI.OnRevive;
        LobbyMemberEvent.OwnerChanged -= inLobbyUI.OnOwnerChanged;
        LobbyMemberEvent.HeartBeat -= inLobbyUI.HeartBeat;
        LobbyMemberEvent.Ready -= inLobbyUI.OnReady;
    }

    void OnChangeLobbyState(LobbyState state)
    {

        systemMessage.text = state.ToString();

        switch (state)
        {

            case LobbyState.None:
                ActivateLoadUI();
                break; 

            case LobbyState.InLobbySearchRoom:
                ActivatedSearchUI();
                break;

            case LobbyState.CreateLobbyAndJoin:
            case LobbyState.Joining:
            case LobbyState.LeavingLobby:
                ActivateLoadUI();
                break;


            case LobbyState.InLobby:
                inLobbyUI.SwitchButtonsOnNotReady();
                break;

            case LobbyState.Ready:
                inLobbyUI.SwitchButtonsOnReady();
                break;

            case LobbyState.ConnectingOpponent:
                inLobbyUI.DeactivateButtons();
                loading.SetActive(true);
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

    public void ActivatedInLobbyUI(LobbyData data)
    {
        searchUI.Deactivated();
        loading.SetActive(false);
        inLobbyUI.Activated(data);
    }

    public void RefreshAvailableLobby(List<SearchedLobbyData> searchLobbyDatas)
    {
        searchUI.RefreshList(searchLobbyDatas);
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
