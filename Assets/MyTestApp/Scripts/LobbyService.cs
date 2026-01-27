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

public struct LobbyData
{
    public string path;
    public string id;
    public uint maxLobbyMembers;
    public uint avairableSlots;
    public Action joinAction;
    public List<ProductUserId> PUIDs;
}

public static class LobbyMemberEvent
{
    public delegate void MemberChanged(ProductUserId puid, string displayName);
    public static event MemberChanged Joined;
    public static event MemberChanged Left;

    public static void RaiseJoined(ProductUserId puid, string name) => Joined?.Invoke(puid, name);
    public static void RaiseLeft(ProductUserId puid, string name) => Left?.Invoke(puid, name);
}

public sealed class LobbyService : MonoBehaviour
{
    [Header("Create")]
    [SerializeField] private uint maxMembers = 2;
    [Header("Join")]
    [SerializeField] private string joinLobbyId = ""; // Hostが作成したLobbyIdを貼る

    private EOSLobbyManager _lobbyManager;

    // SearchResultsは Dictionary<Lobby, LobbyDetails>
    private Dictionary<Lobby, LobbyDetails> _cachedResults = new();

    Lobby currentLobby;
    LobbyMember myMemberData;

    HashSet<ProductUserId> _prevMembers = new();

    public void Init(EOSLobbyManager lm)
    {
        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        _lobbyManager = lm;

        _lobbyManager.LobbyChanged += (_, e) =>
        {
            Debug.Log($"[LobbyChanged] type={e.LobbyChangeType} lobbyId={e.LobbyId}");
        };

    }

    private void OnEnable()
    {
        _lobbyManager.AddNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.AddNotifyMemberUpdateReceived(OnLobbyMemberUpdated);
    }

    private void OnDisable()
    {
        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnLobbyMemberUpdated);
    }


    private void OnLobbyMemberUpdated(string lobbyId, ProductUserId changedMemberId)
    {
        Debug.Log("ロビーメンバーアップデート");
        var lobby = _lobbyManager.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid()) return;
        if (lobby.Id != lobbyId) return;

        RefreshMemberDiffAndRaiseEvents();

        void RefreshMemberDiffAndRaiseEvents()
        {
            var lobby = _lobbyManager.GetCurrentLobby();
            if (lobby == null || !lobby.IsValid()) return;

            var current = new HashSet<ProductUserId>(lobby.Members.Select(m => m.ProductId));

            // 入室（current に居て prev に居ない）
            foreach (var added in current.Except(_prevMembers))
            {
                var name = lobby.Members.FirstOrDefault(m => m.ProductId == added)?.DisplayName ?? "";
                LobbyMemberEvent.RaiseJoined(added, name);
                Debug.Log($"[LobbyMember] Joined: {name} ({added})");
            }

            // 退室（prev に居て current に居ない）
            foreach (var removed in _prevMembers.Except(current))
            {
                // 退室後は Members から名前が取れないことがあるので空文字許容
                LobbyMemberEvent.RaiseLeft(removed, "");
                Debug.Log($"[LobbyMember] Left: ({removed})");
            }

            _prevMembers = current;
        }
    }


    void OnLobbyUpdated()
    {
        currentLobby = _lobbyManager.GetCurrentLobby();

        if (currentLobby == null) return;
        if (LobbySceneManager.myPUID == null) return;

        var myMemberData = currentLobby.Members.FirstOrDefault(m => m.ProductId == LobbySceneManager.myPUID);
    }

    // ---- Host: Create ----
    public async UniTask<LobbyData> CreateAsync(string lobbyPath, CancellationToken token)
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


        SetMyLobbyDisplayName();

        await UniTask.WhenAny(
            UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: token),
            UniTask.WaitUntil(() => {
                if (myMemberData == null) return false;
                return myMemberData.DisplayName == LobbySceneManager.localUserName;
                }, cancellationToken: token)
        );

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

    // ---- Client: Join (LobbyId -> Search -> Join) ----
    public void JoinByLobbyId()
    {
        if (!EOSManager.Instance.GetProductUserId().IsValid())
        {
            Debug.LogError("Not logged in (ProductUserId invalid).");
            return;
        }

        if (string.IsNullOrEmpty(joinLobbyId))
        {
            Debug.LogError("joinLobbyId is empty.");
            return;
        }

        // 1) LobbyIdで検索して LobbyDetails を得る
        _lobbyManager.SearchByLobbyId(joinLobbyId, searchResult =>
        {
            Debug.Log($"SearchByLobbyId result={searchResult}");

            if (searchResult != Result.Success)
                return;

            // 2) SearchResults から LobbyDetails を取り出す
            //    SearchResults: Dictionary<Lobby, LobbyDetails>
            var results = _lobbyManager.GetSearchResults();
            var found = results.Keys.FirstOrDefault(l => string.Equals(l.Id, joinLobbyId, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                Debug.LogError("Search succeeded but lobby not found in results.");
                return;
            }

            LobbyDetails lobbyDetails = results[found];
            if (lobbyDetails == null)
            {
                Debug.LogError("LobbyDetails is null.");
                return;
            }

            // 3) Join
            _lobbyManager.JoinLobby(found.Id, lobbyDetails, presenceEnabled: false, JoinCompleted);
        });
    }

    public void JoinWithLobbyDetails(string lobbyId, LobbyDetails lobbyDetails)
    {
        if (string.IsNullOrEmpty(lobbyId) || lobbyDetails == null)
        {
            Debug.LogError("JoinWithLobbyDetails: invalid args.");
            return;
        }

        _lobbyManager.JoinLobby(lobbyId, lobbyDetails, presenceEnabled: false, result =>
        {
            Debug.Log($"JoinLobby result={result}");
            var current = _lobbyManager.GetCurrentLobby();
            Debug.Log($"CurrentLobbyId={current?.Id}");
        });
    }

    private void JoinCompleted(Result result)
    {
        Debug.Log($"JoinLobby result={result}");

        var current = _lobbyManager.GetCurrentLobby();
        Debug.Log($"CurrentLobbyId={current?.Id}");

        if(result == Result.Success)
        {
            SetMyLobbyDisplayName();
        }
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

    public UniTask<List<LobbyData>> GetAvairableLobbyDatas()
    {
        var tcs = new UniTaskCompletionSource<List<LobbyData>>();
        List<LobbyData> lobbyDatas = new();
        _lobbyManager.SearchByAttribute(LobbySceneManager.bKey, LobbySceneManager.bId, OnSearchCompleted);
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

        LobbyData lobbyData = new ()
        {
            id = lobby.Id,
            maxLobbyMembers = lobby.MaxNumLobbyMembers,
            avairableSlots = lobby.AvailableSlots,
            joinAction = () => JoinWithLobbyDetails(lobbyId, details),
        };

        return lobbyData;
    }

    public List<LobbyMemberData> GetCurrentLobbyMemberDatas()
    {
        Lobby current = _lobbyManager.GetCurrentLobby();

        List<LobbyMemberData> memberDatas = new List<LobbyMemberData>();

        foreach(LobbyMember member in current.Members)
        {
            bool isOwner = current.IsOwner(member.ProductId);
            memberDatas.Add(new LobbyMemberData(member.DisplayName, member.ProductId.ToString(), isOwner));
        }

        return memberDatas;
    }
}
