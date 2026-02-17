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
public class PlayerData
{
    public string puid;
    public string name;
    public int charaId;
    public Color hatCol;
    public Color umaCol;
    public bool ready;
    public bool isOwner;

    public PlayerData(
        string puid = "", 
        string name = "no name", 
        int charaId = -1, 
        Color hatCol = default, 
        Color umaCol = default, 
        bool ready = false,
        bool isOwner = false)
    {
        this.puid = puid;
        this.name = name;
        this.charaId = charaId == -1 ? UnityEngine.Random.Range(0, 13) : charaId;
        this.hatCol = hatCol == default? GetRondmColor() : hatCol;
        this.umaCol = umaCol == default ? GetRondmColor() : umaCol;
        this.ready = ready;
        this.isOwner = isOwner;

        Color GetRondmColor()
        {
            return new Color(
                UnityEngine.Random.value,
                UnityEngine.Random.value,
                UnityEngine.Random.value);
        }
    }
}

public interface IEosService
{
    public UniTask<List<SearchedLobbyData>> SearchLobby(string path = "");

    public UniTask<LobbyData> JoinLobby(string id, CancellationToken token);

    public UniTask Ready(CancellationToken token);

    public void CancelReady();

    public UniTask LeaveLobby();

    public UniTask<LobbyData> CreateLobby(string path, CancellationToken token);

    public PlayerData GetLocalPlayerData();

    public PlayerData GetRemotePlayerData();

    public bool GetRemoteInput();

    public UniTask<bool> StartQuickMatch();

    public UniTask AutoHandShake(CancellationToken token);
}
