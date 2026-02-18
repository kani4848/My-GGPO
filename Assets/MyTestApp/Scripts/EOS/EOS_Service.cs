using Cysharp.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using System.Threading;
using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System;
using Epic.OnlineServices.Lobby;
using System.ComponentModel;

public class EOS_Service : MonoBehaviour, IEosService
{
    public EOSManager eosManager { get; set; }
    public static EOSLobbyManager lobbyManager { get; set; }

    [SerializeField] PlayerData playerData_Local;
    [SerializeField] PlayerData playerData_Remote;

    [SerializeField] PlayerPeer.PeerState peerState;

    LobbyService_search searchService;
    LobbyService_InLobby inLobbyService;
    EOS_LoginService loginService;
    PlayerPeer playerPeer;

    [SerializeField] double ping = -1;

    public bool inLobby;
    public bool p2pConnected { get; set; } = false;
    
    public void Init()
    {
        lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
        searchService = new LobbyService_search(lobbyManager);
        inLobbyService = new LobbyService_InLobby(lobbyManager);
        loginService = new();
        playerData_Local = new PlayerData();
        playerData_Local.imageData = new PlayerImageData();
    }

    private void OnDisable()
    {
        inLobbyService?.LeaveLobby().Forget();
        inLobbyService?.ExitAction();
        playerPeer?.Dispose();
    }

    private void Update()
    {
        if (playerPeer == null) return;

        playerPeer.Tick();
        peerState = playerPeer.state;
    }

    //タイトルシーン===============================================

    bool loggedin = false;

    public async UniTask LogInAsync(CancellationToken token)
    {
        if (loggedin) return;

        bool r = await loginService.CoAutoLogin(token);
        
        if (r)
        {
            // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
            await UniTask.WaitUntil(() => lobbyManager != null, cancellationToken: token);
            lobbyManager.OnLoggedIn();
            EosCommonData.myPuid = EOSManager.Instance.GetProductUserId();
            playerData_Local.puid = EosCommonData.myPuid.ToString();
            playerPeer = new();

            loggedin = true;
        }
    }

    //ロビーシーン===============================================
    public async UniTask<List<SearchedLobbyData>> SearchLobby(string path = "")
    {
        var data = await searchService.SearchLobbyAuto(path);
        inLobbyService.EnterLobbyAction();
        return data;
    }

    public async UniTask<LobbyData> JoinLobby(string id, CancellationToken token)
    {
        playerData_Local.isOwner = false;

        var data = await searchService.Join(id, playerData_Local);
        if (data == null) return null;

        inLobbyService.EnterLobbyAction();

        AutoP2pConnect(token).Forget();

        return data;
    }

    public  async UniTask<LobbyData> CreateLobby(string path, CancellationToken token)
    {
        playerData_Local.isOwner = true;
        var data = await searchService.CreateAndJoinAsync(path, playerData_Local);

        AutoP2pConnect(token).Forget();

        return data;
    }

    public async UniTask LeaveLobby()
    {
        p2pConnected = false;
        
        playerPeer.CloseConnection();
        await inLobbyService.LeaveLobby();
        inLobbyService.ExitAction();
    }

    public static PlayerData CreatePlayerData(LobbyMember lobbyMember)
    {
        var _puid = lobbyMember.ProductId;
        string puid = _puid == null ? "" : _puid.ToString();

        string memberName = lobbyMember.DisplayName;

        LobbyAttribute hatAtt; 
        bool a = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_HAT, out hatAtt);
        Color hatCol = a ? UnpackRgb((long)hatAtt.AsInt64) : Color.black;

