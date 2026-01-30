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

public struct LobbyData
{
    public string path;
    public string id;
    public uint maxLobbyMembers;
    public uint avairableSlots;
    public List<ProductUserId> PUIDs;
    public LobbyDetails details;
}

public static class LobbyMemberEvent
{
    public delegate void MemberJoined(ProductUserId puid, string userName);
    public static event MemberJoined AppliedUserName;
    public static void RaiseAppliedUserName(ProductUserId puid, string name) => AppliedUserName?.Invoke(puid, name);

    public delegate void MemberChanged(LobbyMember member);
    public static event MemberChanged Joined;
    public static event MemberChanged Left;
    public static event MemberChanged Death;
    public static event MemberChanged OwnerChanged;

    public static void RaiseJoined(LobbyMember member) => Joined?.Invoke(member);
    public static void RaiseLeft(LobbyMember member) => Left?.Invoke(member);
    public static void RaiseDeath(LobbyMember member) => Death?.Invoke(member);
    public static void RaiseOwnerChanged(LobbyMember member) => OwnerChanged?.Invoke(member);
}

public sealed class LobbyService : MonoBehaviour
{
    [Header("Create")]
    [SerializeField] private uint maxMembers = 2;
    [Header("Join")]
    [SerializeField] private string joinLobbyId = ""; // Hostが作成したLobbyIdを貼る

    [SerializeField] EOSLobbyManager _lobbyManager;

    // SearchResultsは Dictionary<Lobby, LobbyDetails>
    private Dictionary<Lobby, LobbyDetails> _cachedResults = new();

    Lobby currentLobby;

    // Polling (crash / editor stop / network drop 対策)
    [SerializeField] private float memberPollIntervalSec = 2.0f;
    private CancellationTokenSource _memberPollCts;

    public void Init(EOSLobbyManager lm)
    {
        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        _lobbyManager = lm;

        _lobbyManager.LobbyChanged += (_, e) =>
        {
            Debug.Log($"[LobbyChanged] type={e.LobbyChangeType} lobbyId={e.LobbyId}");
        };

        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();

        //ロビー入室中の定期チェック
        LobbyEvent.lobbyStateChangedEvent += OnLobbyStateChanged;
    }

    private void OnDisable()
    {
        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = null;

        _prevMembers.Clear();

        LobbyEvent.lobbyStateChangedEvent -= OnLobbyStateChanged;
    }

    Dictionary<ProductUserId, long> lastBeatDic = new();
    Dictionary<ProductUserId, bool> deadMemberList = new();

