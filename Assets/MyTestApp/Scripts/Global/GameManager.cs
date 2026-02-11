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

public class GameManager : Singleton<GameManager>
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
    [SerializeField] CharaImageHandler charaImageHandler;

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

     void Start()
    {
        state = GameState.Initialize;
       
        cts?.Cancel();
        cts?.Dispose();
        cts = new();

        charaImageHandler.SetCharaImageData();

        GameLoop().Forget();
    }

    async UniTask GameLoop()
    {
        while (true)
        {
            var nextScene = await TitleFlow();
            
            switch (nextScene)
            {
                case TitleState.GoOnline:
                    //StartOnlineMode();
                    break;
                case TitleState.GoLocal:
                    currentGameMode = GameMode.Local;
                    break;
                case TitleState.GoSolo:
                    currentGameMode = GameMode.Solo;
                    break;
            }

            await MainGameFlow(currentGameMode);
        }
    }

    public async UniTask<TitleState> TitleFlow()
    {
        state = GameState.Title;
        currentGameMode = GameMode.None;
        if (SceneManager.GetActiveScene().name != "Title") SceneManager.LoadScene(SceneName.Title.ToString());
        await eos_Service.LogOut();

        await UniTask.WaitUntil(() => IGameManager.titleSceneManager != null, cancellationToken: cts.Token);
        
        IGameManager.titleSceneManager.Init(charaImageHandler);

        await UniTask.WaitUntil(() => IGameManager.titleSceneManager.state != TitleState.None, cancellationToken: cts.Token);

        var nextSecne = IGameManager.titleSceneManager.state;
        IGameManager.titleSceneManager = null;
        return nextSecne;
    }

    public void StartOnlineMode() //タイトル画面のボタンに直置き
    {
        StartOnlineModeAsync().Forget();

        async UniTask StartOnlineModeAsync()
        {
            currentGameMode = GameMode.Online;
            //await eos_Service.LogInAsync(playerName,cts);

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
   
    public async UniTask LobbyFlow()
    {

        if (SceneManager.GetActiveScene().name != "Lobby") SceneManager.LoadScene(SceneName.Lobby.ToString());
        state = GameState.Lobby;

        if (IGameManager.lobbySceneManager == null) await UniTask.WaitUntil(() => IGameManager.lobbySceneManager != null, cancellationToken: cts.Token);
        
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
        if(SceneManager.GetActiveScene().name != "Main") SceneManager.LoadScene(SceneName.Main.ToString());
        state = GameState.Main;

        if (IGameManager.mainSceneManager == null) await UniTask.WaitUntil(() => IGameManager.mainSceneManager != null, cancellationToken: cts.Token);

        await IGameManager.mainSceneManager.StartFlow(charaImageHandler, eos_Service, mode);
        IGameManager.mainSceneManager = null;
    }
}