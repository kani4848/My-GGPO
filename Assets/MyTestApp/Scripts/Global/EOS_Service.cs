using Cysharp.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using System.Threading;
using UnityEngine;
using Epic.OnlineServices;

public class EOS_Service : MonoBehaviour, IEosService
{
    public string playerName;
    public ProductUserId localPUID { get; set; } 
    public  ProductUserId remotePUID { get; set; }
    public EOSManager EOSManager { get; set; }
    public EOSLobbyManager lobbyManager { get; set; }
    public ProductUserId myPuid { get; set; }

    [SerializeField] string myPUID_obs;
    [SerializeField] string remotePUID_obs;
    [SerializeField] AutoLogin_DeviceId loginService;
    bool login = false;

    private void Start()
    {
    }

    public async UniTask LogInAsync(string _playerName, CancellationTokenSource cts)
    {
        playerName = _playerName;
        await loginService.CoAutoLogin(cts);


        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();

        await UniTask.WaitUntil(() => lobbyManager != null, cancellationToken: cts.Token);
        Debug.Log(lobbyManager);
        lobbyManager.OnLoggedIn();
        
        localPUID = EOSManager.Instance.GetProductUserId();
        myPUID_obs = localPUID.ToString();

        login = true;
    }

    public async UniTask LogOut()
    {
        if (!login) return;

        //await loginService.LogoutAsync();
        lobbyManager = null;

        login = false;
    }
}
