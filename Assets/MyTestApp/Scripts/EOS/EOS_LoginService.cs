using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

public class EOS_LoginService
{
    [SerializeField] private float waitEosReadyTimeoutSec = 15f;

    public async UniTask<bool> CoAutoLogin(CancellationToken token)
    {
        // 念のため毎回初期化
        token.ThrowIfCancellationRequested();

        // 1) EOSManager / Platform が準備できるまで待つ（returnで抜けない）
        bool eosReady = await WaitEosReadyAsync(token);
        if (!eosReady) return false;

        // 2) すでにログイン済みなら終了
        var puid = EOSManager.Instance.GetProductUserId();
        if (puid != null && puid.IsValid())
        {
            Debug.Log("[AutoLogin_DeviceId] Already logged in.");
            return false;
        }

        // 3) まずは CreateDeviceId せずに Login を試す（既存DeviceId環境で余計なErrorを出さない）
        var loginInfo = await ConnectLoginAsync(token);

        if (loginInfo.ResultCode == Result.Success)
        {
            Debug.Log($"[AutoLogin_DeviceId] Connect Login Success. PUID={EOSManager.Instance.GetProductUserId()}");
            return true;
        }

        // 4) 初回などで Connect User が無い場合は作成
        if (loginInfo.ResultCode == Result.InvalidUser)
        {
            Debug.Log("[AutoLogin_DeviceId] InvalidUser -> CreateConnectUser");
            await CreateConnectUserAsync(loginInfo.ContinuanceToken, token);
            Debug.Log($"[AutoLogin_DeviceId] CreateConnectUser Success. PUID={EOSManager.Instance.GetProductUserId()}");
            return true;
        }

        // 5) 「DeviceIdが無い/無効」系っぽい失敗だけ、ここで DeviceId を作ってリトライ
        if (IsLikelyMissingDeviceId(loginInfo.ResultCode))
        {
            Debug.Log($"[AutoLogin_DeviceId] Login failed ({loginInfo.ResultCode}) -> Try CreateDeviceId and retry login.");

            var createResult = await CreateDeviceIdAsync(token);
            if (createResult != Result.Success && createResult != Result.DuplicateNotAllowed)
            {
                Debug.LogError($"[AutoLogin_DeviceId] CreateDeviceId failed: {createResult}");
                return false;
            }

            // リトライ
            var retryInfo = await ConnectLoginAsync(token);

            if (retryInfo.ResultCode == Result.Success)
            {
                Debug.Log($"[AutoLogin_DeviceId] Connect Login Success (retry). PUID={EOSManager.Instance.GetProductUserId()}");
                return true;
            }

            if (retryInfo.ResultCode == Result.InvalidUser)
            {
                Debug.Log("[AutoLogin_DeviceId] InvalidUser (retry) -> CreateConnectUser");
                await CreateConnectUserAsync(retryInfo.ContinuanceToken, token);
                Debug.Log($"[AutoLogin_DeviceId] CreateConnectUser Success. PUID={EOSManager.Instance.GetProductUserId()}");
                return true;
            }

            Debug.LogError($"[AutoLogin_DeviceId] Connect Login failed (retry): {retryInfo.ResultCode}");
            return false;
        }

        // その他の失敗は普通にエラーとして扱う
        Debug.LogError($"[AutoLogin_DeviceId] Connect Login failed: {loginInfo.ResultCode}");
        return false;
    }

    private async UniTask<bool> WaitEosReadyAsync(CancellationToken ct)
    {
        float start = Time.realtimeSinceStartup;

        while (EOSManager.Instance == null || EOSManager.Instance.GetEOSPlatformInterface() == null)
        {
            ct.ThrowIfCancellationRequested();

            if (Time.realtimeSinceStartup - start > waitEosReadyTimeoutSec)
            {
                Debug.LogError("[AutoLogin_DeviceId] EOSManager / PlatformInterface is not ready. Check scene bootstrap objects.");
                return false;
            }

            // 次フレームまで待つ（returnしない）
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        return true;
    }

    private UniTask<LoginCallbackInfo> ConnectLoginAsync(CancellationToken ct)
    {
        var tcs = new UniTaskCompletionSource<LoginCallbackInfo>();

        // ここはサンプルのラッパーをそのまま使う（最小改変）
        string displayName = "dsa";

        EOSManager.Instance.StartConnectLoginWithOptions(
            ExternalCredentialType.DeviceidAccessToken,
            token: null,
            displayname: displayName,
            onloginCallback: (LoginCallbackInfo info) =>
            {
                tcs.TrySetResult(info);
            }
        );

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    private UniTask<Result> CreateDeviceIdAsync(CancellationToken ct)
    {
        var connect = EOSManager.Instance.GetEOSConnectInterface();
        if (connect == null)
        {
            Debug.LogError("[AutoLogin_DeviceId] ConnectInterface is null.");
            return UniTask.FromResult(Result.NotFound);
        }

        var tcs = new UniTaskCompletionSource<Result>();

        var options = new CreateDeviceIdOptions
        {
            DeviceModel = SystemInfo.deviceModel
        };

        connect.CreateDeviceId(ref options, null, (ref CreateDeviceIdCallbackInfo info) =>
        {
            // DuplicateNotAllowed は「既にある」なので正常扱いで進めてOK
            tcs.TrySetResult(info.ResultCode);
        });

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    private UniTask CreateConnectUserAsync(ContinuanceToken token, CancellationToken ct)
    {
        var tcs = new UniTaskCompletionSource();

        EOSManager.Instance.CreateConnectUserWithContinuanceToken(
            token,
            (CreateUserCallbackInfo info) =>
            {
                if (info.ResultCode != Result.Success)
                {
                    Debug.LogError($"[AutoLogin_DeviceId] CreateConnectUser failed: {info.ResultCode}");
                    tcs.TrySetException(new Exception(info.ResultCode.ToString()));
                    return;
                }
                tcs.TrySetResult();
            }
        );

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    private bool IsLikelyMissingDeviceId(Result code)
    {
        // 「DeviceId作ってから来い」系の失敗をここに寄せる
        // ※ プロジェクトで実測したコードが分かったらここを絞るのが最強
        return code == Result.InvalidAuth
            //|| code == Result.InvalidToken
            || code == Result.NotFound;
    }
}
