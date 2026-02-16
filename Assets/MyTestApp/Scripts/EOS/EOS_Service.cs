using Cysharp.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using System.Threading;
using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System;
using Epic.OnlineServices.Lobby;

public class EOS_Service : MonoBehaviour, IEosService
{
    public EOSManager eosManager { get; set; }
    public static EOSLobbyManager lobbyManager { get; set; }
    
    [SerializeField]PlayerData playerData_Local;

    LobbyService_search lobbySearchService;
    LobbyService_InLobby inLobbyService;
    EOS_LoginService loginService;
    PlayerPeer playerPeer;

    private void Start()
    {
        lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
        lobbySearchService = new LobbyService_search(lobbyManager);
        inLobbyService = new LobbyService_InLobby(lobbyManager);
        loginService = new();
        playerData_Local = new PlayerData();
    }

    private void OnDisable()
    {
        inLobbyService?.LeaveLobby().Forget();
        inLobbyService?.ExitAction();
        playerPeer?.Dispose();
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
            playerPeer = new(EosCommonData.myPuid);

            loggedin = true;
        }
    }


    //ロビーシーン===============================================
    public async UniTask<List<SearchedLobbyData>> SearchLobby(string path = "")
    {
        var data = await lobbySearchService.SearchLobbyAuto(path);
        inLobbyService.EnterLobbyAction();
        return data;
    }

    public async UniTask<LobbyData> JoinLobby(string id)
    {
        var data = await lobbySearchService.Join(id, playerData_Local);
        if (data == null) return null;

        inLobbyService.EnterLobbyAction();
        return data;
    }

    //ロビーのメンバー全員がレディ状態になったらシーンマネジャから呼ぶ
    public async UniTask Ready(CancellationToken token)
    {
        inLobbyService.OnReady();
        await inLobbyService.WaitAllReady(token);
    }

    public void CancelReady()
    {
        inLobbyService.CancelReady();
    }

    public async UniTask<bool> StartConnectPeer(CancellationToken token)
    {
        var m = inLobbyService.GetOpponentMemberData();
        bool isOwner = lobbyManager.GetCurrentLobby().IsOwner(m.ProductId);
        return await playerPeer.StartConnectToPeer(isOwner, m.ProductId, token);
    }

    public  async UniTask<LobbyData> CreateLobby(string path)
    {
        return await lobbySearchService.CreateAndJoinAsync(path, playerData_Local);
    }

    public async UniTask LeaveLobby()
    {
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

        Debug.Log(ready);

        bool isOwner = lobbyManager.GetCurrentLobby().IsOwner(_puid);

        return new PlayerData(puid, memberName, charaId, hatCol, umaCol, ready, isOwner);
    }


    //メインシーン===============================================
    public bool GetRemoteInput()
    {
        return false;
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
        var hatCol = PackRgb(playerData.hatCol);
        var hat_att = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_HAT,
            AsInt64 = hatCol,
            ValueType = AttributeType.Int64,
            Visibility = LobbyAttributeVisibility.Public
        };

        //馬の色
        var umaCol = PackRgb(playerData.umaCol);
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
            AsInt64 = playerData.charaId,
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
        return new PlayerData();
    }

    public void SetLocalPlayerName(string playerName)
    {
        playerData_Local.name = playerName;
    }
}
