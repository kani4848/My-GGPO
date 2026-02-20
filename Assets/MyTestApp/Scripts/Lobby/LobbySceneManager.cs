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
    CancellationTokenSource cts;
    CancellationTokenSource inLobbyCts;

    public void Awake()
    {
        state = LobbyState.None;
        IGameManager.lobbySceneManager = this;
    }

    public async UniTask<LobbyState> StartFlow(IEosService _eosService)
    {
        state = LobbyState.InLobbySearchRoom;

        cts?.Cancel();
        cts?.Dispose();
        cts = new();

        eosSirvice = _eosService;
        uiManager.Init();
        SearchLobby();

        AutoRefleshLoop(cts.Token).Forget();

        await UniTask.WaitUntil(() => state == LobbyState.GoTitle || state == LobbyState.GoMain,
            cancellationToken: cts.Token);


        ExitAction();

        return state;
    }

    private void OnEnable()
    {
        LobbyEvent.RequestJoinLobbyEvent += OnJoin;
    }

    private void OnApplicationQuit()
    {
        ExitAction();
    }

    private void OnDestroy()
    {
        ExitAction();
    }

    Action UpdateAction = null;

    private void Update()
    {
        UpdateAction?.Invoke();

        if (UiInputGuard.IsTypingInTextField()) return;

        if (Input.GetKeyDown(KeyCode.Z))
        {
            switch (state)
            {
                case LobbyState.InLobbySearchRoom:
                    //クイックマッチ
                    break;

                case LobbyState.InLobby:
                    Ready();
                    break;

                case LobbyState.Ready:
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
        LobbyEvent.RequestJoinLobbyEvent -= OnJoin;

        cts?.Cancel();
        cts?.Dispose();
        cts = null;

        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = null;
    }

    async UniTask LobbyFlow()
    {
        await SearchFlow();
        await InLobbyFlow();
        ExitAction();
    }

    //ロビー検索画面===========================

    async UniTask SearchFlow()
    {
        //ろびー検索

        //メニュー選択
        await UniTask.WaitUntil(() => state != LobbyState.InLobbySearchRoom, cancellationToken: cts.Token);

        switch (state)
        {
            case LobbyState.SearchingLobby:
                break;
            case LobbyState.Joining:
                break;
        }

        //ロビー検索
        //クイックマッチ＞クイックマッチ処理＞シーン終了
        //
    }

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

            inLobbyCts?.Cancel();
            inLobbyCts?.Dispose();
            inLobbyCts = new();

            var lobbyData = await eosSirvice.CreateLobby(uiManager.GetLobbyPath_Create(), inLobbyCts.Token);
            UpdatePing(inLobbyCts.Token).Forget();
            state = LobbyState.InLobby;
            uiManager.ActivatedInLobbyUI(lobbyData);
        }
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

        inLobbyCts?.Cancel();
        inLobbyCts?.Dispose();
        inLobbyCts = new();

        var lobbyData = await eosSirvice.JoinLobby(id, inLobbyCts.Token);

        if (lobbyData == null)
        {
            Debug.Log("ロビー参加失敗");
            inLobbyCts?.Cancel();
            inLobbyCts?.Dispose();
            inLobbyCts = null;

            state = LobbyState.InLobbySearchRoom;
            return;
        }
        else
        {
            Debug.Log("ロビー参加成功");
        }

        state = LobbyState.InLobby;
        UpdatePing(inLobbyCts.Token).Forget();
        uiManager.ActivatedInLobbyUI(lobbyData);
    }

    //ロビー画面===========================

    async UniTask InLobbyFlow()
    {
        if (eosSirvice.p2pConnected)
        {
            //HB送信
        }
    }

    CancellationTokenSource readyCts;

    public void Ready()
    {
        ReadyAsync().Forget();

        async UniTask ReadyAsync()
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
         
            state = LobbyState.Ready;

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

        state = LobbyState.InLobby;
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
            ExitAction();
            state = LobbyState.LeavingLobby;
            await eosSirvice.LeaveLobby();
            await SearchLobbyAsync();
            state = LobbyState.InLobbySearchRoom;
        }
    }

    public void GoTitle()//ボタン用
    {
        SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
        state = LobbyState.GoTitle;
    }
}

