using Cysharp.Threading.Tasks;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using System.Collections.Generic;
using Epic.OnlineServices;

public class LobbyServiceManager
{
    LobbyService_search searchLobbySystem;
    LobbyService_InLobby inLobby;
    //P2PReadyCoordinator p2p;
    //P2PConnector p2pConnector;

    public LobbyServiceManager(EOSLobbyManager lm)
    {
        searchLobbySystem = new LobbyService_search(lm);
        inLobby = new LobbyService_InLobby(lm);
        //p2p = new P2PReadyCoordinator(lm);
        //p2pConnector = new P2PConnector(lm);
    }

    public void OnDispose()
    {
        //p2pConnector.Stop();
        //p2p.Stop();
        inLobby.ExitAction();
    }

    public async UniTask CreateLobby(string lobbyPath)
    {
        //await searchLobbySystem.CreateAndJoinAsync(lobbyPath);
        inLobby.EnterLobbyAction();
        //p2p.Start();
    }

    public void Ready()
    {
        //p2pConnector.Start();
    }

    public bool ConnectingStart()
    {
        return true;//p2pConnector.CurrentState == P2PConnector.State.Handshaking;
    }

    public bool ConnectingComplete()
    {
        return true;//p2pConnector.CurrentState == P2PConnector.State.Connected;
    }

    /*
    public async UniTask<bool> LeaveAsync()
    {
        inLobby.ExitAction();
        //p2p.Stop();
        //p2pConnector.Stop();
        //return await searchLobbySystem.LeaveAsync();
    }
    */
}
