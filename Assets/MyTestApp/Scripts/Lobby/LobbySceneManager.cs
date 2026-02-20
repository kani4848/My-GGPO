using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine.EventSystems;

public class LobbySceneManager : MonoBehaviour, ILobbySceneManager
{
    [SerializeField] LobbyUIManager uiManager;

    LobbyState _state = LobbyState.None;

    [SerializeField] LobbyState __state;

    public LobbyState state
    {
        get => _state;
        set
        {
            __state = value;
            _state = value;
            LobbyEvent.RaiseLobbyStateChanged(_state);
        }
    }

    public static string emptyPlayerName = "No name";
    public static string localUserName = emptyPlayerName;

    IEosService eosSirvice;

    CancellationTokenSource SceneCts;
    CancellationTokenSource searchCts;
    CancellationTokenSource inLobbyCts;

    public void Awake()
    {
        state = LobbyState.None;
        IGameManager.lobbySceneManager = this;
    }
    private void OnEnable()
    {
        LobbyEvent.RequestJoinLobbyEvent += OnJoin;

        LobbyButtonEvent.QuickMatchEvent += OnQuickMatch;
        LobbyButtonEvent.CreateLobbyEvent += OnCreateLobby;
        LobbyButtonEvent.SearchLobbyEvent += OnSearchLobby;
        LobbyButtonEvent.GoTitleEvent += OnGoTitle;
    }
    void SceneExitAction()
    {
        LobbyEvent.RequestJoinLobbyEvent -= OnJoin;

        LobbyButtonEvent.QuickMatchEvent -= OnQuickMatch;
        LobbyButtonEvent.CreateLobbyEvent -= OnCreateLobby;
        LobbyButtonEvent.SearchLobbyEvent -= OnSearchLobby;
        LobbyButtonEvent.GoTitleEvent -= OnGoTitle;

        SceneCts?.Cancel();
        SceneCts?.Dispose();
        SceneCts = null;

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;

        autoSearchCts?.Cancel();
        autoSearchCts?.Dispose();
        autoSearchCts = null;
    }
    private void OnApplicationQuit()
    {
        SceneExitAction();
    }
    private void OnDestroy()
    {
        SceneExitAction();
    }
    public async UniTask<LobbyState> StartFlow(IEosService _eosService)
    {
        eosSirvice = _eosService;

        SceneCts?.Cancel();
        SceneCts?.Dispose();
        SceneCts = new();

        while (!SceneCts.IsCancellationRequested)
        {
            await SearchFlow();

            if (state == LobbyState.GoTitle) break;

            await JoinFlow();

            if (state == LobbyState.JoiningError) continue;

            await InLobbyFlow();
        }

        SceneExitAction();

        return state;
    }

    //ロビー検索画面===========================

    CancellationTokenSource autoSearchCts;

    async UniTask SearchFlow()
    {
        state = LobbyState.InLobbySearchRoom;
        //入場処理
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = new();

        autoSearchCts?.Cancel();
        autoSearchCts?.Dispose();
        autoSearchCts = new();


        uiManager.ActivateSearchLobbyUI();//検索画面表示
        AutoRefleshLoop(autoSearchCts.Token).Forget();//自動検索開始
     
        await UniTask.WaitUntil(() => state != LobbyState.InLobbySearchRoom, cancellationToken: searchCts.Token);
        
        //退場処理
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;
        
        autoSearchCts?.Cancel();
        autoSearchCts?.Dispose();
        autoSearchCts = null;
        
        uiManager.DeactivateSearchLobbyUI();
    }

