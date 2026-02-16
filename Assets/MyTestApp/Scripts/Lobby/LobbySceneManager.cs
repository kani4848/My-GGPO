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

    public LobbyState state
    {
        get => _state;
        set
        {
            _state = value;
            Debug.Log($"ロビーステート変更:{_state}");
            LobbyEvent.RaiseLobbyStateChanged(_state);
        }
    }

    public static string emptyPlayerName = "No name";
    public static string localUserName = emptyPlayerName;

    IEosService eosSirvice;    
    CancellationTokenSource cts;

    public void Awake()
    {
        state = LobbyState.None;
        IGameManager.lobbySceneManager = this;
        cts = new();
    }

    public void Init(IEosService _eosService)
    {
        state = LobbyState.InLobbySearchRoom;

        eosSirvice = _eosService;
        uiManager.Init();
        SearchLobby();
        AutoRefleshLoop(cts.Token).Forget();
    }

    private void OnEnable()
    {
        LobbyEvent.RequestJoinlobbyEvent += OnJoin;
    }

    private void OnApplicationQuit()
    {
        ExitAction();
    }

    private void OnDestroy()
    {
        ExitAction();
    }

    bool readyWait = false;

    private void Update()
    {
        eosSirvice?.HeartBeatTic();

        if (UiInputGuard.IsTypingInTextField()) return;

        if (Input.GetKeyDown(KeyCode.Z))
        {
            switch (state)
            {
                case LobbyState.InLobbySearchRoom:
                    //クイックマッチ
                    break;

                case LobbyState.InLobby:
                    UnlockReadyWait().Forget();
                    Ready();
                    break;

                case LobbyState.Ready:
                    UnlockReadyWait().Forget();
                    ReadyCancel();
                    break;
            }
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            switch (state)
            {
                case LobbyState.InLobbySearchRoom:
                    CreateAndJoinLobby();
                    break;

                case LobbyState.Ready:
                case LobbyState.InLobby:
                    LeaveLobby();
                    break;
            }
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            switch (state)
            {
                case LobbyState.InLobbySearchRoom:
                    string lobbyPath = uiManager.GetLobbyPath_Search();
                    SearchLobbyAsync(lobbyPath).Forget();
                    break;
            }
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            switch (state)
            {
                case LobbyState.InLobbySearchRoom:
                    state = LobbyState.GoTitle;
                    break;
            }
        }
    }
     
    void ExitAction()
    {
        LobbyEvent.RequestJoinlobbyEvent -= OnJoin;

        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    //ロビー検索画面===========================

    private async UniTask AutoRefleshLoop(CancellationToken token)
    {
        List<SearchedLobbyData> preSearchResult = new();

        while (!token.IsCancellationRequested)
        {
            if (state != LobbyState.InLobbySearchRoom)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token);
                continue;
            }

            var searchResult = await eosSirvice.SearchLobby();

            if (searchResult != preSearchResult)
            {
                uiManager.ClearSearchedLobbyList();
                uiManager.RefreshAvailableLobby(searchResult);
            }

            preSearchResult = searchResult;

            await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token);
        }
    }

    public void CreateAndJoinLobby()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        CreateAndJoinLobbyAsync().Forget();

        async UniTask CreateAndJoinLobbyAsync()
        {

            state = LobbyState.CreateLobbyAndJoin;
            var lobbyData = await eosSirvice.CreateLobby(uiManager.GetLobbyPath_Create());

            state = LobbyState.InLobby;
            uiManager.ActivatedInLobbyUI(lobbyData);
        }
    }

    public void SearchLobby()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        string lobbyPath = uiManager.GetLobbyPath_Search();
        SearchLobbyAsync(lobbyPath).Forget();
    }

    async UniTask SearchLobbyAsync(string lobbyPath = "")
    {
        state = LobbyState.SearchingLobby;
        uiManager.ClearSearchedLobbyList();
        var searchResult = await eosSirvice.SearchLobby(lobbyPath);

        state = LobbyState.InLobbySearchRoom;
        uiManager.RefreshAvailableLobby(searchResult);
    }

    public void QuickMatch()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        eosSirvice.StartQuickMatch();
    }

    async UniTask OnJoin(string id)
    {
        state = LobbyState.Joining;

        var lobbyData = await eosSirvice.JoinLobby(id);

        if (lobbyData == null)
        {
            Debug.Log("ロビー参加失敗");
            state = LobbyState.InLobbySearchRoom;
            return;
        }
        else
        {
            Debug.Log("ロビー参加成功");
        }

        state = LobbyState.InLobby;
        uiManager.ActivatedInLobbyUI(lobbyData);
    }

    //ロビー画面===========================

    public void Ready()
    {
        ReadyAsync().Forget();

        async UniTask ReadyAsync()
        {
            if (readyWait) return;
            readyWait = true;

            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
         
            state = LobbyState.Ready;

            cts?.Cancel();
            cts?.Dispose();
            cts = new();

            try
            {
                await eosSirvice.Ready(cts.Token);

                //全員ready完了
                state = LobbyState.ConnectingOpponent;

                bool connectSuccess = await eosSirvice.StartConnectPeer(cts.Token);
                if (!connectSuccess) return;

                state = LobbyState.GoMain;
            }
            catch (OperationCanceledException)
            {
                //レディ待ちキャンセル
                //ロビー退室
                //マッチング失敗
            }
            finally
            {
                cts?.Cancel();
                cts?.Dispose();
                cts = null;
            }
        }
    }

    async UniTask UnlockReadyWait()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(1));
        readyWait = false;
    }

    public void ReadyCancel()
    {
        ReadyCancelAsync().Forget();    

        async UniTask ReadyCancelAsync()
        {

            if (readyWait) return;
            readyWait = true;

            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);

            state = LobbyState.InLobby;
            eosSirvice.CancelReady();

            cts?.Cancel();
            cts?.Dispose();
            cts = null;

            await UniTask.Delay(TimeSpan.FromSeconds(1));

            readyWait = false;
        }
    }

    public void LeaveLobby()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        LeaveLobbyAsync().Forget();

        async UniTask LeaveLobbyAsync()
        {
            ExitAction();
            state = LobbyState.LeavingLobby;
            await eosSirvice.LeaveLobby();
            await SearchLobbyAsync();
            state = LobbyState.InLobbySearchRoom;
        }
    }

    public void GoTitle()
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        state = LobbyState.GoTitle;
    }
}

