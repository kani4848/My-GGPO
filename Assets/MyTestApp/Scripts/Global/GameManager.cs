using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using Epic.OnlineServices.Auth;

public enum SceneName
{
    Title,
    Lobby,
    Main,
}

public class GameManager : MonoBehaviour
{
    CancellationTokenSource cts;
    enum GameState
    {
        None,
        Initialize,
        Title,
        Lobby,
        Main,
    }

    [SerializeField] GameState state = GameState.None;
    [SerializeField] GameMode currentGameMode = GameMode.None;
    [SerializeField] EOS_Service eos_Service;

    public SceneName currentScene;

    private void OnDisable()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void OnApplicationQuit()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void Awake()
    {
        state = GameState.Initialize;
       
        cts?.Cancel();
        cts?.Dispose();
        cts = new();

        switch (SceneManager.GetActiveScene().name)
        {
            case "a":
                break;
        }
    }

    public async UniTask TitleFlow()
    {
        currentGameMode = GameMode.None;
        state = GameState.Title;
        await eos_Service.LogOut();
    }

    public void StartOnlineMode() //タイトル画面のボタンに直置き
    {
        StartOnlineModeAsync().Forget();

        async UniTask StartOnlineModeAsync()
        {
            currentGameMode = GameMode.Online;

            IGameManager.titleSceneManager.ExitScene();
            string playerName = IGameManager.titleSceneManager.GetPlayerName();
            await eos_Service.LogInAsync(playerName,cts);

            bool goTitle = false;

            while (!goTitle)
            {
                await LobbyFlow();
                goTitle = IGameManager.lobbySceneManager.state == LobbyState.GoTitle;
                IGameManager.lobbySceneManager = null;
                
                if (goTitle) break;
                
                await MainGameFlow(currentGameMode);
            }
        }
    }

    public void StartSoloMode() //タイトル画面のボタンに直置き
    {
        StartModeAsync().Forget();

        async UniTask StartModeAsync()
        {
            currentGameMode = GameMode.Solo;
            IGameManager.titleSceneManager.ExitScene();
            await MainGameFlow(currentGameMode);
        }
    }

    public void StartLocalMode() //タイトル画面のボタンに直置き
    {
        StartModeAsync().Forget();

        async UniTask StartModeAsync()
        {
            currentGameMode = GameMode.Local;
            IGameManager.titleSceneManager.ExitScene();
            await MainGameFlow(GameMode.Local);
        }
    }

    public async UniTask LobbyFlow()
    {
        SceneManager.LoadScene(SceneName.Lobby.ToString());
        state = GameState.Lobby;

        if (IGameManager.lobbySceneManager == null) await UniTask.WaitUntil(() => IGameManager.lobbySceneManager != null, cancellationToken: cts.Token);
        Debug.Log("aa");
        IGameManager.lobbySceneManager.Init(eos_Service);

        await UniTask.WaitUntil(() =>
            IGameManager.lobbySceneManager.state == LobbyState.GoTitle
            || IGameManager.lobbySceneManager.state == LobbyState.GoMain, 
            cancellationToken: cts.Token);

        cts?.Cancel();
        cts.Dispose();
        cts = new();
    }

    public async UniTask MainGameFlow(GameMode mode)
    {        
        SceneManager.LoadScene(SceneName.Main.ToString());
        
        state = GameState.Main;
        if (IGameManager.mainSceneManager == null) await UniTask.WaitUntil(() => IGameManager.mainSceneManager != null, cancellationToken: cts.Token);
        await IGameManager.mainSceneManager.StartFlow(eos_Service, mode);
    }
}