using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;
using Cysharp.Threading;
using Cysharp.Threading.Tasks;
using System.Threading;
using Unity.VisualScripting;
using Epic.OnlineServices;
using System;
using UnityEngine.Events;
using Newtonsoft.Json.Bson;
using Epic.OnlineServices.Lobby;

public static class LobbyEvent
{
    public delegate void LobbyStateChangedAction(LobbyState lobbyState);
    public static event LobbyStateChangedAction lobbyStateChangedEvent;
    public static void RaiseLobbyStateChanged(LobbyState lobbyState) => lobbyStateChangedEvent?.Invoke(lobbyState);
}

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
            LobbyEvent.RaiseLobbyStateChanged(_state);
        }
    }

    public static ProductUserId myPUID;
    public static string emptyPlayerName = "No name";
    public static string localUserName = emptyPlayerName;

    EOSLobbyManager lm;
    [SerializeField]LobbyServiceManager lobbyServiceManager;

    //メンバー属性キーは必ず大文字
    public static string HB_KEY = "HB";
    public static string HB_STALE_KEY = "STALE";
    public static string READY_KEY = "READY";

    //ロビーキー、IDは必ず小文字
    public static string LobbyCommonKey = "bucket";
    public static string LobbyCommonId = "test"; 
    public static string customKey = "custom";

    
    CancellationTokenSource cts;

    public void Awake()
    {
        state = LobbyState.None;
        IGameManager.lobbySceneManager = this;
        cts = new();
    }

    public void Init(IEosService eosService)
    {
        state = LobbyState.InLobbySearchRoom;

        lm = eosService.lobbyManager;
        lobbyServiceManager = new LobbyServiceManager(lm);
        uiManager.Init();
        SearchLobby();
        AutoRefleshLoop(cts.Token).Forget();
    }

    private void OnApplicationQuit()
    {
        ExitAction();
    }

    private void OnDestroy()
    {
        ExitAction();
    }

    void ExitAction()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;

        lobbyServiceManager?.OnDispose();
    }

    //ロビー検索画面===========================
    public void LogOut()
    {
        LogOutAsync().Forget();

        async UniTask LogOutAsync()
        {
            //state = LobbyState.LoggingOut;
            //await autoLogIn.LogoutAsync();

            state = LobbyState.None;
        }
    }

    private async UniTask AutoRefleshLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (state == LobbyState.InLobbySearchRoom)
            {
                var searchResult = await lobbyServiceManager.SearchLobby();
                uiManager.ClearAvairableLobby();
                uiManager.RefreshAvailableLobby(searchResult, JoinLobby);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token);
        }
    }

    public void CreateAndJoinLobby()
    {
        CreateAndJoinLobbyAsync().Forget();


        async UniTask CreateAndJoinLobbyAsync()
        {
            state = LobbyState.CreateLobbyAndJoin;
            await lobbyServiceManager.CreateLobby(uiManager.GetLobbyPath_Create());

            state = LobbyState.InLobby;
            uiManager.ActivatedInLobbyUI(lm.GetCurrentLobby());
        }
    }

    public void SearchLobby()
    {
        string lobbyPath = uiManager.GetLobbyPath_Search();
        SearchLobbyAsync(lobbyPath).Forget();
    }

    async UniTask SearchLobbyAsync(string lobbyPath = "")
    {
        state = LobbyState.SearchingLobby;
        uiManager.ClearAvairableLobby();
        var searchResult = await lobbyServiceManager.SearchLobby(lobbyPath);

        state = LobbyState.InLobbySearchRoom;
        uiManager.RefreshAvailableLobby(searchResult, JoinLobby);
    }

    void JoinLobby(Lobby lobby, LobbyDetails details)
    {
        JoinLobbyAsync(lobby, details).Forget();


        async UniTask JoinLobbyAsync(Lobby lobby, LobbyDetails details)
        {
            state = LobbyState.Joining;

            bool joinSuccess = await lobbyServiceManager.Join(lobby, details);

            if (!joinSuccess)
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
            uiManager.ActivatedInLobbyUI(lm.GetCurrentLobby());
        }
    }

    //ロビー画面===========================

    public void Ready()
    {
        ReadyAsync().Forget();

        async UniTask ReadyAsync()
        {
            /*
            state = LobbyState.Ready;
            lobbyServiceManager.Ready();
            await UniTask.WaitUntil(() => lobbyServiceManager.ConnectingStart(), cancellationToken: cts.Token);
            state = LobbyState.Connecting;
            await UniTask.WaitUntil(() => lobbyServiceManager.ConnectingComplete(), cancellationToken: cts.Token);
            */

            state = LobbyState.GoMain;
        }
    }

    public void LeaveLobby()
    {
        LeaveLobbyAsync().Forget();

        async UniTask LeaveLobbyAsync()
        {
            state = LobbyState.LeavingLobby;
            ExitAction();
            bool result = await lobbyServiceManager.LeaveAsync();

            if (!result)
            {
                Debug.Log("ロビー退室失敗" + result.ToString());
                return;
            }

            await SearchLobbyAsync();

            state = LobbyState.InLobbySearchRoom;
        }
    }
}

