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
    [SerializeField] LobbyQuickMatchUi qmUI;

    [SerializeField] TextMeshProUGUI systemMessage;
    [SerializeField] GameObject loading;
    [SerializeField] GameObject errorWindow;

    private void OnEnable()
    {
        LobbyEvent.lobbyStateChangedEvent += OnChangeLobbyState;

        LobbyMemberEvent.UpdatePlayerData += inLobbyUI.UpdateOrCreateNamePlate;

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

        LobbyMemberEvent.UpdatePlayerData -= inLobbyUI.UpdateOrCreateNamePlate;
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

    public void StartManualSearching()
    {
        searchUI.StartManualSearching();
    }

    public void ShowManualSearchResult(List<SearchedLobbyData> searchLobbyDatas)
    {
        searchUI.ShowManualSearchResult(searchLobbyDatas);
    }

    public void ShowAutoSearchResult(List<SearchedLobbyData> searchLobbyDatas)
    {
        searchUI.ShowAutoSearchResult(searchLobbyDatas);
    }

    public void DeactivateSearchLobbyUI()
    {
        searchUI.Deactivated();
    }

    //クイックマッチ画面===================================

    public void ActivateQuickMatchUI(PlayerData playerData)
    {
        qmUI.Activate(playerData);
    }

    public void FindOpponent_QuickMatch(PlayerData opponentData)
    {
        qmUI.OnFindOpponent(opponentData);
    }

    public void DeactivateQuickMatchUI()
    {
        qmUI.Deactivate();
    }

    //入室処理画面===================================
    public void SwitchLoadigUI(bool active)
    {
        loading.SetActive(active);
    }

    public async UniTask ShowErrorWindow()
    {
        loading.SetActive(false);
        errorWindow.SetActive(true);
        await UniTask.Delay(TimeSpan.FromSeconds(2));
        errorWindow.SetActive(false);
    }

    //インロビー画面===================================
    public void ActivatedInLobbyUI(LobbyData data) => inLobbyUI.Activated(data);

    public void UpdatePing(double ping)
    {
        inLobbyUI.UpdatePing(ping);
    }

    public void DeactivateButtons()
    {
        inLobbyUI.DeactivateButtons();
    }

    public void DeactivatedInLobbyUI()
    {
        inLobbyUI.Deactivated();
    }

}
