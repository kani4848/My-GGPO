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

public enum LobbyState
{
    None = 0,
    LoggingIn = 10,
    LoggedIn =20,

    LoggingOut = 25,

    CreateLobbyAndJoin = 30,
    
    Joining = 40,

    Searching = 50,
    InLobby = 60,
    LeavingLobby = 70,
    Error = 1000,
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
    [SerializeField] LobbyService lobbyService;
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

    public static string localUserName = "Player";
    public static ProductUserId myPUID;

    public static string bKey = "bucket";
    public static string bId = "test"; // Host側Createと合わせる
    public static string customKey = "custom";

    public static string emptyPlayerName = "No name";
    //bool autoReflesh = true;

    CancellationTokenSource cts;


    private void Start()
    {
        cts = new();

        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        EOSLobbyManager lm = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();

        lobbyService.Init(lm);
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

    private void OnDestroy()
    {
        cts.Cancel();
        cts.Dispose();
    }

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

        AutoRefleshLoop(cts.Token).Forget();
    }

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
        
        LobbyData lobbyData = await lobbyService.CreateAndJoinAsync(lobbyUI.GetLobbyPath_Create(), cts.Token);

        lobbyService.SetMyLobbyDisplayName();

        state = LobbyState.InLobby;
        lobbyUI.SwitchJoinedLobbyScreen(lobbyData, lobbyService.GetCurrentLobbyMemberPUIDs());
    }

    public void RefleshAvairableLobby()
    {
        RefleshListAsync().Forget();
    }

    async UniTask RefleshListAsync()
    {
        state = LobbyState.Searching;
        lobbyUI.ClearAvairableLobby();
        var datas = await lobbyService.GetAvairableLobbyDatas();

        state = LobbyState.LoggedIn;
        lobbyUI.RefreshAvailableLobby(datas, JoinLobby);
    }

    public void SearchLobbyWithpath()
    {
        SearchLobbyWithpathAsync().Forget();
    }
    
    async UniTask SearchLobbyWithpathAsync()
    {
        state = LobbyState.Searching;
        lobbyUI.ClearAvairableLobby();
        var datas = await lobbyService.GetAvairableLobbyDatas(lobbyUI.GetLobbyPath_Search());

        state = LobbyState.LoggedIn;
        lobbyUI.RefreshAvailableLobby(datas, JoinLobby);
    }

    void JoinLobby(LobbyData lobbyData)
    {
        JoinLobbyAsync(lobbyData).Forget();
    }

    async UniTask JoinLobbyAsync(LobbyData lobbyData)
    {
        state = LobbyState.Joining;

        bool joinSuccess = await lobbyService.JoinWithLobbyDetails(lobbyData.id,lobbyData.details);

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

        lobbyService.SetMyLobbyDisplayName();

        state = LobbyState.InLobby;
        lobbyUI.SwitchJoinedLobbyScreen(lobbyData, lobbyService.GetCurrentLobbyMemberPUIDs());
    }


    public void LeaveLobby()
    {
        LeaveLobbyAsync().Forget();
    }

    async UniTask LeaveLobbyAsync()
    {
        state = LobbyState.LeavingLobby;
        Result result = await lobbyService.LeaveAsync();

        if (result != Result.Success)
        {
            Debug.Log("ロビー退室失敗" + result.ToString());
            return;
        }

        await RefleshListAsync();

        state = LobbyState.LoggedIn;
    }
}

