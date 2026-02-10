using Cysharp.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Bootstrapper : MonoBehaviour
{
    [Header("Next Scene")]
    [SerializeField] private string nextSceneName = "Title";

    [SerializeField] ConfigService configService; //もろもろ設定ファイル

    private async void Awake()
    {
        await InitializeServicesAsync();
    }

    private async UniTask InitializeServicesAsync()
    {
        // 例：Configを先にロード（ロケール/設定等）
        if (configService != null)await configService.LoadAsync();

        // 例：EOS初期化 + Login（DeviceID等）
        //EOSログイン処理

        //GameManagerへタスク委譲
    }
}

// ------------------ 以下はダミー例：あなたの実装に置換 ------------------

public sealed class ConfigService : MonoBehaviour
{
    public async UniTask LoadAsync()
    {
        // 設定ファイルやPlayerPrefsのロード等
        await UniTask.Yield();
    }
}