    private async UniTask AutoRefleshLoop(CancellationToken token)
    {
        List<SearchedLobbyData> preSearchResult = new();

        while (!token.IsCancellationRequested)
        {
            var searchResult = await eosSirvice.SearchLobby();

            if (searchResult != preSearchResult)
            {
                uiManager.ClearSearchedLobbyButtons();
                uiManager.ShowSearchResult(searchResult);
            }

            preSearchResult = searchResult;
            await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token);
        }
    }

    void OnSearchLobby(string lobbyPath = "")
    {
        OnSearchLobbyAsync().Forget();

        async UniTask OnSearchLobbyAsync()
        {
            autoSearchCts?.Cancel();
            autoSearchCts?.Dispose();
            autoSearchCts = null;

            uiManager.StartSearching();
            var searchResult = await eosSirvice.SearchLobby(lobbyPath);
            state = LobbyState.InLobbySearchRoom;
            uiManager.ShowSearchResult(searchResult);

            autoSearchCts = new();
            AutoRefleshLoop(autoSearchCts.Token).Forget();
        }
    }

    public void OnQuickMatch()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        eosSirvice.StartQuickMatch();
    }

    void OnGoTitle()
    {
        GoTitleAsync().Forget();

        async UniTask GoTitleAsync()
        {
            await ColorFadeService.Instance.BlackFadeIn();
            state = LobbyState.GoTitle;
        }
    }

    //ロビー参加中===========================
    async UniTask JoinFlow()
    {
        uiManager.SwitchLoadigUI(true);
        await UniTask.WaitUntil(() => state != LobbyState.JoiningLobby, cancellationToken: SceneCts.Token);

        if(state == LobbyState.JoiningError)
        {
            Debug.Log("ロビー参加失敗");
            await uiManager.ShowErrorWindow();
        }

        uiManager.SwitchLoadigUI(false);
    }

    public void OnCreateLobby(string path)
    {
        CreateAndJoinLobbyAsync().Forget();

        async UniTask CreateAndJoinLobbyAsync()
        {
            state = LobbyState.JoiningLobby;

            var lobbyData = await eosSirvice.CreateLobby(path, inLobbyCts.Token);

            if (lobbyData == null)state = LobbyState.JoiningError;
            else joinedLobbyData = lobbyData;
        }
    }

    async UniTask OnJoin(string id)
    {
        state = LobbyState.JoiningLobby;

        var lobbyData = await eosSirvice.JoinLobby(id, inLobbyCts.Token);

        if (lobbyData == null) state = LobbyState.JoiningError;
        else joinedLobbyData = lobbyData;
    }

    //インロビー画面===========================

    LobbyData joinedLobbyData;
    CancellationTokenSource readyCts;

    async UniTask InLobbyFlow()
    {
        state = LobbyState.InLobby_NoReady;
        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = new();
        UpdatePing(inLobbyCts.Token).Forget();
        eosSirvice.AutoP2pConnect(inLobbyCts.Token).Forget();

        uiManager.ActivatedInLobbyUI(joinedLobbyData);

        await UniTask.WaitUntil(() => state != LobbyState.InLobby_NoReady, cancellationToken: inLobbyCts.Token);

        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;
    }

    async UniTask UpdatePing(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var ping = eosSirvice.GetPing();
            uiManager.UpdatePing(ping);
            await UniTask.Delay(TimeSpan.FromSeconds(3));
        }
    }
    
    public void Ready()
    {
        ReadyAsync().Forget();

        async UniTask ReadyAsync()
        {
            readyCts?.Cancel();
            readyCts?.Dispose();
            readyCts = new();

            try
            {
                var r = await eosSirvice.Ready(readyCts.Token);

                if (r)
                {
                   state = LobbyState.GoMain;
                }
                else
                {
                    Debug.Log("握手失敗");
                    await ReadyCancelAsync();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("何か失敗");
                //レディ待ちキャンセル
                //ロビー退室
                //マッチング失敗
            }
            finally
            {
                readyCts?.Cancel();
                readyCts?.Dispose();
                readyCts = null;
            }
        }
    }

    public void ReadyCancel()
    {
        ReadyCancelAsync().Forget();    
    }

    async UniTask ReadyCancelAsync()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);

        eosSirvice.CancelReady();

        readyCts?.Cancel();
        readyCts?.Dispose();
        readyCts = null;

        await UniTask.Delay(TimeSpan.FromSeconds(1));
    }

    public void LeaveLobby()
    {
        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;

        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        LeaveLobbyAsync().Forget();

        async UniTask LeaveLobbyAsync()
        {
            SceneExitAction();
            state = LobbyState.LeavingLobby;
            await eosSirvice.LeaveLobby();
            state = LobbyState.InLobbySearchRoom;
        }
    }
}

