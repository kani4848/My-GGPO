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

    public delegate void MemberChanged(ProductUserId puid);
    public static event MemberChanged Joined;
    public static event MemberChanged Left;
    public static event MemberChanged OwnerChanged;

    public static void RaiseJoined(ProductUserId puid) => Joined?.Invoke(puid);
    public static void RaiseLeft(ProductUserId puid) => Left?.Invoke(puid);
    public static void RaiseOwnerChanged(ProductUserId puid) => OwnerChanged?.Invoke(puid);
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

    HashSet<ProductUserId> _prevMembers = new();

    // Polling (crash / editor stop / network drop 対策)
    [SerializeField] private float memberPollIntervalSec = 2.0f;
    private CancellationTokenSource _memberPollCts;
    private bool _memberSnapshotInitialized = false;


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
        _memberPollCts = new CancellationTokenSource();

        // fire and forget
        MemberPollLoop(_memberPollCts.Token).Forget();

    }

    private void OnDisable()
    {
        _lobbyManager.RemoveNotifyLobbyUpdate(OnLobbyUpdated);
        _lobbyManager.RemoveNotifyMemberUpdate(OnMemberUpdated);

        _memberPollCts?.Cancel();
        _memberPollCts?.Dispose();
        _memberPollCts = null;

        _memberSnapshotInitialized = false;
        _prevMembers.Clear();
    }

    void OnLobbyUpdated()
    {
        currentLobby = _lobbyManager.GetCurrentLobby();
        Debug.Log("ロビーアップデート");
        if (currentLobby == null) return;
        if (LobbySceneManager.myPUID == null) return;

        RefreshMemberDiffAndRaiseEvents(currentLobby);
    }

    //ユーザー名適用のタイミングでJOINイベント発行
    private void OnMemberUpdated(string LobbyId, ProductUserId MemberId)
    {
        string userName = currentLobby.Members.Find(m=>m.ProductId == MemberId).DisplayName;
        LobbyMemberEvent.RaiseAppliedUserName(MemberId, userName);

        var lobby = _lobbyManager.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid()) return;
        if (lobby.Id != currentLobby.Id) return;

        //オーナーチェック
        ProductUserId newOwner = lobby.Members.FirstOrDefault(m => lobby.IsOwner(m.ProductId)).ProductId;

        if (newOwner != null)
        {
            LobbyMemberEvent.RaiseOwnerChanged(newOwner);
        }
    }

    //エラー落ち、エディタ終了などの例外退室時にメンバーを自動チェック
    private async UniTaskVoid MemberPollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var lobby = _lobbyManager?.GetCurrentLobby();

                // 初回スナップショットは “Joined を乱発” しないためにイベントなしで確定
                if (!_memberSnapshotInitialized)
                {
                    _prevMembers = new HashSet<ProductUserId>(lobby.Members.Select(m => m.ProductId));
                    _memberSnapshotInitialized = true;
                    continue;
                }

                if (lobby != null && lobby.IsValid())
                {
                    RefreshMemberDiffAndRaiseEvents(lobby);
                }

                await UniTask.Delay(TimeSpan.FromSeconds(memberPollIntervalSec), cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MemberPollLoop] Exception: {e}");
                // 例外でループが死ぬのが一番まずいので継続
                await UniTask.Delay(TimeSpan.FromSeconds(memberPollIntervalSec), cancellationToken: token);
            }
        }
    }

    private void RefreshMemberDiffAndRaiseEvents(Lobby lobby)
    {
        var current = new HashSet<ProductUserId>(lobby.Members.Select(m => m.ProductId));

        foreach (var joined in current.Except(_prevMembers))
        {
            Debug.Log("join");

            LobbyMemberEvent.RaiseJoined(joined);
            Debug.Log($"[LobbyMember] Join: ({joined})");
        }

        foreach (var removed in _prevMembers.Except(current))
        {
            Debug.Log("leave");

            LobbyMemberEvent.RaiseLeft(removed);
            Debug.Log($"[LobbyMember] Left: ({removed})");
        }


        _prevMembers = current;
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

    public List<ProductUserId> GetCurrentLobbyMemberPUIDs()
    {
        Lobby current = _lobbyManager.GetCurrentLobby();
        List<ProductUserId> puids = current.Members.Select(m=>m.ProductId).ToList();
        return puids;
    }
}
