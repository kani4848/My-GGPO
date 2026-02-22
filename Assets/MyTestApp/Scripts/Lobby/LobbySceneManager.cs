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
    CancellationTokenSource qmCts;
    CancellationTokenSource inLobbyCts;

    public void Awake()
    {
        state = LobbyState.None;
        IGameManager.lobbySceneManager = this;
    }
    private void OnEnable()
    {
        LobbyEvent.RequestJoinLobbyEvent += OnJoin;

        LobbyButtonEvent.CancelQuickMatchEvent += OnQuickMatchCancel;
        LobbyButtonEvent.QuickMatchEvent += OnQuickMatch;

        LobbyButtonEvent.CreateLobbyEvent += OnCreateLobby;
        LobbyButtonEvent.SearchLobbyEvent += OnSearchLobby;
        LobbyButtonEvent.GoTitleEvent += OnGoTitle;

        LobbyButtonEvent.ReadyEvent += OnReady;
        LobbyButtonEvent.ReadyCancelEvent += OnReadyCancel;
        LobbyButtonEvent.LeaveLobbyEvent+= OnLeaveLobby;

    }
    void SceneExitAction()
    {
        LobbyEvent.RequestJoinLobbyEvent -= OnJoin;

        LobbyButtonEvent.CancelQuickMatchEvent -= OnQuickMatchCancel;
        LobbyButtonEvent.QuickMatchEvent -= OnQuickMatch;

        LobbyButtonEvent.CreateLobbyEvent -= OnCreateLobby;
        LobbyButtonEvent.SearchLobbyEvent -= OnSearchLobby;
        LobbyButtonEvent.GoTitleEvent -= OnGoTitle;

        LobbyButtonEvent.ReadyEvent -= OnReady;
        LobbyButtonEvent.ReadyCancelEvent -= OnReadyCancel;
        LobbyButtonEvent.LeaveLobbyEvent -= OnLeaveLobby;

        SceneCts?.Cancel();
        SceneCts?.Dispose();
        SceneCts = null;

        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;

        autoSearchCts?.Cancel();
        autoSearchCts?.Dispose();
        autoSearchCts = null;

        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;

        readyCts?.Cancel();
        readyCts?.Dispose();
        readyCts = null;
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

            if(state == LobbyState.QuickMatch)
            {
                await QuickMatchFlow();

                if (state == LobbyState.InLobbySearchRoom) continue;
                if (state == LobbyState.GoMain) break;
            }

            await JoinFlow();

            if (state == LobbyState.JoiningError) continue;

            await InLobbyFlow();

            if (state == LobbyState.GoMain) break;

            //検索画面に戻る
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

        StartAutoSearch();

        uiManager.ActivateSearchLobbyUI();//検索画面表示
        
        await UniTask.WaitUntil(() => state != LobbyState.InLobbySearchRoom, cancellationToken: searchCts.Token);
        
        //退場処理
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = null;
        
        StopAutoSearch();
        
        uiManager.DeactivateSearchLobbyUI();
    }

    int searchResultId = 0;

    private async UniTask AutoRefleshLoop(CancellationToken token)
    {
        int mySearchId;
        List<SearchedLobbyData> searchResult;
        List<SearchedLobbyData> preSearchResult = new();

        while (!token.IsCancellationRequested)
        {
            mySearchId = ++searchResultId;

            searchResult = await eosSirvice.SearchLobby();

            if (token.IsCancellationRequested) break;
            if (mySearchId != searchResultId) continue;

            if (searchResult != preSearchResult)
            {
                //Debug.Log("オートサーチ結果適用");
                uiManager.ShowAutoSearchResult(searchResult);
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
            int mySearchId = ++searchResultId;

            StopAutoSearch();

            uiManager.StartManualSearching();
            var searchResult = await eosSirvice.SearchLobby(lobbyPath);

            if (mySearchId == searchResultId)
            {
                //Debug.Log("マニュアルサーチ結果適用");
                state = LobbyState.InLobbySearchRoom;
                uiManager.ShowManualSearchResult(searchResult);
            }

            StartAutoSearch();
        }
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

    void StopAutoSearch()
    {
        //自動検索止める
        autoSearchCts?.Cancel();
        autoSearchCts?.Dispose();
        autoSearchCts = null;
    }

    void StartAutoSearch()
    {
        //自動検索復活
        if (autoSearchCts == null)
        {
            autoSearchCts?.Cancel();
            autoSearchCts?.Dispose();
            autoSearchCts = new();
            AutoRefleshLoop(autoSearchCts.Token).Forget();
        }
    }


    //クイックマッチ画面===========================

    async UniTask QuickMatchFlow()
    {
        qmCts?.Cancel();
        qmCts?.Dispose();
        qmCts = CancellationTokenSource.CreateLinkedTokenSource(SceneCts.Token);

        uiManager.ActivateQuickMatchUI(eosSirvice.GetLocalPlayerData());

        var goahead = await eosSirvice.QuickMatch_FindOpponent(qmCts.Token);

        if (!goahead)
        {
            ExitQuickMatch();
            //await uiManager.ShowErrorWindow();
            state = LobbyState.InLobbySearchRoom;
            return;
        }

        if (qmCts.IsCancellationRequested)
        {
            ExitQuickMatch();
            state = LobbyState.InLobbySearchRoom;
            return;
        }

        //ここからキャンセルボタン無効
        uiManager.DeactivateButtons_QuickMatch();
        goahead = await eosSirvice.QuickMatch_HandShake(qmCts.Token);

        if (!goahead)
        {
            ExitQuickMatch();
            //await uiManager.ShowErrorWindow();
            state = LobbyState.InLobbySearchRoom;
            return;
        }

        if (qmCts.IsCancellationRequested)
        {
            ExitQuickMatch();
            state = LobbyState.InLobbySearchRoom;
            return;
        }

        state = LobbyState.GoMain;
    }

    void ExitQuickMatch()
    {
        uiManager.DeactivateQuickMatchUI();
        qmCts?.Cancel();
        qmCts?.Dispose();
        qmCts = null;
    }

    void OnQuickMatch()
    {
        state = LobbyState.QuickMatch;
    }

    void OnQuickMatchCancel()
    {
        qmCts?.Cancel();
        qmCts?.Dispose();
        qmCts = null;
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

            var lobbyData = await eosSirvice.CreateLobby(path, SceneCts.Token);


            if (lobbyData == null)
            {
                state = LobbyState.JoiningError;
            }
            else
            {
                state = LobbyState.JoiningSuccess;
                joinedLobbyData = lobbyData;
            }
        }
    }

    async UniTask OnJoin(string id)
    {
        state = LobbyState.JoiningLobby;

        var lobbyData = await eosSirvice.JoinLobby(id, SceneCts.Token);

        if (lobbyData == null)
        {
            state = LobbyState.JoiningError;
        }
        else
        {
            state = LobbyState.JoiningSuccess;
            joinedLobbyData = lobbyData;
        }
    }

    //インロビー画面===========================
    LobbyData joinedLobbyData;
    CancellationTokenSource readyCts;

    async UniTask InLobbyFlow()
    {
        //入場処理
        state = LobbyState.InLobby_NoReady;
        
        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = new();

        readyCts?.Cancel();
        readyCts?.Dispose();
        readyCts = new();

        UpdatePing(inLobbyCts.Token).Forget();
        eosSirvice.AutoP2pConnect(inLobbyCts.Token).Forget();

        uiManager.ActivatedInLobbyUI(joinedLobbyData);

        //操作結果待ち
        await UniTask.WaitUntil(() => state == LobbyState.GoMain || state == LobbyState.LeavingLobby, 
            cancellationToken: inLobbyCts.Token);

        //退場処理
        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;

        readyCts?.Cancel();
        readyCts?.Dispose();
        readyCts = null;

        uiManager.DeactivatedInLobbyUI();
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
    
    public void OnReady()
    {
        OnReadyAsync().Forget();

        async UniTask OnReadyAsync()
        {
            readyCts?.Cancel();
            readyCts?.Dispose();
            readyCts = new();

            try
            {
                //レディ待機
                await eosSirvice.Ready(readyCts.Token);

                if (readyCts.IsCancellationRequested) return;

                //オールレディでボタン操作停止
                uiManager.DeactivateButtons();

                var r = await eosSirvice.AllReady(readyCts.Token);

                if (!r)
                {
                    Debug.Log("握手失敗");
                    OnReadyCancel();
                    return;
                }

                state = LobbyState.GoMain;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("何か失敗");
            }
            finally
            {
                readyCts?.Cancel();
                readyCts?.Dispose();
                readyCts = null;
            }
        }
    }

    public void OnReadyCancel()
    {
        state = LobbyState.InLobby_NoReady;
        eosSirvice.CancelReady();
        readyCts?.Cancel();
        readyCts?.Dispose();
        readyCts = null;
    }

    public void OnLeaveLobby()
    {
        LeaveLobbyAsync().Forget();

        async UniTask LeaveLobbyAsync()
        {
            await eosSirvice.LeaveLobby();
            state = LobbyState.LeavingLobby;
        }
    }
}

