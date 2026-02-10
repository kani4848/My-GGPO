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

public sealed class LobbyService_search
{
    uint maxMembers = 2;
    EOSLobbyManager _lobbyManager;
    
    Dictionary<Lobby, LobbyDetails> searchResults = new();

    public LobbyService_search(EOSLobbyManager lm)
    {
        _lobbyManager = lm;
    }

    // ---- Host: Create ----
    public async UniTask CreateAndJoinAsync(string lobbyPath)
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

        //カスタム用にアトリビュートを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = LobbySceneManager.customKey,
            ValueType = AttributeType.String,
            AsString = lobbyPath,
            Visibility = LobbyAttributeVisibility.Public,
        });

        var myLobby = await CreateLobbyAwait();
        
        await ModifyLobbyAwait(myLobby);
        
        SetMyMemberLobbyAttribute();

        UniTask<Lobby> CreateLobbyAwait()
        {
            var tcs = new UniTaskCompletionSource<Lobby>();

            _lobbyManager.CreateLobby(lobbySettings, r =>
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
                Debug.Log("ロビー修正完了");
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
            bool success = result == Result.Success;
            if (success) SetMyMemberLobbyAttribute();

            tcs.TrySetResult(success);    
        });

        return tcs.Task;

       
    }

    void SetMyMemberLobbyAttribute()
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
    public UniTask<bool> LeaveAsync()
    {
        var tcs = new UniTaskCompletionSource<bool>();

        _lobbyManager.LeaveLobby(result =>
        {
            tcs.TrySetResult(result == Result.Success);
        });

        return tcs.Task;
    }

    // ---- 外部利用 ----
    public UniTask<Dictionary<Lobby,LobbyDetails>> GetAvairableLobbyDatas(string lobbyPath = "")
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

        var tcs = new UniTaskCompletionSource<Dictionary<Lobby, LobbyDetails>>();
        Dictionary<Lobby, LobbyDetails> findLobbies = new();
        _lobbyManager.SearchByAttribute(key, id, OnSearchCompleted);
        return tcs.Task;

        void OnSearchCompleted(Result result)
        {
            searchResults = _lobbyManager.GetSearchResults();

            // 表示用に並べ替え（例：空きスロット多い順）
            var lobbies = searchResults.Keys
                .Where(l => l != null && l.IsValid())
                .OrderByDescending(l => l.AvailableSlots)
                .ToList();

            if (lobbies.Count == 0)
            {
                Debug.Log("No lobbies found.");
            }

            tcs.TrySetResult(searchResults);
        }
    }
}
