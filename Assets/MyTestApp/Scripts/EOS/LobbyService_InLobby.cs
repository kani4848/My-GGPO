using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using Cysharp.Threading.Tasks;
using System.Threading;
using PlayEveryWare.EpicOnlineServices;

public class LobbyService_InLobby
{
    EOSLobbyManager _lobbyManager;

    LobbyInterface _lobbyInterface;

    [SerializeField] private float memberPollIntervalSec = 2.0f;
    
    //退室時用リセット=============
    ProductUserId prevOwnerId = null;
    List<ProductUserId> preMemberPuids = new();

    public LobbyService_InLobby(EOSLobbyManager lm)
    {
        _lobbyManager = lm;

        _lobbyInterface = EOSManager.Instance.GetEOSPlatformInterface().GetLobbyInterface();
    }

    //ロビー内イベント開始・終了===============================
    public void EnterLobbyAction()
    {
        prevOwnerId = null;
        preMemberPuids.Clear();
        leaveLobby = false;

        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnMemberUpdated);
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
        CheckMemberLeft();

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

        void CheckMemberLeft()
        {
            if (currentMembers.Count == preMemberPuids.Count) return;

            var currrentPuid = currentMembers.Select(m => m.ProductId).ToList();

            List<ProductUserId> leftMembers = new();

            if (currrentPuid.Count < preMemberPuids.Count)
            {
                leftMembers = preMemberPuids.Except(currrentPuid).ToList();
            }

            foreach(var leftMember in leftMembers)
            {
                Debug.Log("メンバー退室");
                LobbyMemberEvent.RaiseLeft(leftMember.ToString());//情報適用イベント
            }
        }
    }

    public void ExitAction()
    {
        prevOwnerId = null;
        preMemberPuids.Clear();
        leaveLobby = false;

        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnMemberUpdated);

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
            LobbyMemberEvent.RaiseLeft(targetMemberData.ProductId.ToString());//情報適用イベント
        }

        //データのアップデート
        if (preExists && currentExists)
        {
            Debug.Log("メンバー情報アップデート");
            LobbyMemberEvent.RaiseUpdatePlayerData(playerData);
        }

        preMemberPuids = currentMemberDatas.Select(m=>m.ProductId).ToList();
    }

    //クイックマッチ===============================

    public async UniTask<bool> QuickMatch_WaitOpponent(CancellationToken token)
    {
        float timeout = 10;

        var myCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        Debug.Log($"クイックマッチ:待ち受けを開始");

        try
        {
            //getcurrentlobby一回だと固定値なので、毎フレーム取り直さないといけない
            UniTask waitOppo = UniTask.WaitUntil(() => {
                var lobby = _lobbyManager.GetCurrentLobby();
                bool oppoJoined = lobby != null && lobby.IsValid() && lobby.Members.Count == 2;
                if (oppoJoined) Debug.Log("対戦相手が入室しました");
                return oppoJoined;
            }, cancellationToken: myCts.Token);

            UniTask timeOut = UniTask.Delay(TimeSpan.FromSeconds(timeout),cancellationToken: myCts.Token);

            var result = await UniTask.WhenAny(waitOppo, timeOut);

            myCts?.Cancel();
            myCts?.Dispose();
            myCts = null;

            if (result == 0) return true;
            else
            {
                Debug.Log("待ち受けタイムアウト");
                await LeaveLobby(); 
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("ロビー待機中にクイックマッチがキャンセルされました");
            await LeaveLobby(); 
            return false;
        }
        finally
        {
            myCts?.Cancel();
            myCts?.Dispose();
            myCts = null;
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
            _lobbyManager.RemoveNotifyMemberUpdate(CheckAllReady);
        }

        void CheckAllReady(string LobbyId, ProductUserId MemberId)
        {
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
        var lobby = _lobbyManager.GetCurrentLobby();

        if(string.IsNullOrEmpty(lobby.Id)) return;
        if (leaveLobby) return;

        leaveLobby = true;

        var tcs = new UniTaskCompletionSource();

        if (lobby.Members.Count == 1)
        {
            EOS_Service.SetDisposedLobbyId(lobby.Id);
        }


        Debug.Log($"ロビー退室 ID:{lobby.Id}");
        _lobbyManager.LeaveLobby(result =>
        {
            leaveLobby = false;
            ExitAction();
            tcs.TrySetResult();
        });

        await tcs.Task;

        /*ロビー破棄処理が退室できてない扱いになってそうなのでいったん使用せず
        var destOpt = new DestroyLobbyOptions()
        {
            LocalUserId = EosCommonData.myPuid,
            LobbyId = lobby.Id,
        };

        _lobbyInterface.DestroyLobby(ref destOpt, null, (ref DestroyLobbyCallbackInfo info) =>
        {
            leaveLobby = false;
            Debug.Log($"ロビーを破棄 ID:{lobby.Id}");
            EOS_Service.SetDisposedLobbyId(lobby.Id);
            ExitAction();
            tcs.TrySetResult();
        });
        */
    }

    // データ取得 ===============================
    public LobbyMember GetOpponentMemberData()
    {
        var lobby = _lobbyManager.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid())
            return null;

        return lobby.Members
            .FirstOrDefault(m => m.ProductId != EosCommonData.myPuid);
    }
}
