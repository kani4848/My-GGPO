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
using NUnit.Framework;

public sealed class LobbyService_search
{
    uint maxMembers = 2;
    EOSLobbyManager _lobbyManager;    
    Dictionary<Lobby, LobbyDetails> searchedLobbies = new();

    public LobbyService_search(EOSLobbyManager lm)
    {
        _lobbyManager = lm;
    }

    public async UniTask<LobbyData> CreateAndJoinAsync(string lobbyPath, PlayerData playerData_local)
    {
        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = IEosService.LobbyCommonId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false, // 今回不要
        };

        //全検索用にバケットを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = IEosService.LobbyCommonKey,
            ValueType = AttributeType.String,
            AsString = IEosService.LobbyCommonId,
            Visibility = LobbyAttributeVisibility.Public,
        });

        //カスタム用にアトリビュートを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = IEosService.LobbyCustomKey,
            ValueType = AttributeType.String,
            AsString = lobbyPath,
            Visibility = LobbyAttributeVisibility.Public,
        });

        var myLobby = await CreateLobbyAwait();

        return await ModifyLobbyAwait(myLobby);
        
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

        UniTask<LobbyData> ModifyLobbyAwait(Lobby lobby)
        {
            var lobbyDataTask = new UniTaskCompletionSource<LobbyData>();

            _lobbyManager.ModifyLobby(lobby, modifyResult =>
            {
                if (modifyResult != Result.Success)
                {
                    lobbyDataTask.TrySetResult(null);
                    return;
                }

                var lobbyData = CreateLobbyData(_lobbyManager.GetCurrentLobby());
                EOS_Service.SetMyMemberLobbyAttribute(playerData_local);
                lobbyDataTask.TrySetResult(lobbyData);
            });

            return lobbyDataTask.Task;
        }
    }

    public UniTask<LobbyData> Join(string lobbyId, PlayerData playerData_local)
    {
        Lobby targetLobby;
        LobbyDetails details;

        try
        {
            var targetLobbyInfo = searchedLobbies.First(m => m.Key.Id == lobbyId);
            targetLobby = targetLobbyInfo.Key;
            details = targetLobbyInfo.Value;
        }
        catch(NullReferenceException)
        {
            return UniTask.FromResult<LobbyData>(null);
        }

        var tcs = new UniTaskCompletionSource<LobbyData>(); 

        _lobbyManager.JoinLobby(lobbyId, details, presenceEnabled: false, result =>
        {
            var current = _lobbyManager.GetCurrentLobby();
            bool success = result == Result.Success;
            if (success) EOS_Service.SetMyMemberLobbyAttribute(playerData_local);

            var lobbyData = CreateLobbyData(targetLobby);

            tcs.TrySetResult(lobbyData);    
        });

        return tcs.Task;
    }

    public UniTask<List<LobbyData>> SearchLobby(string lobbyPath = "")
    {
        string key;
        string id;

        if(lobbyPath == "")
        {
            key = IEosService.LobbyCommonKey;
            id = IEosService.LobbyCommonId;
        }
        else
        {
            key = IEosService.LobbyCustomKey;
            id = lobbyPath;
        }

        var tcs = new UniTaskCompletionSource<List<LobbyData>>();
        Dictionary<Lobby, LobbyDetails> findLobbies = new();
        _lobbyManager.SearchByAttribute(key, id, OnSearchCompleted);
        return tcs.Task;

        void OnSearchCompleted(Result result)
        {
            searchedLobbies = _lobbyManager.GetSearchResults();

            // 表示用に並べ替え（例：空きスロット多い順）
            var lobbies = searchedLobbies.Keys
                .Where(l => l != null && l.IsValid())
                .OrderByDescending(l => l.AvailableSlots)
                .ToList();

            List<LobbyData> lobbyDatas = new();

            foreach(var searchedLobby in searchedLobbies)
            {
                var lobbyData = CreateLobbyData(searchedLobby.Key);
                lobbyDatas.Add(lobbyData);
            }

            tcs.TrySetResult(lobbyDatas);
        }
    }

    LobbyData CreateLobbyData(Lobby lobby)
    {
        List<PlayerData> memberDatas = new();

        string path = lobby.Attributes.FirstOrDefault(m => m.Key == IEosService.LobbyCustomKey).AsString;
        path ??= "";

        foreach (var member in lobby.Members)
        {
            var memberData = EOS_Service.CreatePlayerData(member);
            memberDatas.Add(memberData);
        }

        LobbyData data = new LobbyData(lobby.Id, path, 2, memberDatas);

        return data;
    }
}
