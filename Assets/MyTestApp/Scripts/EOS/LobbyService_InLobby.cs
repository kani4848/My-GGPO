using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using Cysharp.Threading.Tasks;
using System.Threading;

public class LobbyService_InLobby
{
    EOSLobbyManager _lobbyManager;

    [SerializeField] private float memberPollIntervalSec = 2.0f;
    private CancellationTokenSource _memberPollCts;
    UniTaskCompletionSource tcs_HB;
    CancellationTokenSource _hbCts;

    //退室時用リセット=============
    ProductUserId prevOwnerId = null;
    List<ProductUserId> preMemberPuids = new();

    public LobbyService_InLobby(EOSLobbyManager lm)
    {
        _lobbyManager = lm;

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = null;
    }

    //ロビー内イベント開始・終了===============================
    public void EnterLobbyAction()
    {   
        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = new();

        //HeartbeatLoopAsync().Forget();
    }
    public void ExitAction()
    {
        leaveLobby = false;

        _hbCts?.Cancel();
        _hbCts?.Dispose();
        _hbCts = null;

        tcs_HB?.TrySetResult();

        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = null;
    }

    //ロビー情報アップデート時のコールバック処理
    void OnLobbyUpdated()
    {
        Lobby currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null) return;
        if (EosCommonData.myPuid == null) return;

        var currentMembers = currentLobby.Members;
        if (currentMembers.Count <= 0) return;

        CheckOwnerChanged();//オーナー変更イベント発行

        void CheckOwnerChanged()
        {
            var newOwner = currentMembers.FirstOrDefault(m => currentLobby.IsOwner(m.ProductId));

            if (newOwner == null) return;

            if (prevOwnerId == null || newOwner.ProductId != prevOwnerId)
            {
                Debug.Log("オーナー変更");
                LobbyMemberEvent.RaiseOwnerChanged(EOS_Service.CreatePlayerData(newOwner));
                prevOwnerId = newOwner.ProductId;
            }
        }
    }

    //メンバー情報アップデート時のコールバック処理===============================
    private void OnMemberUpdated(string LobbyId, ProductUserId MemberId)
    {
        var currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null || !currentLobby.IsValid()) return;

        var currentMemberDatas = currentLobby.Members;
        if (currentMemberDatas.Count <= 0) return;

        var currentMemberPuids = currentMemberDatas.Select(m => m.ProductId).ToList();

        var targetMemberData = currentLobby.Members.First(m => m.ProductId == MemberId);

        /*駄目な例：PUIDが存在していてもアトリビュートが異なるとfalseを返してしまう。
        var preExists = preLobbyMemberDatas.Contains(targetMemberData);
        var currentExits = currentMemberDatas.Contains(targetMemberData);
        PUIDベースの比較をしないと意図しない結果となる*/

        /*駄目な例：リスト同士を比較するとき、参照渡しで代入しているとtrueを返す。
         * if(preMemberData == currentMemberData)
         */

        PlayerData playerData = EOS_Service.CreatePlayerData(targetMemberData);

        bool preExists = preMemberPuids.Contains(targetMemberData.ProductId);
        bool currentExists = currentMemberPuids.Contains(targetMemberData.ProductId);

        //入室
        if (!preExists && currentExists)
        {
            Debug.Log("メンバー入室");
            LobbyMemberEvent.RaiseJoined(playerData);
        }

        //退室
        if (preExists && !currentExists)
        {
            Debug.Log("メンバー退室");
            LobbyMemberEvent.RaiseLeft(playerData);//情報適用イベント
        }

        //データのアップデート
        if (preExists && currentExists)
        {
            Debug.Log("メンバー情報アップデート");
            LobbyMemberEvent.RaiseUpdatePlayerData(playerData);
        }

        preMemberPuids = currentMemberDatas.Select(m=>m.ProductId).ToList();


    }


    async UniTask HeartbeatLoopAsync()
    {
        _hbCts?.Cancel();
        _hbCts?.Dispose();
        _hbCts = new();

        // 1回目を即送る（UIの反応も良くなる）
        while (!_hbCts.Token.IsCancellationRequested)
        {
            try
            {
                UpdateHBAttributeAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                // 一時失敗してもループ継続（stale判定はUI側で吸収）
                Debug.LogWarning($"Heartbeat update failed: {e.Message}");
            }

            // interval
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _hbCts.Token);
        }

        Debug.Log($"鼓動終了");

        void UpdateHBAttributeAsync()
        {
            if (leaveLobby) return;

            var lobby = _lobbyManager.GetCurrentLobby();

            if (lobby == null || !lobby.IsValid()) return;

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string value = nowUnix.ToString();

            var attr = new LobbyAttribute
            {
                Key = EosCommonData.HB_KEY,
                ValueType = AttributeType.String,
                AsString = value,
                Visibility = LobbyAttributeVisibility.Public
            };

            _lobbyManager.SetMemberAttribute(attr);
        }
    }

    //レディ===============================
    public void OnReady()
    {
        var readyAtt = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_READY,
            AsBool = true,
            ValueType = AttributeType.Boolean,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(readyAtt);
    }

    public async UniTask WaitAllReady(CancellationToken token)
    {
        bool allReady = false;

        _lobbyManager.AddNotifyMemberUpdateReceived(CheckAllReady);

        try
        {
            CheckAllReady("",new ProductUserId());
            await UniTask.WaitUntil(() => allReady, cancellationToken: token);
        }
        finally
        {
            Debug.Log("レディ解除");
            _lobbyManager.RemoveNotifyMemberUpdate(CheckAllReady);
        }

        void CheckAllReady(string LobbyId, ProductUserId MemberId)
        {
            Debug.Log("レディチェック");
            Lobby lobby = _lobbyManager.GetCurrentLobby();
            if (lobby == null) return;
            if (lobby.Members.Count != 2) return;

            foreach (var member in lobby.Members)
            {
                if (!member.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_READY, out var memberReady))
                {
                    return;
                }
                if (!(bool)memberReady.AsBool)
                {
                    return;
                }
            }

            Debug.Log("オールレディ");
            allReady = true;
        }
    }

    public void CancelReady()
    {
        var readyAtt = new LobbyAttribute()
        {
            Key = EosCommonData.MEMBER_KEY_READY,
            AsBool = false,
            ValueType = AttributeType.Boolean,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(readyAtt);
    }

    // 退室 ===============================
    bool leaveLobby = false;
    public async UniTask LeaveLobby()
    {
        if(_lobbyManager.GetCurrentLobby() == null) return;

        leaveLobby = true;
        var tcs = new UniTaskCompletionSource();

        _lobbyManager.LeaveLobby(result =>
        {
            tcs.TrySetResult();
        });

        await tcs.Task;
    }


    // データ取得 ===============================
    public LobbyMember GetOpponentMemberData()
    {
        var lobby = _lobbyManager.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid()) return null;

        return lobby.Members
            .FirstOrDefault(m => m.ProductId != EosCommonData.myPuid);
    }
}
