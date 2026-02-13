using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
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
    public List<PlayerData> currentMemberDatas;

    public LobbyData (string id, string path, List<PlayerData> currentMemberDatas)
    {
        this.id = id;
        this.path = path;
        this.currentMemberDatas = currentMemberDatas;
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

    public PlayerData(
        string puid = "", 
        string name = "no name", 
        int charaId = -1, 
        Color hatCol = default, 
        Color umaCol = default, 
        bool ready = false)
    {
        this.puid = puid;
        this.name = name;
        this.charaId = charaId == -1 ? UnityEngine.Random.Range(0, 13) : charaId;
        this.hatCol = hatCol == default? GetRondmColor() : hatCol;
        this.umaCol = umaCol == default ? GetRondmColor() : umaCol;
        this.ready = ready;

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
    public static ProductUserId myPuid { get; set; }
    
    //メンバー属性キーは必ず大文字
    public static string HB_KEY = "HB";
    public static string HB_STALE_KEY = "STALE";
    public static string MEMBER_KEY_READY = "READY";

    public static string MEMBER_KEY_HAT = "HAT";
    public static string MEMBER_KEY_UMA = "UMA";
    public static string MEMBER_KEY_CHARA = "CHARA";

    //ロビーキー、IDは必ず小文字
    public static string LobbyCommonKey = "bucket";
    public static string LobbyCommonId = "test";
    
    public static string LOBBY_KEY_PATH = "pass";
    public static string LOBBY_KEY_OWNER_NAME = "owner";
    public static string LOBBY_KEY_CHARA = "chara";
    public static string LOBBY_KEY_HAT = "hat";

    public UniTask<List<SearchedLobbyData>> SearchLobby(string path = "");

    public UniTask<LobbyData> JoinLobby(string id);

    public UniTask Ready(CancellationToken token);

    public void CancelReady();

    public UniTask<bool> StartConnectPeer(CancellationToken token);
    public UniTask LeaveLobby();

    public UniTask<LobbyData> CreateLobby(string path);

    public PlayerData GetLocalPlayerData();

    public PlayerData GetRemotePlayerData();

    public bool GetRemoteInput();

    public UniTask<bool> StartQuickMatch();
}
