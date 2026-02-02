using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;
using PlayEveryWare.EpicOnlineServices;

using Cysharp.Threading.Tasks;
using System.Threading;

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
    public static event MemberChanged Revive;
    public static event MemberChanged OwnerChanged;
    public static event MemberChanged HeartBeat;

    public static void RaiseJoined(LobbyMember member) => Joined?.Invoke(member);
    public static void RaiseLeft(LobbyMember member) => Left?.Invoke(member);
    public static void RaiseDeath(LobbyMember member) => Death?.Invoke(member);
    public static void RaiseRevive(LobbyMember member) => Revive?.Invoke(member);
    public static void RaiseOwnerChanged(LobbyMember member) => OwnerChanged?.Invoke(member);
    public static void RaiseHeartBeat(LobbyMember member) => HeartBeat?.Invoke(member);
}

public sealed class LobbyService : MonoBehaviour
{
    uint maxMembers = 2;
    [SerializeField] EOSLobbyManager _lobbyManager;
    
    // SearchResultsは Dictionary<Lobby, LobbyDetails>
    Dictionary<Lobby, LobbyDetails> _cachedResults = new();
    LobbyService_InLobby inLobbyAction;

    public void Init(EOSLobbyManager lm)
    {
        // 前提：EOSManagerがInitialize済み + Login済み（ProductUserIdが有効）
        _lobbyManager = lm;

        _lobbyManager.LobbyChanged += (_, e) =>
        {
            //Debug.Log($"[ロビー変わりました] type={e.LobbyChangeType} lobbyId={e.LobbyId}");
        };

        inLobbyAction = new LobbyService_InLobby(lm);
    }

    // ---- Host: Create ----
    public async UniTask<LobbyData> CreateAndJoinAsync(string lobbyPath, CancellationToken token)
    {
        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = LobbySceneManager.LobbyCommonId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false, // 今回不要
        };

        //全検索用にバケットを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = LobbySceneManager.LobbyCommonKey,
            ValueType = AttributeType.String,
            AsString = LobbySceneManager.LobbyCommonId,
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
                inLobbyAction.EnterLobbyAction();

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

            inLobbyAction.EnterLobbyAction();

            tcs.TrySetResult(result == Result.Success);    
        });

        return tcs.Task;
    }

    public void SetMyLobbyAttribute()
    {
        //名前
        var name_att = new LobbyAttribute()
        {
            Key = LobbyMember.DisplayNameKey, // "DISPLAYNAME"
            AsString = LobbySceneManager.localUserName,
            ValueType = AttributeType.String,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(name_att);

        //準備
        var ready_att = new LobbyAttribute()
        {
            Key = LobbySceneManager.READY_KEY,
            AsString = "1",
            ValueType = AttributeType.String,
            Visibility = LobbyAttributeVisibility.Public
        };

        _lobbyManager.SetMemberAttribute(ready_att);
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
                inLobbyAction.LeaveLobbyAction();

                tcs.TrySetResult(result);
            });

            return tcs.Task;
        }
    }

    // ---- 外部利用 ----
    public UniTask<List<LobbyData>> GetAvairableLobbyDatas(string lobbyPath = "")
    {
        string key;
        string id;

        if(lobbyPath == "")
        {
            key = LobbySceneManager.LobbyCommonKey;
            id = LobbySceneManager.LobbyCommonId;
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

        LobbyData GetLobbyData(Lobby lobby)
        {
            string lobbyId = lobby.Id;
            LobbyDetails details = _cachedResults[lobby];

            List<ProductUserId> puids = new();

            foreach (LobbyMember member in lobby.Members)
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
    }

    public Lobby GetCurrentLobby()
    {
        return _lobbyManager.GetCurrentLobby();
    }

    public List<LobbyMember> GetCurrentLobbyMember()
    {
        Lobby current = _lobbyManager.GetCurrentLobby();
        List<LobbyMember> puids = current.Members;
        return puids;
    }
}
