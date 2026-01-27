using System;
using System.Collections;
using UnityEngine;

using Cysharp.Threading;

using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using Cysharp.Threading.Tasks;
using System.Threading;
using TMPro;
using UnityEngine.UI;
using PlayEveryWare.EpicOnlineServices.Samples;

public class AutoLogin_DeviceId : MonoBehaviour
{
    [Tooltip("EOSManager/Platform の準備を待つ最大秒数")]
    [SerializeField] private float waitEosReadyTimeoutSec = 15f;

    private bool isDone;



    public async UniTask CoAutoLogin(CancellationTokenSource cts)
    {
        // 1) EOSManager と Platform が起きるまで待つ（ここが無いと今回のエラーになる）
        float start = Time.realtimeSinceStartup;

        while (EOSManager.Instance == null || EOSManager.Instance.GetEOSPlatformInterface() == null)
        {
            if (Time.realtimeSinceStartup - start > waitEosReadyTimeoutSec)
            {
                Debug.LogError("[AutoLogin_DeviceId] EOSManager / PlatformInterface が準備できませんでした。SceneにEOSの土台(GameObject/Prefab)が入っているか確認してください。");
                break;
            }
            return;
        }

        // 2) すでにログイン済みなら終了
        if (EOSManager.Instance.GetProductUserId() != null)
        {
            Debug.Log("[AutoLogin_DeviceId] すでにログイン済みです。");
            return;
        }

        // 3) Device IDを作成（初回のみ。2回目以降は DuplicateNotAllowed が返ることがある）
        var connectInterface = EOSManager.Instance.GetEOSConnectInterface();
        if (connectInterface == null)
        {
            Debug.LogError("[AutoLogin_DeviceId] ConnectInterface が取得できません。EOS初期化を確認してください。");
            return;
        }

        var options = new CreateDeviceIdOptions
        {
            DeviceModel = SystemInfo.deviceModel
        };

        connectInterface.CreateDeviceId(ref options, null, OnCreateDeviceIdComplete);

        await UniTask.WaitUntil(() => isDone, cancellationToken: cts.Token);


        void OnCreateDeviceIdComplete(ref CreateDeviceIdCallbackInfo info)
        {
            if (info.ResultCode != Result.Success && info.ResultCode != Result.DuplicateNotAllowed)
            {
                Debug.LogError($"[AutoLogin_DeviceId] CreateDeviceId failed: {info.ResultCode}");
                return;
            }

            // 4) Connectログイン開始（サンプルUILoginMenuと同じ呼び方）
            string displayName = SafeGetDisplayName();

            EOSManager.Instance.StartConnectLoginWithOptions(
                ExternalCredentialType.DeviceidAccessToken,
                token: null,
                displayname: displayName,
                onloginCallback: OnConnectLoginComplete
            );
        }
    }

    public async UniTask LogoutAsync()
    {
        // すでにログアウト済みなら何もしない
        if (EOSManager.Instance == null ||
            EOSManager.Instance.GetProductUserId() == null ||
            !EOSManager.Instance.GetProductUserId().IsValid())
        {
            Debug.Log("[AutoLogin_DeviceId] already logged out.");
            isDone = false;
            return;
        }

        var connectInterface = EOSManager.Instance.GetEOSConnectInterface();
        if (connectInterface == null)
        {
            Debug.LogError("[AutoLogin_DeviceId] ConnectInterface is null.");
            return;
        }

        var tcs = new UniTaskCompletionSource();

        var options = new LogoutOptions
        {
            LocalUserId = EOSManager.Instance.GetProductUserId()
        };

        connectInterface.Logout(ref options, null, (ref LogoutCallbackInfo info) =>
        {
            if (info.ResultCode == Result.Success)
            {
                Debug.Log("[AutoLogin_DeviceId] Logout Success.");
                isDone = false; // ← 次回ログイン可能に戻す
                tcs.TrySetResult();
            }
            else
            {
                Debug.LogError($"[AutoLogin_DeviceId] Logout Failed: {info.ResultCode}");
                tcs.TrySetResult(); // 失敗しても進める
            }
        });

        await tcs.Task;
    }


    private void OnConnectLoginComplete(LoginCallbackInfo info)
    {
        if (info.ResultCode == Result.Success)
        {
            Debug.Log($"[AutoLogin_DeviceId] Connect Login Success. PUID={EOSManager.Instance.GetProductUserId()}");
            isDone = true;
            return;
        }

        // 5) 初回などで Connect User がまだ無い場合は作成が必要（これもサンプルでやってるやつ）
        if (info.ResultCode == Result.InvalidUser)
        {
            Debug.Log("[AutoLogin_DeviceId] InvalidUser → CreateConnectUser を実行します。");

            EOSManager.Instance.CreateConnectUserWithContinuanceToken(
                info.ContinuanceToken,
                (CreateUserCallbackInfo createUserInfo) =>
                {
                    if (createUserInfo.ResultCode != Result.Success)
                    {
                        Debug.LogError($"[AutoLogin_DeviceId] CreateConnectUser failed: {createUserInfo.ResultCode}");
                        return;
                    }

                    Debug.Log($"[AutoLogin_DeviceId] CreateConnectUser Success. PUID={EOSManager.Instance.GetProductUserId()}");
                    isDone = true;
                }
            );
            return;
        }

        Debug.LogError($"[AutoLogin_DeviceId] Connect Login failed: {info.ResultCode}");
    }

    private static string SafeGetDisplayName()
    {
        // 端末によっては取れないので安全に
        try
        {
            var name = Environment.UserName;
            if (string.IsNullOrWhiteSpace(name)) return "Player";
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }
        catch
        {
            return "Player";
        }
    }
}