    async UniTask CheckMemberAtt(CancellationToken token)
    {
        while (token.IsCancellationRequested)
        {
            currentLobby = _lobbyManager.GetCurrentLobby();

            if (currentLobby == null)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            var members = currentLobby.Members;
            if (members.Count == 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            Dictionary<ProductUserId, bool> newDeadList = new();

            foreach (LobbyMember member in members)
            {
                long lastBeat;
                lastBeatDic.TryGetValue(member.ProductId, out lastBeat);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                bool wasDead;    
                deadMemberList.TryGetValue(member.ProductId, out wasDead);
                bool newDead = now - lastBeat >= 8;

                if(newDead && wasDead != newDead)
                {
                    LobbyMemberEvent.RaiseDeath(member);
                    Debug.Log($"{member.DisplayName} is dead");
                }

                newDeadList.Add(member.ProductId, newDead);
            }

            deadMemberList = newDeadList;
            await UniTask.Delay(TimeSpan.FromSeconds(1));
        }
    }

    void OnLobbyStateChanged(LobbyState lobbyState)
    {
        if(lobbyState == LobbyState.InLobby)
        {
            _memberPollCts?.Cancel();
            _memberPollCts?.Dispose();
            _memberPollCts = new CancellationTokenSource();
            CheckMemberAtt(_memberPollCts.Token).Forget();
        }
        else
        {
            _memberPollCts?.Cancel();
            _memberPollCts?.Dispose();
            _memberPollCts = null;
        }
    }

    void OnLobbyUpdated()
    {
        currentLobby = _lobbyManager.GetCurrentLobby();
        Debug.Log("ロビーアップデート");
        if (currentLobby == null) return;
        if (LobbySceneManager.myPUID == null) return;

        RefreshMemberDiffAndRaiseEvents(currentLobby);
    }

    Lobby prevLobby;

    //ユーザー名適用のタイミングでJOINイベント発行
    private void OnMemberUpdated(string LobbyId, ProductUserId MemberId)
    {
        var currentLobby = _lobbyManager.GetCurrentLobby();
        if (currentLobby == null || !currentLobby.IsValid())
        {
            prevLobby = null;
            return;
        }

        tcs_HB?.TrySetResult();

        var members = currentLobby.Members;
        if (members.Count < 0) return;
        var memberData = currentLobby.Members.First(m => m.ProductId == MemberId);

        //ハートビートの更新
        var newLastBeat = long.Parse(memberData.MemberAttributes[LobbySceneManager.HB_KEY].AsString);

        if (lastBeatDic.ContainsKey(MemberId))
        {
            lastBeatDic[MemberId] = newLastBeat;
        }
        else
        {
            lastBeatDic.Add(MemberId, newLastBeat);
        }

        //名前変更の通知
        if (prevLobby == null)
        {
            LobbyMemberEvent.RaiseAppliedUserName(MemberId, memberData.DisplayName);
        }
        else
        {
            foreach (LobbyMember member in members)
            {
                LobbyMember prevMemberData = prevLobby.Members.FirstOrDefault(m => m.ProductId == member.ProductId);
                bool nameChanged = member.DisplayName != prevMemberData.DisplayName;
                if (nameChanged) LobbyMemberEvent.RaiseAppliedUserName(MemberId, member.DisplayName);
            }
        }

        prevLobby = currentLobby;
    }

    HashSet<LobbyMember> _prevMembers = new();
    ProductUserId prevOwner;

    private void RefreshMemberDiffAndRaiseEvents(Lobby lobby)
    {
        var currentMembers = new HashSet<LobbyMember>(lobby.Members);
        var currentPUIDs = new HashSet<ProductUserId>(lobby.Members.Select(m => m.ProductId));
        var prevPUIDs = new HashSet<ProductUserId>(_prevMembers.Select(m => m.ProductId));

        if (currentMembers.Count < 0) return;
        if (currentMembers == _prevMembers) return;

        foreach (var joined in currentPUIDs.Except(prevPUIDs))
        {
            Debug.Log("join");
            var joinedMember = lobby.Members.FirstOrDefault(m => m.ProductId == joined);
            LobbyMemberEvent.RaiseJoined(joinedMember);
        }

        foreach (var removed in prevPUIDs.Except(currentPUIDs))
        {
            Debug.Log("leave");
            var removedMember = _prevMembers.FirstOrDefault(m => m.ProductId == removed);
            LobbyMemberEvent.RaiseLeft(removedMember);
        }

        var newOwner = lobby.Members.First(m => lobby.IsOwner(m.ProductId));

        if(newOwner.ProductId != prevOwner)
        {
            LobbyMemberEvent.RaiseOwnerChanged(newOwner);
        }

        prevOwner = null;
        _prevMembers = currentMembers;
    }

    UniTaskCompletionSource tcs_HB;

    /// <summary>
    /// 自分自身の LobbyMember Attribute を更新する
    /// （Heartbeat 用：HB = UnixTimeSeconds など）
    /// </summary>
    public void UpdateMyMemberAttributeAsync()
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

    // ---- Host: Create ----
    public async UniTask<LobbyData> CreateAndJoinAsync(string lobbyPath, CancellationToken token)
    {
        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = LobbySceneManager.bId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false, // 今回不要
        };

        //全検索用にバケットを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = LobbySceneManager.bKey,
            ValueType = AttributeType.String,
            AsString = LobbySceneManager.bId,
            Visibility = LobbyAttributeVisibility.Public,
        });

        if(lobbyPath != "")
        {
            //カスタム用にアトリビュートを付与
            lobbySettings.Attributes.Add(new LobbyAttribute
            {
                Key = LobbySceneManager.customKey,
                ValueType = AttributeType.String,
                AsString = lobbyPath,
                Visibility = LobbyAttributeVisibility.Public,
            });
        }

        var myLobby = await CreateLobbyAwait(lobbySettings);
        await ModifyLobbyAwait(myLobby);

        LobbyData myLobbyData = new()
        {
            path = lobbyPath,
            id = myLobby.Id,
            maxLobbyMembers = maxMembers,
            avairableSlots = myLobby.AvailableSlots,
        };

        return myLobbyData;

        UniTask<Lobby> CreateLobbyAwait(Lobby lobbyProps)
        {
            var tcs = new UniTaskCompletionSource<Lobby>();

            _lobbyManager.CreateLobby(lobbyProps, r =>
            {
                Lobby myLobby = _lobbyManager.GetCurrentLobby();
                tcs.TrySetResult(myLobby);
            });

            return tcs.Task;
        }

        UniTask ModifyLobbyAwait(Lobby lobby)
        {
            var tcs = new UniTaskCompletionSource();
            
            _lobbyManager.ModifyLobby(lobby, modifyResult =>
            {
                tcs.TrySetResult();
            });

            return tcs.Task;
        }
    }

    public UniTask<bool> JoinWithLobbyDetails(string lobbyId, LobbyDetails lobbyDetails)
    {
        var tcs = new UniTaskCompletionSource<bool>(); 

        if (string.IsNullOrEmpty(lobbyId) || lobbyDetails == null)
        {
            Debug.LogError("JoinWithLobbyDetails: invalid args.");
            tcs.TrySetResult(false);
        }

        _lobbyManager.JoinLobby(lobbyId, lobbyDetails, presenceEnabled: false, result =>
        {
            Debug.Log($"JoinLobby result={result}");
            var current = _lobbyManager.GetCurrentLobby();
            Debug.Log($"CurrentLobbyId={current?.Id}");

            tcs.TrySetResult(result == Result.Success);    
        });

        return tcs.Task;
    }

    public void SetMyLobbyDisplayName()
    {
        var attr = new LobbyAttribute()
        {
            Key = LobbyMember.DisplayNameKey, // "DISPLAYNAME"
            AsString = LobbySceneManager.localUserName,
            ValueType = AttributeType.String,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(attr);
    }

    // ---- Any: Leave ----
    public async UniTask<Result> LeaveAsync()
    {
        prevOwner = null;
        prevLobby = null;
        lastBeatDic.Clear();
        deadMemberList.Clear();

        Result result_ = await LeaveAwait();

        return result_;

        UniTask<Result> LeaveAwait()
        {
            var tcs = new UniTaskCompletionSource<Result>();

            _lobbyManager.LeaveLobby(result =>
            {
                tcs.TrySetResult(result);
            });

            return tcs.Task;
        }
    }

    public UniTask<List<LobbyData>> GetAvairableLobbyDatas(string lobbyPath = "")
    {
        string key;
        string id;

        if(lobbyPath == "")
        {
            key = LobbySceneManager.bKey;
            id = LobbySceneManager.bId;
        }
        else
        {
            key = LobbySceneManager.customKey;
            id = lobbyPath;
        }

        var tcs = new UniTaskCompletionSource<List<LobbyData>>();
        List<LobbyData> lobbyDatas = new();
        _lobbyManager.SearchByAttribute(key, id, OnSearchCompleted);
        return tcs.Task;

        void OnSearchCompleted(Result result)
        {
            _cachedResults = _lobbyManager.GetSearchResults();

            // 表示用に並べ替え（例：空きスロット多い順）
            var lobbies = _cachedResults.Keys
                .Where(l => l != null && l.IsValid())
                .OrderByDescending(l => l.AvailableSlots)
                .ToList();


            foreach (var lobby in lobbies)
            {
                LobbyData lobbyData = GetLobbyData(lobby);
                lobbyDatas.Add(lobbyData);
            }

            if (lobbies.Count == 0)
            {
                Debug.Log("No lobbies found.");
            }

            tcs.TrySetResult(lobbyDatas);
        }
    }

    LobbyData GetLobbyData(Lobby lobby)
    {
        string lobbyId = lobby.Id;
        LobbyDetails details = _cachedResults[lobby];

        List<ProductUserId> puids = new();

        foreach(LobbyMember member in lobby.Members)
        {
            puids.Add(member.ProductId);
        }

        EOSManager.Instance.GetProductUserId();

        LobbyData lobbyData = new()
        {
            id = lobby.Id,
            maxLobbyMembers = lobby.MaxNumLobbyMembers,
            avairableSlots = lobby.AvailableSlots,
            details = details,
        };

        return lobbyData;
    }

    public List<LobbyMember> GetCurrentLobbyMember()
    {
        Lobby current = _lobbyManager.GetCurrentLobby();
        List<LobbyMember> puids = current.Members;
        return puids;
    }
}
