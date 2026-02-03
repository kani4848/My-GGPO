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

public enum LobbyState
{
    None,
    LoggingIn,
    LoggedIn,
    LoggingOut,

    Searching, 
    CreateLobbyAndJoin,
    
    Joining,
    InLobby,
    Ready,
    Connecting,
    Connected,
    LeavingLobby,

    Error,
}

public static class LobbyEvent
{
    public delegate void LobbyStateChangedAction(LobbyState lobbyState);
    public static event LobbyStateChangedAction lobbyStateChangedEvent;
    public static void RaiseLobbyStateChanged(LobbyState lobbyState) => lobbyStateChangedEvent?.Invoke(lobbyState);
}

public class LobbySceneManager : MonoBehaviour
{
    [SerializeField] AutoLogin_DeviceId autoLogIn;
    [SerializeField] LobbyUIManager lobbyUI;

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

    private void Start()
    {
        cts = new();

        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        lm = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();

        lobbyServiceManager = new LobbyServiceManager(lm);
        lobbyUI.Init();

        state = LobbyState.None;
    }

    private async UniTask AutoRefleshLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // 前の処理が終わってから n 秒待つ
            await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token);
        }
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
        cts.Cancel();
        cts.Dispose();

        lobbyServiceManager.OnDispose();
    }

    //ログイン画面===========================

    public void LogIn()
    {
        LogInAsync(cts).Forget();
    }

    async UniTask LogInAsync(CancellationTokenSource cts)
    {
        state = LobbyState.LoggingIn;

        localUserName = lobbyUI.GetUserName();
        await autoLogIn.CoAutoLogin(cts);

        state = LobbyState.LoggedIn;
        EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>().OnLoggedIn();
        myPUID = EOSManager.Instance.GetProductUserId();
        await RefleshListAsync();

        //AutoRefleshLoop(cts.Token).Forget();
    }


    //ロビー検索画面===========================
    public void LogOut()
    {
        LogOutAsync().Forget();
    }

    public async UniTask LogOutAsync()
    {
        state = LobbyState.LoggingOut;
        await autoLogIn.LogoutAsync();

        state = LobbyState.None;
    }

    public void CreateAndJoinLobby()
    {
        CreateAndJoinLobbyAsync().Forget();
    }

    async UniTask CreateAndJoinLobbyAsync()
    {
        state = LobbyState.CreateLobbyAndJoin;
        await lobbyServiceManager.CreateLobby(lobbyUI.GetLobbyPath_Create());

        state = LobbyState.InLobby;
        lobbyUI.SwitchJoinedLobbyScreen(lm.GetCurrentLobby());
    }

    public void RefleshAvairableLobby()
    {
        RefleshListAsync().Forget();
    }

    async UniTask RefleshListAsync()
    {
        state = LobbyState.Searching;
        lobbyUI.ClearAvairableLobby();
        var searchResult = await lobbyServiceManager.SearchLobby();

        state = LobbyState.LoggedIn;
        lobbyUI.RefreshAvailableLobby(searchResult, JoinLobby);
    }

    public void SearchLobbyWithpath()
    {
        SearchLobbyWithpathAsync().Forget();
    }
    
    async UniTask SearchLobbyWithpathAsync()
    {
        state = LobbyState.Searching;
        lobbyUI.ClearAvairableLobby();
        var lobbies = await lobbyServiceManager.SearchLobby(lobbyUI.GetLobbyPath_Search());

        state = LobbyState.LoggedIn;
        lobbyUI.RefreshAvailableLobby(lobbies, JoinLobby);
    }

    void JoinLobby(Lobby lobby, LobbyDetails details)
    {
        JoinLobbyAsync(lobby, details).Forget();
    }

    async UniTask JoinLobbyAsync(Lobby lobby, LobbyDetails details)
    {
        state = LobbyState.Joining;

        bool joinSuccess = await lobbyServiceManager.Join(lobby, details);

        if (!joinSuccess)
        {
            Debug.Log("ロビー参加失敗");
            state = LobbyState.LoggedIn;
            return;
        }
        else
        {
            Debug.Log("ロビー参加成功");
        }

        state = LobbyState.InLobby;
        lobbyUI.SwitchJoinedLobbyScreen(lm.GetCurrentLobby());
    }

    //ロビー画面===========================

    public void Ready()
    {
        ReadyAsync().Forget();
    }

    async UniTask ReadyAsync()
    {
        state = LobbyState.Ready;
        lobbyServiceManager.Ready();
        await UniTask.WaitUntil(() => lobbyServiceManager.ConnectingStart(), cancellationToken: cts.Token);

        state = LobbyState.Connecting;
        await UniTask.WaitUntil(() => lobbyServiceManager.ConnectingComplete(), cancellationToken: cts.Token);

        state = LobbyState.Connected;
    }

    public void LeaveLobby()
    {
        LeaveLobbyAsync().Forget();
    }

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

        await RefleshListAsync();

        state = LobbyState.LoggedIn;
    }
}

