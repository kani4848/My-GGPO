using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

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

        eos_Service.Init();

        cts?.Cancel();
        cts?.Dispose();
        cts = new();

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
                    currentGameMode = GameMode.Online;
                    await OnlineFlow();
                    break;

                case TitleState.GoLocal:
                    currentGameMode = GameMode.Local;
                    await MainGameFlow(currentGameMode);
                    break;

                case TitleState.GoSolo:
                    currentGameMode = GameMode.Solo;
                    await MainGameFlow(currentGameMode);
                    break;
            }
        }
    }

    public async UniTask<TitleState> TitleFlow()
    {
        state = GameState.Title;
        currentGameMode = GameMode.None;
        if (SceneManager.GetActiveScene().name != "Title") SceneManager.LoadScene(SceneName.Title.ToString());
        
        await UniTask.WaitUntil(() => IGameManager.titleSceneManager != null, cancellationToken: cts.Token);
        
        IGameManager.titleSceneManager.Init(eos_Service.GetLocalPlayerData());

        await UniTask.WaitUntil(() => IGameManager.titleSceneManager.state != TitleState.None, cancellationToken: cts.Token);

        eos_Service.SetLocalPlayerName(IGameManager.titleSceneManager.GetPlayerName());

        var nextSecne = IGameManager.titleSceneManager.state;
        IGameManager.titleSceneManager = null;
        return nextSecne;
    }

    async UniTask OnlineFlow()
    {
        while (true)
        {
            var nextScene = await LobbyFlow();
            
            if (nextScene == LobbyState.GoTitle) break;

            var nextState = await MainGameFlow(currentGameMode);

            if (nextState == MainGameState.GO_TITLE) break;
        }
        
        async UniTask<LobbyState> LobbyFlow()
        {
            if (SceneManager.GetActiveScene().name != "Lobby") SceneManager.LoadScene(SceneName.Lobby.ToString());

            state = GameState.Lobby;

            if (IGameManager.lobbySceneManager == null) await UniTask.WaitUntil(() => IGameManager.lobbySceneManager != null, cancellationToken: cts.Token);

            await eos_Service.LogInAsync(cts.Token);
            var nextState = await IGameManager.lobbySceneManager.StartFlow(eos_Service);

            cts?.Cancel();
            cts?.Dispose();
            cts = new();

            IGameManager.lobbySceneManager = null;

            return nextState;
        }
    }

    async UniTask<MainGameState> MainGameFlow(GameMode mode)
    {
        if(SceneManager.GetActiveScene().name != "Main") SceneManager.LoadScene(SceneName.Main.ToString());
        state = GameState.Main;

        if (IGameManager.mainSceneManager == null) await UniTask.WaitUntil(() => IGameManager.mainSceneManager != null, cancellationToken: cts.Token);

        var nextState = await IGameManager.mainSceneManager.StartFlow(eos_Service, charaImageHandler, mode);
        IGameManager.mainSceneManager = null;

        return nextState;
    }
}
