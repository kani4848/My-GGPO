using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;
using Epic.OnlineServices;
public static class LobbyMemberEvent
{
    public delegate void MemberJoined(ProductUserId puid, string userName);
    public static event MemberJoined AppliedUserName;
    public static void RaiseAppliedUserName(ProductUserId puid, string name) => AppliedUserName?.Invoke(puid, name);

    public delegate void MemberChanged(LobbyMember member);
    public static event MemberChanged Joined;
    public static event MemberChanged Left;
    public static event MemberChanged Death;
    public static event MemberChanged Revive;
    public static event MemberChanged OwnerChanged;
    public static event MemberChanged HeartBeat;

    public static void RaiseJoined(LobbyMember member) => Joined?.Invoke(member);
    public static void RaiseLeft(LobbyMember member) => Left?.Invoke(member);
    public static void RaiseDeath(LobbyMember member) => Death?.Invoke(member);
    public static void RaiseRevive(LobbyMember member) => Revive?.Invoke(member);
    public static void RaiseOwnerChanged(LobbyMember member) => OwnerChanged?.Invoke(member);
    public static void RaiseHeartBeat(LobbyMember member) => HeartBeat?.Invoke(member);
}

public class LobbyServiceManager
{
    EOSLobbyManager lm;

    LobbyService_search lobbyService;
    LobbyService_InLobby inLobby;
    //P2PReadyCoordinator p2p;
    P2PConnector p2pConnector;

    public LobbyServiceManager(EOSLobbyManager lm)
    {
        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）

        this.lm = lm;

        lobbyService = new LobbyService_search(lm);
        inLobby = new LobbyService_InLobby(lm);
        //p2p = new P2PReadyCoordinator(lm);
        p2pConnector = new P2PConnector(lm);
    }

    public void OnDispose()
    {
        p2pConnector.Stop();
        //p2p.Stop();
        inLobby.LeaveLobbyAction();
    }

    public async UniTask CreateLobby(string lobbyPath)
    {
        await lobbyService.CreateAndJoinAsync(lobbyPath);
        inLobby.EnterLobbyAction();
        //p2p.Start();
    }

    public async UniTask<Dictionary<Lobby, LobbyDetails>> SearchLobby(string path = "")
    {
        return await lobbyService.GetAvairableLobbyDatas(path);
    }

    public async UniTask<bool> Join(Lobby lobby,LobbyDetails details)
    {
        bool result = await lobbyService.JoinWithLobbyDetails(lobby.Id, details);
        inLobby.EnterLobbyAction();
        return result;
    }

    public void Ready()
    {
        p2pConnector.Start();
    }

    public bool ConnectingStart()
    {
        return p2pConnector.CurrentState == P2PConnector.State.Handshaking;
    }

    public bool ConnectingComplete()
    {
        return p2pConnector.CurrentState == P2PConnector.State.Connected;
    }


    public async UniTask<bool> LeaveAsync()
    {
        inLobby.LeaveLobbyAction();
        //p2p.Stop();
        p2pConnector.Stop();
        return await lobbyService.LeaveAsync();
    }
}
