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
using static UnityEngine.Rendering.DebugUI;

public class LobbyService_InLobby
{
    EOSLobbyManager _lobbyManager;

    private Dictionary<Lobby, LobbyDetails> _cachedResults = new();

    [SerializeField] private float memberPollIntervalSec = 2.0f;
    private CancellationTokenSource _memberPollCts;

    HashSet<LobbyMember> prevMembers = new();
    ProductUserId prevOwnerId;
    Dictionary<ProductUserId, long> lastBeatDic = new();
    Dictionary<ProductUserId, bool> deadMemberList = new();
    Dictionary<ProductUserId, string> memberNameList = new();

    UniTaskCompletionSource tcs_HB;

    private CancellationTokenSource _hbCts;
    private UniTask _hbTask;

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
        _hbCts = new CancellationTokenSource();
        _hbTask = HeartbeatLoopAsync(_hbCts.Token);

        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = new();

        CheckOtherMemberAlive(_memberPollCts.Token).Forget();

    }
    public void LeaveLobbyAction()
    {
        _hbCts?.Cancel();
        _hbCts?.Dispose();
        _hbCts = null;
        tcs_HB?.TrySetResult();

        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = null;

        memberNameList.Clear();
        prevMembers.Clear();
        prevOwnerId = null;

        lastBeatDic.Clear();
        deadMemberList.Clear();   
    }

    //コールバックとループ処理===============================

    //ロビー情報アップデート時のコールバック処理
    void OnLobbyUpdated()
    {
        Lobby currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null) return;
        if (LobbySceneManager.myPUID == null) return;

        var currentMembers = new HashSet<LobbyMember>(currentLobby.Members);
        if (currentMembers.Count <= 0) return;
        if (currentMembers == prevMembers) return;

        var currentPUIDs = new HashSet<ProductUserId>(currentMembers.Select(m => m.ProductId));
        var prevPUIDs = new HashSet<ProductUserId>(prevMembers.Select(m => m.ProductId));

        //入室イベント発行
        foreach (var joined in currentPUIDs.Except(prevPUIDs))
        {
            var joinedMember = currentMembers.FirstOrDefault(m => m.ProductId == joined);
            LobbyMemberEvent.RaiseJoined(joinedMember);
        }

        //退室イベント発行
        foreach (var removed in prevPUIDs.Except(currentPUIDs))
        {
            var removedMember = prevMembers.FirstOrDefault(m => m.ProductId == removed);
            LobbyMemberEvent.RaiseLeft(removedMember);
        }

        prevMembers = currentMembers;
            
        //オーナー変更イベント発行
        var newOwner = currentMembers.FirstOrDefault(m => currentLobby.IsOwner(m.ProductId));

        if (newOwner == null) return;

        if (newOwner.ProductId != prevOwnerId)
        {
            LobbyMemberEvent.RaiseOwnerChanged(newOwner);
            prevOwnerId = newOwner.ProductId;
        }
    }

    //メンバー情報アップデート時のコールバック処理
    private void OnMemberUpdated(string LobbyId, ProductUserId MemberId)
    {
        var currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null || !currentLobby.IsValid()) return;

        var members = currentLobby.Members;
        if (members.Count <= 0) return;
        var memberData = currentLobby.Members.First(m => m.ProductId == MemberId);

        //他メンバーハートビートの更新
        LobbyAttribute lastBeatAtt;
        memberData.MemberAttributes.TryGetValue(LobbySceneManager.HB_KEY, out lastBeatAtt);

        if (lastBeatAtt != null)
        {
            var newLastBeat = long.Parse(memberData.MemberAttributes[LobbySceneManager.HB_KEY].AsString);

            if (lastBeatDic.ContainsKey(MemberId))
            {
                lastBeatDic[MemberId] = newLastBeat;
            }
            else
            {
                lastBeatDic.Add(MemberId, newLastBeat);
            }

            LobbyMemberEvent.RaiseHeartBeat(memberData);
        }

        UpdateMemberName();

        //名前適用完了イベント発行
        
        void UpdateMemberName()
        {
            string currentName = members.FirstOrDefault(m => m.ProductId == MemberId).DisplayName;
            string prevName;

            if (!memberNameList.TryGetValue(MemberId, out prevName))
            {
                LobbyMemberEvent.RaiseAppliedUserName(MemberId, currentName);
                memberNameList.Add(MemberId, currentName);
                return;
            }

            bool nameChanged = currentName != prevName;
            if (nameChanged) LobbyMemberEvent.RaiseAppliedUserName(MemberId, currentName);
            memberNameList[MemberId] = currentName;
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
                    LobbyMemberEvent.RaiseDeath(member);
                }

                if (!newDead && wasDead != newDead)
                {
                    LobbyMemberEvent.RaiseRevive(member);
                }

                newDeadList.Add(member.ProductId, newDead);
            }

            deadMemberList = newDeadList;
            await UniTask.Delay(TimeSpan.FromSeconds(1));
        }
    }

    //ハートビート実働部分===============================
    private async UniTask HeartbeatLoopAsync(CancellationToken ct)
    {
        // 1回目を即送る（UIの反応も良くなる）
        while (!ct.IsCancellationRequested)
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
            await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
        }

        Debug.Log($"鼓動終了");

        void UpdateHBAttributeAsync()
        {
            tcs_HB = new UniTaskCompletionSource();

            var lobby = _lobbyManager.GetCurrentLobby();

            if (lobby == null || !lobby.IsValid())
            {
                tcs_HB.TrySetException(new Exception("Lobby is not valid"));
                return;
            }

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string value = nowUnix.ToString();

            var attr = new LobbyAttribute
            {
                Key = LobbySceneManager.HB_KEY,
                ValueType = AttributeType.String,
                AsString = value,
                Visibility = LobbyAttributeVisibility.Public
            };

            _lobbyManager.SetMemberAttribute(attr);
        }
    }
}
