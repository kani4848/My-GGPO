using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;

using Cysharp.Threading;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Runtime.ConstrainedExecution;
using Unity.VisualScripting;

public class LobbyService_InLobby
{
    EOSLobbyManager _lobbyManager;

    [SerializeField] private float memberPollIntervalSec = 2.0f;
    private CancellationTokenSource _memberPollCts;

    HashSet<LobbyMember> prevMembers = new();
    ProductUserId prevOwnerId;
    Dictionary<ProductUserId, long> lastBeatDic = new();
    Dictionary<ProductUserId, bool> deadMemberList = new();
    Dictionary<ProductUserId, string> memberNameDic = new();

    UniTaskCompletionSource tcs_HB;

    private CancellationTokenSource _hbCts;

    LobbyMember opponentdata;
    ProductUserId ownerPUID;

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

        HeartbeatLoopAsync().Forget();
        CheckOtherMemberAlive(_memberPollCts.Token).Forget();
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

        memberNameDic.Clear();
        prevMembers.Clear();
        prevOwnerId = null;

        lastBeatDic.Clear();
        deadMemberList.Clear();

        opponentdata = null;
        ownerPUID = null;
    }

    //コールバックとループ処理===============================

    //ロビー情報アップデート時のコールバック処理
    void OnLobbyUpdated()
    {
        Lobby currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null) return;
        if (IEosService.myPuid == null) return;

        var currentMembers = new HashSet<LobbyMember>(currentLobby.Members);
        if (currentMembers.Count <= 0) return;
        if (currentMembers == prevMembers) return;

        ownerPUID = null;
        opponentdata = null;

        foreach(var member in currentMembers)
        {
            if (currentLobby.IsOwner(member.ProductId)) ownerPUID = member.ProductId;
            if (member.ProductId != IEosService.myPuid) opponentdata = member;
        }

        var currentPUIDs = new HashSet<ProductUserId>(currentMembers.Select(m => m.ProductId));
        var prevPUIDs = new HashSet<ProductUserId>(prevMembers.Select(m => m.ProductId));

        OnJoined();//入室イベント発行
        OnLeft();//退室イベント発行
        OnOwnerChanged();//オーナー変更イベント発行

        prevMembers = currentMembers;

        void OnJoined()
        {
            foreach (var joined in currentPUIDs.Except(prevPUIDs))
            {
                var joinedMember = currentMembers.FirstOrDefault(m => m.ProductId == joined);
                LobbyMemberEvent.RaiseJoined(EOS_Service.CreatePlayerData(joinedMember));
            }
        }

        void OnLeft()
        {
            foreach (var removed in prevPUIDs.Except(currentPUIDs))
            {
                var removedMember = prevMembers.FirstOrDefault(m => m.ProductId == removed);
                LobbyMemberEvent.RaiseLeft(EOS_Service.CreatePlayerData(removedMember));
            }
        }

        void OnOwnerChanged()
        {
            var newOwner = currentMembers.FirstOrDefault(m => currentLobby.IsOwner(m.ProductId));

            if (newOwner == null) return;

            if (newOwner.ProductId != prevOwnerId)
            {
                LobbyMemberEvent.RaiseOwnerChanged(EOS_Service.CreatePlayerData(newOwner));
                prevOwnerId = newOwner.ProductId;
            }
        }
    }


    //メンバー情報アップデート時のコールバック処理
    private void OnMemberUpdated(string LobbyId, ProductUserId MemberId)
    {
        var currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null || !currentLobby.IsValid()) return;

        var currentMembers = currentLobby.Members;
        if (currentMembers.Count <= 0) return;
        var memberData = currentLobby.Members.First(m => m.ProductId == MemberId);

        //他メンバーハートビートの更新
        LobbyAttribute lastBeatAtt;
        memberData.MemberAttributes.TryGetValue(IEosService.HB_KEY, out lastBeatAtt);

        if (lastBeatAtt != null)
        {
            var newLastBeat = long.Parse(memberData.MemberAttributes[IEosService.HB_KEY].AsString);

            if (lastBeatDic.ContainsKey(MemberId))
            {
                lastBeatDic[MemberId] = newLastBeat;
            }
            else
            {
                lastBeatDic.Add(MemberId, newLastBeat);
            }

            LobbyMemberEvent.RaiseHeartBeat(EOS_Service.CreatePlayerData(memberData));
        }

        UpdateMemberName();
        OnReady();//readyイベント発行
                  
        //名前適用完了イベント発行

        void UpdateMemberName()
        {
            var updateMember = currentMembers.FirstOrDefault(m => m.ProductId == MemberId);
            string prevName;

            bool nameExisted = !memberNameDic.TryGetValue(MemberId, out prevName);
            bool nameChanged = updateMember.DisplayName != prevName;

            if (!nameExisted || nameChanged)
            {
                var updateMemberData = EOS_Service.CreatePlayerData(updateMember);
                LobbyMemberEvent.RaiseAppliedUserName(updateMemberData);
                memberNameDic[MemberId] = updateMember.DisplayName;
            }
        }


        void OnReady()
        {
            foreach (var member in currentMembers)
            {
                LobbyAttribute currentReadyAtt;
                if (!member.MemberAttributes.TryGetValue(IEosService.MEMBER_KEY_READY, out currentReadyAtt)) continue;

                var prevData = prevMembers.FirstOrDefault(m => m.ProductId == member.ProductId);

                if (prevData == null)
                {
                    Debug.Log($"ready is {(bool)currentReadyAtt.AsBool}");
                    LobbyMemberEvent.RaiseReady(EOS_Service.CreatePlayerData(member));
                    continue;
                }

                LobbyAttribute prevReadyAtt;
                bool prevReadyExits = prevData.MemberAttributes.TryGetValue(IEosService.MEMBER_KEY_READY, out prevReadyAtt);

                if (!prevReadyExits)
                {
                    Debug.Log($"ready is {(bool)currentReadyAtt.AsBool}");
                    LobbyMemberEvent.RaiseReady(EOS_Service.CreatePlayerData(member));
                    continue;
                }

                if (currentReadyAtt.AsBool == prevReadyAtt.AsBool) continue;

                Debug.Log($"ready is {(bool)currentReadyAtt.AsBool}");
                LobbyMemberEvent.RaiseReady(EOS_Service.CreatePlayerData(member));
            }
        }
    }

    //他メンバーハートビートの定期自動チェック処理
    async UniTask CheckOtherMemberAlive(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Lobby currentLobby = _lobbyManager.GetCurrentLobby();

            if (currentLobby == null)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            var members = currentLobby.Members;
            if (members.Count <= 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            Dictionary<ProductUserId, bool> newDeadList = new();

            foreach (LobbyMember member in members)
            {
                long lastBeat;
                if (!lastBeatDic.TryGetValue(member.ProductId, out lastBeat)) continue;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                bool newDead;
                bool wasDead;

                if (!deadMemberList.TryGetValue(member.ProductId, out wasDead))
                {
                    newDeadList.Add(member.ProductId, false);
                    continue;
                }
    
                newDead = now - lastBeat >= 5;

                if (newDead && wasDead != newDead)
                {
                    LobbyMemberEvent.RaiseDeath(EOS_Service.CreatePlayerData(member));
                }

                if (!newDead && wasDead != newDead)
                {
                    LobbyMemberEvent.RaiseRevive(EOS_Service.CreatePlayerData(member));
                }

                newDeadList.Add(member.ProductId, newDead);
            }

            deadMemberList = newDeadList;
            await UniTask.Delay(TimeSpan.FromSeconds(1));
        }
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
                Key = IEosService.HB_KEY,
                ValueType = AttributeType.String,
                AsString = value,
                Visibility = LobbyAttributeVisibility.Public
            };

            _lobbyManager.SetMemberAttribute(attr);
        }
    }

    //レディ===============================
    public async UniTask OnReady(CancellationToken token)
    {
        var readyAtt = new LobbyAttribute()
        {
            Key = IEosService.MEMBER_KEY_READY,
            AsBool = true,
            ValueType = AttributeType.Boolean,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(readyAtt);

        bool allReady = false;

        _lobbyManager.AddNotifyLobbyUpdate(CheckAllReady);

        try
        {
            CheckAllReady();
            await UniTask.WaitUntil(() => allReady, cancellationToken: token);
        }
        finally
        {
            _lobbyManager.RemoveNotifyLobbyUpdate(CheckAllReady);
        }

        void CheckAllReady()
        {
            Lobby lobby = _lobbyManager.GetCurrentLobby();
            if (lobby == null) return;
            if(lobby.Members.Count != 2)return;

            foreach(var member in lobby.Members)
            {
                if (!member.MemberAttributes.TryGetValue(IEosService.MEMBER_KEY_READY, out var memberReady)) return;
                if (!(bool)memberReady.AsBool) return;
            }

            allReady = true;
        }
    }

    public void CancelReady()
    {
        var readyAtt = new LobbyAttribute()
        {
            Key = IEosService.MEMBER_KEY_READY,
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
        _hbCts?.Cancel();
        _hbCts?.Dispose();
        _hbCts = null;

        leaveLobby = true;

        var tcs = new UniTaskCompletionSource();

        _lobbyManager.LeaveLobby(result =>
        {
            tcs.TrySetResult();
        });

        await tcs.Task;

        //leavelobbyより先に実行するとヌルエラー
        ExitAction();
    }


    // データ取得 ===============================
    public LobbyMember GetOpponentData()
    {
        return opponentdata;
    }

    public ProductUserId GetOwnerPUID()
    {
        return ownerPUID;
    }
}
