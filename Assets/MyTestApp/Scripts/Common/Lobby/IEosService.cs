using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using System;

public class SearchedLobbyData
{
    public string lobbyId;
    public string ownerName;
    public int charaId;
    public Color hatCol;

    public SearchedLobbyData(string lobbyId, string ownerName, int charaId, Color hatCol)
    {
        this.lobbyId = lobbyId;
        this.ownerName = ownerName;
        this.charaId = charaId;
        this.hatCol = hatCol;
    }
}

public class LobbyData
{
    public string id;
    public string path;
    public List<PlayerData> playerDatas;

    public LobbyData (string id, string path, List<PlayerData> currentMemberDatas)
    {
        this.id = id;
        this.path = path;
        this.playerDatas = currentMemberDatas;
    }
}

[Serializable]
public struct PlayerData
{
    public string puid;
    public string name;
    public PlayerImageData imageData;
    public bool ready;
    public bool isOwner;

    public PlayerData(
        string puid = "",
        string name = "no name",
        PlayerImageData imageData = default,
        bool ready = false,
        bool isOwner = false)
    {
        this.puid = puid;
        this.name = name;
        this.imageData = imageData == default ? new PlayerImageData() : imageData;
        this.ready = ready;
        this.isOwner = isOwner;
    }
}

public class PeerInputData
{
    public int frame;
    public bool input;

    public PeerInputData(int _frame = -1, bool _input = false)
    {
        frame = _frame;
        input = _input;
    }
}


public interface IEosService
{
    public bool p2pConnected { get; set; }

    public void Init();

    public UniTask<List<SearchedLobbyData>> SearchLobby(string path = "");

    public UniTask<LobbyData> JoinLobby(string id, CancellationToken token);

    public UniTask<bool> Ready(CancellationToken token);

    public void CancelReady();

    public UniTask LeaveLobby();

    public UniTask<LobbyData> CreateLobby(string path, CancellationToken token);

    public PlayerData GetLocalPlayerData();

    public PlayerData GetRemotePlayerData();


    public void SendInput(int frame, bool pressed);
    public PeerInputData GetRemoteInput();

    public UniTask<bool> StartQuickMatch();

    public UniTask AutoP2pConnect(CancellationToken token);

    public double GetPing();
}
