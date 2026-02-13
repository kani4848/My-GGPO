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

public class LobbyData
{
    public string id;
    public string path;
    public int maxPlayers;
    public List<PlayerData> currentMemberDatas;

    public LobbyData (string id, string path, int maxPlayers, List<PlayerData> currentMemberDatas)
    {
        this.id = id;
        this.path = path;
        this.maxPlayers = maxPlayers;
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
    public static string LobbyCustomKey = "custom";

    public UniTask<List<LobbyData>> SearchLobby(string path = "");

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
