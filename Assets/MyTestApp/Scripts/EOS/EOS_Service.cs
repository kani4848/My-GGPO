using Cysharp.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;
using System.Threading;
using UnityEngine;
using Epic.OnlineServices;
using System.Collections.Generic;
using System;

using Epic.OnlineServices.Stats;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.Logging;
using System.Linq;

public class EOS_Service : Singleton<EOS_Service>, IEosService
{
    public EOSManager eosManager { get; set; }
    public static EOSLobbyManager lobbyManager { get; set; }

    [SerializeField] PlayerData playerData_Local;
    [SerializeField] PlayerData playerData_Remote;

    [SerializeField] PlayerPeer.PeerState peerState;//インスペクタ監視用

    LobbyService_search searchService;
    LobbyService_InLobby inLobbyService;
    EOS_LoginService loginService;
    StatService statService;
    PlayerPeer playerPeer;

    [SerializeField] double ping = -1;

    public bool inLobby;

    public void Init()
    {
        lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
        searchService = new LobbyService_search(lobbyManager);
        inLobbyService = new LobbyService_InLobby(lobbyManager);
        statService = new StatService();
        loginService = new();
        
        playerData_Local = new PlayerData();
        playerData_Local.imageData = new PlayerImageData();

        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Error);
    }

    private void OnEnable()
    {
        LobbyMemberEvent.MemberLeftEvent += OnMemberLeft;
    }

    private void OnDisable()
    {

        LobbyMemberEvent.MemberLeftEvent -= OnMemberLeft;

        inLobbyService?.ExitAction();
        playerPeer?.Dispose();
    }

    private void Update()
    {
        if (playerPeer == null) return;

        playerPeer.Tick();
        peerState = playerPeer.state;
    }

    public static string disposedLobbyId;
    static float lobbyDisposedTime;
    const float resetDisposeLobbyIdInterval = 5f;

    public static void SetDisposedLobbyId(string lobbyId)
    {
        disposedLobbyId = lobbyId;
        lobbyDisposedTime = Time.time + resetDisposeLobbyIdInterval;
    }

    public static bool IsDisposedId(string lobbyId)
    {
        if (Time.time >= lobbyDisposedTime)
        {
            disposedLobbyId = "";
            lobbyDisposedTime = 0;
            return false;
        }

        if (lobbyId == disposedLobbyId)
        {
            Debug.Log("このロビーは既に破棄されています");
            return true;
        }
        else return false;
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
            
            playerData_Local.rankPoint = await statService.GetRankPoint(EosCommonData.myPuid);
            
            playerPeer = new();

            await statService.IngestPlayerName(playerData_Local.name);

            loggedin = true;
        }
    }

    //ロビー検索シーン===============================================
    public async UniTask<List<SearchedLobbyData>> SearchLobby(string path = "")
    {
        var data = await searchService.SearchLobby_Common(path);
        return data;
    }

    public async UniTask<LobbyData> JoinLobby(SearchedLobbyData searched, CancellationToken token)
    {
        playerData_Local.isOwner = false;

        var data = await searchService.Join(searched.id, searched.details, playerData_Local, token);
        
        if (data == null)
        {
            await inLobbyService.LeaveLobby();
            return null;
        }

        inLobbyService.EnterLobbyAction();
        return data;
    }

    public  async UniTask<LobbyData> CreateLobby(string path, CancellationToken token)
    {
        path ??= "";

        playerData_Local.isOwner = true;
        var data = await searchService.CreateAndJoinAsync(playerData_Local, token, path);

        if (data == null)
        {
            Debug.Log("ロビー作成失敗");
            return null;
        }

        inLobbyService.EnterLobbyAction();
        return data;
    }

    public static PlayerData CreatePlayerData(LobbyMember lobbyMember)
    {
        var _puid = lobbyMember.ProductId;
        string puid = _puid == null ? "" : _puid.ToString();

        string memberName = lobbyMember.DisplayName;

        var r = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_RANK, out var rank);
        int rankPoint = r ? (int)rank.AsInt64.GetValueOrDefault() : 0;

        bool a = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_HAT, out var hatAtt);
        Color hatCol = a ? UnpackRgb((long)hatAtt.AsInt64) : Color.black;

        bool b = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_UMA, out var umaAtt);
        Color umaCol = b ? UnpackRgb((long)umaAtt.AsInt64) : Color.black;

        bool c = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_CHARA, out var charaAtt);
        int charaId = c ? (int)charaAtt.AsInt64 : -1;

        bool d = lobbyMember.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_READY, out var readyAtt);
        bool ready = d ? (bool)readyAtt.AsBool : false;

        bool isOwner = lobbyManager.GetCurrentLobby().IsOwner(_puid);

        var imageData = new PlayerImageData(charaId, hatCol, umaCol);

        return new PlayerData(puid, memberName, rankPoint, imageData, ready, isOwner);
    }

    public static void SetMyMemberAttribute(PlayerData playerData)
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

        //ランクポイント
        var rank_att = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_RANK,
            AsInt64 = playerData.rankPoint,
            ValueType = AttributeType.Int64,
            Visibility = LobbyAttributeVisibility.Public
        };

        atts.Add(name_att);
        atts.Add(hat_att);
        atts.Add(uma_att);
        atts.Add(chara_att);
        atts.Add(rank_att);

        lobbyManager.SetMemberAttributesBatch(atts);
    }


    //ランキング============================================================

    public async UniTask<List<RankRow>> GetRankingDatas()
    {
        return await statService.QueryTopRanksAsync();
    }

    public async UniTask<List<RankRow>> GetMyRankingDatas()
    {
        return await statService.QueryAroundMeFallbackAsync();
    }


    //クイックマッチ============================================================
    public async UniTask<bool> QuickMatch_FindOpponent(CancellationToken token)
    {
        Debug.Log("クイックマッチサーチ開始");

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();//キャンセルされたときここで止まる

                //検索
                var searchResult = await searchService.QuickMatch_Search(playerData_Local, token);
                //ロビー未発見ならクリエイト

                if(searchResult.Status == LobbyService_search.LobbySearchStatus.Success)
                {
                    playerData_Local.isOwner = false;
                    Debug.Log($"ロビーを発見し入場しました");
                    return true;
                }

                switch (searchResult.Status)
                {
                    case LobbyService_search.LobbySearchStatus.Cancelled:
                        Debug.Log($"検索がキャンセルされました");
                        break;
                    case LobbyService_search.LobbySearchStatus.NotFount:
                        Debug.Log($"ロビーが見つかりませんでした");
                        break;
                    case LobbyService_search.LobbySearchStatus.Error:
                        Debug.Log($"検索処理でエラーが発生しました");
                        break;
                }

                token.ThrowIfCancellationRequested();//キャンセルされたときここで止まる

                await searchService.CreateAndJoinAsync(playerData_Local, token, quick:true);

                token.ThrowIfCancellationRequested();//キャンセルされたときここで止まる

                Debug.Log($"ロビー作成、待ち受けを開始");
                bool oppoJoined = await inLobbyService.QuickMatch_WaitOpponent(token);



                if (oppoJoined)
                {
                    playerData_Local.isOwner = true;
                    Debug.Log($"対戦相手がロビーに入りました");
                    break;
                }
                else
                {
                    Debug.Log($"ロビーを破棄し、再検索に移行");
                    continue;
                }
            }

            return true;
        }
        catch(OperationCanceledException)
        {
            Debug.Log($"クイックマッチがキャンセルされました。");
            return false;
        }
    }

    public async UniTask<bool> QuickMatch_HandShake(CancellationToken token)
    {
        try
        {
            //対戦相手のアトリビュート適用まで待機
            await UniTask.WaitUntil(() => 
                {
                    var lobby = lobbyManager.GetCurrentLobby();
                    var m = lobbyManager.GetCurrentLobby().Members.FirstOrDefault(x => x.ProductId != EosCommonData.myPuid);
                    if (m == default) return false;
                    if (!m.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_CHARA, out var cid)) return false;
                    return true;
                },
                cancellationToken: token);

            var opponent = inLobbyService.GetOpponentMemberData();
            playerData_Remote = CreatePlayerData(opponent);
            LobbyMemberEvent.RaiseUpdatePlayerData(playerData_Remote);

            bool r = await playerPeer.AcceptRequestP2P(opponent.ProductId, token);

            if (!r) return false;

            r = await playerPeer.handShakeFlow(token);

            playerData_Remote = CreatePlayerData(opponent);

            return r;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }


    //インロビーシーン===============================================
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

            //データ受け入れ設定を登録&待ち受け
            var r = await playerPeer.AcceptRequestP2P(opponent.ProductId, token);

            if (r)
            {
                Debug.Log("ｐ２ｐ接続完了");
                break;
            }
        }
    }

    //ロビーのメンバー全員がレディ状態になったらシーンマネジャから呼ぶ
    public async UniTask Ready(CancellationToken token)
    {
        try
        {
            inLobbyService.OnReady();
            await inLobbyService.WaitAllReady(token);
        }
        finally
        {

        }
    }

    public async UniTask<bool> AllReady(CancellationToken token)
    {
        bool r;

        try
        {
            //インプット通信のテスト
            r = await playerPeer.handShakeFlow(token);

            if (!r)
            {
                Debug.Log("インプット通信失敗");
                return false;
            }

            Debug.Log("インプット通信成功、ハンドシェイク完了");
            var opponent = inLobbyService.GetOpponentMemberData();
            playerData_Remote = CreatePlayerData(opponent);
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


    public async UniTask LeaveLobby()
    {
        Debug.Log("リーブによる通信終了");

        playerPeer.CloseConnection();
        await inLobbyService.LeaveLobby();
    }

    public void OnMemberLeft(string puid)
    {
        Debug.Log("イベントによる通信終了");
        playerPeer.CloseConnection();
    }

    //オンラインメインシーン===============================================

    public void OnRoundReset()
    {
        playerPeer.ResetBuffers();
    }

    //下とセット
    public async UniTask<bool> SendRoundDataAndWaitAck(RoundData roundData, CancellationToken token)
    {
        var lobby = lobbyManager.GetCurrentLobby();

        if(lobby == null || !lobby.IsValid())
        {
            Debug.Log("ロビー情報取得失敗");
            return false;
        }

        return await playerPeer.SendRoundDataAndWaitAck(roundData, token);
    }
    public async UniTask<RoundData> WaitReceivingRoundData(CancellationToken token)
    {
        return await playerPeer.WaitReceivingSignal(token);
    }
    //上とセット

    public async UniTask<int> SendInputResultAndWaitRemote(CancellationToken token)
    {
        return await playerPeer.SendFinalInputAndWaitRemote(token);
    }

    public void SendInput(int frame, bool pressed)
    {
        playerPeer.SendInput(frame, pressed);
    }

    public bool GetRemoteInput_MainLoop()
    {
        return playerPeer.TryGetRemoteInput();
    }

    //スタッツ処理===============================================

    //シーン共通===============================================
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
        if (playerData_Remote.puid == default)
        {
            return new PlayerData();
        }

        return playerData_Remote;
    }

    public void SetLocalPlayerName(string playerName)
    {
        playerData_Local.name = playerName;
    }

    public async UniTask<int> GetBountyChangedAmount(MatchResult result)
    {
        int delta;

        switch (result)
        {
            case MatchResult.WIN_P1:
                delta = StatService.GetWinnerDelta(playerData_Local.rankPoint, playerData_Remote.rankPoint);
                break;
                
            case MatchResult.WIN_P2:
                //勝者のポイントなのでマイナスが付きます
                delta = -StatService.GetWinnerDelta(playerData_Remote.rankPoint, playerData_Local.rankPoint);
                break;

            default:
                return 0;
        }

        await statService.IngestRankPoint(delta);
        return delta;
    }
}
