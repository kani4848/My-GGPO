using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using System;
using Epic.OnlineServices.Lobby;

public class SearchedLobbyData_Quick
{
    public string lobbyId;
    public LobbyDetails details;

    public SearchedLobbyData_Quick(string lobbyId, LobbyDetails details)
    {
        this.lobbyId = lobbyId;
        this.details = details;
    }
}

public class SearchedLobbyData
{
    public string id;
    public LobbyDetails details;
    
    public string ownerName;
    public int rankPoint;
    public int charaId;
    public Color hatCol;

    public SearchedLobbyData(string lobbyId, LobbyDetails detailes, string ownerName, int rankPoint, int charaId, Color hatCol)
    {
        this.id = lobbyId;
        this.details = detailes;

        this.ownerName = ownerName;
        this.rankPoint = rankPoint;
        this.charaId = charaId;
        this.hatCol = hatCol;
    }
}

public class LobbyData
{
    public string id;
    public string path;
    public List<PlayerData> playerDatas;
    public LobbyDetails details;
    public LobbyData (string id, string path, List<PlayerData> currentMemberDatas, LobbyDetails details = null)
    {
        this.id = id;
        this.path = path;
        this.playerDatas = currentMemberDatas;
        this.details = details;
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
    public int rankPoint;

    public PlayerData(
        string puid = "",
        string name = "no name",
        int rankPoint = 0,
        PlayerImageData imageData = default,
        bool ready = false,
        bool isOwner = false
        )
    {
        this.puid = puid;
        this.name = name;
        this.rankPoint = rankPoint;
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

public class RoundData
{
    public int roundCount;
    public int signalFrame;//メインフレーム基準
    public int timeUpFrame;//メインフレーム基準

    public RoundData(int roundCount, int signalFrame, int timeUpFrame)
    {
        this.roundCount = roundCount;
        this.signalFrame = signalFrame;
        this.timeUpFrame = timeUpFrame;
    }
}


public interface IEosService
{
    public bool p2pConnected { get; set; }

    public void Init();

    public UniTask<List<SearchedLobbyData>> SearchLobby(string path = "");

    public UniTask<LobbyData> JoinLobby(SearchedLobbyData searched, CancellationToken token);

    public UniTask Ready(CancellationToken token);

    public UniTask<bool> AllReady(CancellationToken token);


    public void CancelReady();

    public UniTask LeaveLobby();

    public UniTask<LobbyData> CreateLobby(string path, CancellationToken token);

    public PlayerData GetLocalPlayerData();

    public PlayerData GetRemotePlayerData();

    public void OnRoundReset();

    public UniTask AutoP2pConnect(CancellationToken token);

    public double GetPing();

    //ランキング==================================================

    public UniTask<List<RankRow>> GetRankingDatas();

    //メインゲーム==================================================
    public void SendInput(int frame, bool pressed);
    public bool GetRemoteInput_MainLoop();
    
    public UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token);

    public UniTask<RoundData> WaitReceivingRoundData(CancellationToken token);

    public UniTask<int> SendInputResultAndWaitRemote(CancellationToken token);

    public UniTask<bool> QuickMatch_FindOpponent(CancellationToken token);
    public UniTask<bool> QuickMatch_HandShake(CancellationToken token);

    public UniTask CloseConnection();

    public UniTask<int> GetBountyChangedAmount();
}
