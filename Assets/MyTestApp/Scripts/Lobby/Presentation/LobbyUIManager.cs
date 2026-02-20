using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

public sealed class LobbyUIManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] SearchLobbyUI searchUI;
    [SerializeField] JoinedLobbyUI inLobbyUI;
    
    [SerializeField] TextMeshProUGUI systemMessage;
    [SerializeField] GameObject loading;
    [SerializeField] GameObject errorWindow;

    private void OnEnable()
    {
        LobbyEvent.lobbyStateChangedEvent += OnChangeLobbyState;

        LobbyMemberEvent.UpdatePlayerData += inLobbyUI.OnMemberDataUpdate;

        LobbyMemberEvent.MemberJoinedEvent += inLobbyUI.OnJoined;
        LobbyMemberEvent.MemberLeftEvent += inLobbyUI.OnLeft;
        LobbyMemberEvent.MemberHbStopEvent += inLobbyUI.OnDisconnect;
        
        LobbyMemberEvent.MemberReviveEvent += inLobbyUI.OnRevive;
        
        LobbyMemberEvent.OwnerChangedEvent += inLobbyUI.OnOwnerChanged;
        
        LobbyMemberEvent.HeartBeatEvent += inLobbyUI.HeartBeat;
    }

    private void OnDisable()
    {
        LobbyEvent.lobbyStateChangedEvent -= OnChangeLobbyState;

        LobbyMemberEvent.UpdatePlayerData -= inLobbyUI.OnMemberDataUpdate;
        LobbyMemberEvent.MemberJoinedEvent -= inLobbyUI.OnJoined;
        LobbyMemberEvent.MemberLeftEvent -= inLobbyUI.OnLeft;
        LobbyMemberEvent.MemberHbStopEvent -= inLobbyUI.OnDisconnect;
        LobbyMemberEvent.MemberReviveEvent -= inLobbyUI.OnRevive;
        LobbyMemberEvent.OwnerChangedEvent -= inLobbyUI.OnOwnerChanged;
        LobbyMemberEvent.HeartBeatEvent -= inLobbyUI.HeartBeat;
    }

    void OnChangeLobbyState(LobbyState state)
    {
        systemMessage.text = state.ToString();
    }

    //ロビー検索画面===================================
    public void ActivateSearchLobbyUI()
    {
        searchUI.Activated();
    }

    public void ShowSearchResult(List<SearchedLobbyData> searchLobbyDatas)
    {
        searchUI.RefreshList(searchLobbyDatas);
    }

    public void ClearSearchedLobbyButtons()
    {
        searchUI.ClearLobbyButtons();
    }

    public void StartSearching()
    {
        searchUI.StartSearching();
    }

    public void DeactivateSearchLobbyUI()
    {
        searchUI.Deactivated();
    }

    //参加中画面===================================
    public void SwitchLoadigUI(bool active)
    {
        loading.SetActive(active);
    }

    public async UniTask ShowErrorWindow()
    {
        errorWindow.SetActive(true);
        await UniTask.Delay(TimeSpan.FromSeconds(2));
        errorWindow.SetActive(false);
    }

    //ロビー内画面===================================
    public void ActivatedInLobbyUI(LobbyData data)
    {
        searchUI.Deactivated();
        loading.SetActive(false);
        inLobbyUI.Activated(data);
    }

    public void UpdatePing(double ping)
    {
        inLobbyUI.UpdatePing(ping);
    }


}
