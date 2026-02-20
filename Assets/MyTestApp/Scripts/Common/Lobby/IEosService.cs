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

    public UniTask<LobbyData> JoinLobby(string id, CancellationToken token);

    public UniTask<bool> Ready(CancellationToken token);

    public void CancelReady();

    public UniTask LeaveLobby();

    public UniTask<LobbyData> CreateLobby(string path, CancellationToken token);

    public PlayerData GetLocalPlayerData();

    public PlayerData GetRemotePlayerData();

    public void OnRoundReset();

    public UniTask AutoP2pConnect(CancellationToken token);

    public double GetPing();

    //メインゲーム==================================================
    public void SendInput(int frame, bool pressed);
    public bool GetRemoteInput_MainLoop();
    
    public UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token);

    public UniTask<RoundData> WaitReceivingRoundData(CancellationToken token);

    public UniTask<int> SendInputResultAndWaitRemote(CancellationToken token);

    public UniTask<bool> StartQuickMatch();
    public UniTask CloseConnection();
}