        LobbyAttribute umaAtt;
        bool b = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_UMA, out umaAtt);
        Color umaCol = b ? UnpackRgb((long)umaAtt.AsInt64) : Color.black;

        LobbyAttribute charaAtt;
        bool c = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_CHARA, out charaAtt);
        int charaId = c ? (int)charaAtt.AsInt64 : -1;

        LobbyAttribute readyAtt;
        bool d = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_READY, out readyAtt);
        bool ready = d ? (bool)readyAtt.AsBool : false;

        bool isOwner = lobbyManager.GetCurrentLobby().IsOwner(_puid);

        var imageData = new PlayerImageData(charaId, hatCol, umaCol);

        return new PlayerData(puid, memberName, imageData, ready, isOwner);
    }

    float handShakePollTime = 6;

    public async UniTask AutoP2pConnect(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Func<bool> StartConnect = () =>
            {
                var lobby = lobbyManager.GetCurrentLobby();
                if (lobby == null || !lobby.IsValid()) return false;
                return lobbyManager.GetCurrentLobby().Members.Count == 2;
            };

            await UniTask.WaitUntil(() => StartConnect(), cancellationToken: token);

            var opponent = inLobbyService.GetOpponentMemberData();

            if(opponent == null) continue;

            Debug.Log(opponent.ProductId);

            //データ受け入れ設定を登録&待ち受け
            var r = await playerPeer.RegisterConnectionRequestAccept(opponent.ProductId, token);

            if (r)
            {
                Debug.Log("ｐ２ｐ接続完了");
                playerData_Remote = searchService.CreatePlayerDataFromLobbyMemberData(opponent);
                break;
            }
        }
    }

    //ロビーのメンバー全員がレディ状態になったらシーンマネジャから呼ぶ
    public async UniTask<bool> Ready(CancellationToken token)
    {
        try
        {
            inLobbyService.OnReady();
            await inLobbyService.WaitAllReady(token);

            bool r;

            //相手にデータを送信or受信待ち
            if (playerData_Local.isOwner)
            {
                r = await playerPeer.SendSeedAndWaitSeedAck(token);
            }
            else
            {
                r = await playerPeer.WaitReceivingSeed(token);
            }

            if (!r)
            {
                Debug.Log("シード通信タイムアウト");
                return false;
            }

            Debug.Log("シード通信成功");

            //インプット通信のテスト
            r = await playerPeer.InputCommunicationTest(token);

            if (!r)
            {
                Debug.Log("インプット通信失敗");
                return false;
            }

            Debug.Log("インプット通信成功、ハンドシェイク完了");
            return true;
        }
        finally
        {
        }
    }

    public void CancelReady()
    {
        inLobbyService.CancelReady();
    }

    public double GetPing()
    {
        if (playerPeer == null) return -1;

        return playerPeer.pingMs;
    }

    //メインシーン===============================================

    public void SendInput(int frame, bool pressed)
    {
        playerPeer.SendInput(frame, pressed);
    }

    public PeerInputData GetRemoteInputByFrame(int frame)
    {
        return playerPeer.TryGetRemoteInput_ByFrame(frame);
    }

    public PeerInputData GetRemoteInputImmediately()
    {
        return playerPeer.TryGetRemoteInput_Latest();
    }

    public int GetRemoteShotFrame()
    {
        return playerPeer.TryGetRemoteShotFrame();
    }

    public void OnRoundReset()
    {
        playerPeer.ClearInputData();
    }

    public async UniTask<int> ShareSignalFrame(CancellationToken token)
    {
        var lobby = lobbyManager.GetCurrentLobby();
        if(lobby == null || !lobby.IsValid())
        {
            Debug.Log("かんべんしてよ");
        }

        bool isOwner = lobby.IsOwner(EosCommonData.myPuid);
        
        if (isOwner)
        {
            return  await playerPeer.SendSignalAndWait(token);
        }
        else
        {
            return await playerPeer.WaitReceivingSignal(token);
        }
    }

    public async UniTask<bool> StartQuickMatch()
    {
        return true;
    }

    //シーン共通===============================================
    public static void SetMyMemberLobbyAttribute(PlayerData playerData)
    {
        List<LobbyAttribute> atts = new();

        //名前
        var name_att = new LobbyAttribute()
        {
            Key = LobbyMember.DisplayNameKey, // "DISPLAYNAME"
            AsString = playerData.name,
            ValueType = AttributeType.String,
            Visibility = LobbyAttributeVisibility.Public
        };

        //帽子の色
        var hatCol = PackRgb(playerData.imageData.hatCol);
        var hat_att = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_HAT,
            AsInt64 = hatCol,
            ValueType = AttributeType.Int64,
            Visibility = LobbyAttributeVisibility.Public
        };

        //馬の色
        var umaCol = PackRgb(playerData.imageData.umaCol);
        var uma_att = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_UMA,
            AsInt64 = umaCol,
            ValueType = AttributeType.Int64,
            Visibility = LobbyAttributeVisibility.Public
        };

        //キャラスプライトID
        var chara_att = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_CHARA,
            AsInt64 = playerData.imageData.charaId,
            ValueType = AttributeType.Int64,
            Visibility = LobbyAttributeVisibility.Public
        };

        atts.Add(name_att);
        atts.Add(hat_att);
        atts.Add(uma_att);
        atts.Add(chara_att);
        
        lobbyManager.SetMemberAttributesBatch(atts);
    }

    //色データを送信するためlong型に変更
    public static long PackRgb(Color32 c)
    {
        // 0xRRGGBB に詰める（Alphaは今回は捨てる）
        return ((long)c.r << 16) | ((long)c.g << 8) | c.b;
    }

    //色データを開封
    public static Color32 UnpackRgb(long rgb)
    {
        // 0xRRGGBB から復元
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);
        return new Color32(r, g, b, 255);
    }

    public PlayerData GetLocalPlayerData()
    {
        return playerData_Local;
    }

    public PlayerData GetRemotePlayerData()
    {
        return playerData_Remote;
    }

    public void SetLocalPlayerName(string playerName)
    {
        playerData_Local.name = playerName;
    }

}
