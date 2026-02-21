using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;

using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEditor.PackageManager.Requests;

public sealed class LobbyService_search
{
    uint maxMembers = 2;
    EOSLobbyManager _lobbyManager;    
    Dictionary<Lobby, LobbyDetails> searchedLobbies = new();
    LobbySearchGuard searchGuard  = new();

    public LobbyService_search(EOSLobbyManager lm)
    {
        _lobbyManager = lm;
    }

    public async UniTask<LobbyData> CreateAndJoinAsync(string lobbyPath, PlayerData playerData_local)
    {
        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = EosCommonData.LobbyCommonId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false, // 今回不要
        };

        //全検索用にバケットを付与
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData .LobbyCommonKey,
            ValueType = AttributeType.String,
            AsString = EosCommonData.LobbyCommonId,
            Visibility = LobbyAttributeVisibility.Public,
        });

        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LOBBY_KEY_OWNER_NAME,
            ValueType = AttributeType.String,
            AsString = playerData_local.name,
            Visibility = LobbyAttributeVisibility.Public,
        });

        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LOBBY_KEY_CHARA,
            ValueType = AttributeType.Int64,
            AsInt64 = playerData_local.imageData.charaId,
            Visibility = LobbyAttributeVisibility.Public,
        });

        var hatColData = EOS_Service.PackRgb(playerData_local.imageData.hatCol);

        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LOBBY_KEY_HAT,
            ValueType = AttributeType.Int64,
            AsInt64 = hatColData,
            Visibility = LobbyAttributeVisibility.Public,
        });

        if (lobbyPath != "")
        {
            lobbySettings.Attributes.Add(new LobbyAttribute
            {
                Key = EosCommonData.LOBBY_KEY_PATH,
                ValueType = AttributeType.String,
                AsString = lobbyPath,
                Visibility = LobbyAttributeVisibility.Public,
            });
        }
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
            var modifyTask = new UniTaskCompletionSource<LobbyData>();

            _lobbyManager.ModifyLobby(lobby, modifyResult =>
            {
                if (modifyResult != Result.Success)
                {
                    modifyTask.TrySetResult(null);
                    return;
                }

                var lobbyData = CreateLobbyData(_lobbyManager.GetCurrentLobby());
                EOS_Service.SetMyMemberLobbyAttribute(playerData_local);
                modifyTask.TrySetResult(lobbyData);
            });

            return modifyTask.Task;
        }
    }

    
    public UniTask<LobbyData> Join(string lobbyId, PlayerData playerData_local)
    {
        LobbyDetails details;
        var tcs = new UniTaskCompletionSource<LobbyData>();

        try
        {
            var targetLobbyInfo = searchedLobbies.First(m => m.Key.Id == lobbyId);
            details = targetLobbyInfo.Value;
        }
        catch(NullReferenceException)
        {
            Debug.Log("ロビー参加失敗");
            tcs.TrySetResult(null);
            return tcs.Task;
        }

        _lobbyManager.JoinLobby(lobbyId, details, presenceEnabled: false, result =>
        {
            if (result != Result.Success)
            {
                tcs.TrySetResult(null);
                return;
            }

            var current = _lobbyManager.GetCurrentLobby();
            Debug.Log($"lobbyservice 人数は{current.Members.Count}人");
            EOS_Service.SetMyMemberLobbyAttribute(playerData_local);
            var lobbyData = CreateLobbyData(current);

            tcs.TrySetResult(lobbyData);    
        });

        return tcs.Task;
    }
    public UniTask<List<SearchedLobbyData>> SearchLobby(string lobbyPath = "")
    {
        string key;
        string path;

        if (lobbyPath == "")
        {
            key = EosCommonData.LobbyCommonKey;
            path = EosCommonData.LobbyCommonId;
        }
        else
        {
            key = EosCommonData.LOBBY_KEY_PATH;
            path = lobbyPath;
        }

        var tcs = new UniTaskCompletionSource<List<SearchedLobbyData>>();


        // 追加：この検索を「最新」として登録
        int requestId = searchGuard.BeginRequest();

        Dictionary<Lobby, LobbyDetails> findLobbies = new();
        _lobbyManager.SearchByAttribute(key, path, OnSearchCompleted);

        return tcs.Task;

        void OnSearchCompleted(Result result)
        {
            try
            {
                // 追加：古い検索結果なら捨てる（UIや状態を汚さない）
                if (!searchGuard.IsLatest(requestId)) return;

                if (result != Result.Success)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                searchedLobbies = _lobbyManager.GetSearchResults();

                if (searchedLobbies.Count == 0)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var raw = _lobbyManager.GetSearchResults();
                if (raw == null || raw.Count == 0)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                // 重要：LobbyId で重複排除してから一覧化する
                var uniqueByLobbyId = new Dictionary<string, LobbyDetails>();

                foreach (var kv in raw)
                {
                    var details = kv.Value;
                    if (details == null) continue;

                    // LobbyDetails -> LobbyId 取得
                    var opt = new LobbyDetailsCopyInfoOptions();
                    var r = details.CopyInfo(ref opt, out LobbyDetailsInfo? info);
                    if (r != Result.Success || !info.HasValue) continue;

                    string lobbyId = info.Value.LobbyId;
                    if (string.IsNullOrEmpty(lobbyId)) continue;

                    // 同じ LobbyId がすでにあれば上書きしない（先勝ちでOK）
                    if (!uniqueByLobbyId.ContainsKey(lobbyId))
                        uniqueByLobbyId.Add(lobbyId, details);
                }

                // ついでに class field も「重複なし」に更新したいならここで作り直す
                // searchedLobbies は Lobbyキーなので、ここではUI用だけ作るのが安全
                // searchedLobbies = raw;

                var lobbyDatas = new List<SearchedLobbyData>(capacity: 8);

                foreach (var details in uniqueByLobbyId.Values)
                {
                    lobbyDatas.Add(CreateSearchedLobbyData(details));
                    if (lobbyDatas.Count == 8) break;
                }

                tcs.TrySetResult(lobbyDatas);
            }
            finally
            {
                // 追加：in-flight 終了
                searchGuard.EndRequest();
            }
        }
    }

    LobbyData CreateLobbyData(Lobby lobby)
    {
        string path = "undefined";
        var p = lobby.Attributes.FirstOrDefault(a => a.Key == EosCommonData.LOBBY_KEY_PATH);
        if (p != null) path = p.AsString;

        List<PlayerData> playerDatas = new();

        if(lobby.Members.Count != 0)
        {
            foreach (var member in lobby.Members)
            {
                var pd = CreatePlayerDataFromLobbyMemberData(member);
                playerDatas.Add(pd);
            }
        }

        return new LobbyData(lobby.Id, path, playerDatas);
    }

    public PlayerData CreatePlayerDataFromLobbyMemberData(LobbyMember member)
    {
        string puid = member.ProductId == null ? "" : member.ProductId.ToString();
        string name = member.DisplayName == null ? "" : member.DisplayName;

        int charaId = -1;
        if (member.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_CHARA, out var cid))
            charaId = (int)cid.AsInt64.GetValueOrDefault();

        Color hatCol = default;
        if (member.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_HAT, out var hc))
        {
            var packed = hc.AsInt64.GetValueOrDefault();
            hatCol = EOS_Service.UnpackRgb(packed);
        }

        Color umaCol = default;
        if (member.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_UMA, out var uc))
        {
            var packed = uc.AsInt64.GetValueOrDefault();
            umaCol = EOS_Service.UnpackRgb(packed);
        }

        bool ready = false;
        if (member.MemberAttributes.TryGetValue(EosCommonData.MEMBER_KEY_READY, out var r))
            ready = (bool)r.AsBool;

        return new PlayerData(puid, name, new PlayerImageData(charaId, hatCol, umaCol), ready);
    }

    SearchedLobbyData CreateSearchedLobbyData(LobbyDetails details)
    {
        string lobbyId = "";
        string ownerName = "";
        int charaId = -1;
        Color hatCol = default;

        var opt_info = new LobbyDetailsCopyInfoOptions();
        var r = details.CopyInfo(ref opt_info, out LobbyDetailsInfo? info);
        if (r == Result.Success && info.HasValue)
        {
            lobbyId = info.Value.LobbyId;
        }

        //ロビーアトリビュートに追加したオーナーネームを取得
        var opt_owner = new LobbyDetailsCopyAttributeByKeyOptions
        {
            AttrKey = EosCommonData.LOBBY_KEY_OWNER_NAME,
        };

        r = details.CopyAttributeByKey(ref opt_owner, out var ownerInfo);
        if(r == Result.Success && ownerInfo.HasValue && ownerInfo.Value.Data.HasValue) 
        {
            var d = ownerInfo.Value.Data.Value.Value;
            if (d.ValueType == AttributeType.String) ownerName = d.AsUtf8.ToString();
        }
        else
        {
            Debug.Log("ownerattのデータはありません");
        }

        //同様にキャラを取得
        var opt_chara = new LobbyDetailsCopyAttributeByKeyOptions
        {
            AttrKey = EosCommonData.LOBBY_KEY_CHARA
        };
        r = details.CopyAttributeByKey(ref opt_chara, out var charaInfo);
        if (r == Result.Success && charaInfo.HasValue && charaInfo.Value.Data.HasValue)
        {
            var d = charaInfo.Value.Data.Value.Value;
            if (d.ValueType == AttributeType.Int64) charaId = (int)d.AsInt64.GetValueOrDefault();
        }

        //同様に帽子カラーを取得
        var opt_hat = new LobbyDetailsCopyAttributeByKeyOptions
        {
            AttrKey = EosCommonData.LOBBY_KEY_HAT
        };
        r = details.CopyAttributeByKey(ref opt_hat, out var hatInfo);
        if (r == Result.Success && hatInfo.HasValue && hatInfo.Value.Data.HasValue)
        {
            var d = hatInfo.Value.Data.Value.Value;
            if (d.ValueType == AttributeType.Int64)
            {
                var packed = d.AsInt64.GetValueOrDefault();
                hatCol = EOS_Service.UnpackRgb(packed);
            }
        }

        SearchedLobbyData data = new SearchedLobbyData(lobbyId, ownerName, charaId, hatCol);
        return data;
    }

    //クイックマッチ================================================
    
    const float retryInterval_quick = 0.5f;
    public async UniTask<bool> QuickMatch_FindOpponent(CancellationToken token)
    {
        int loopCount_quick = 0;
        int loopLimit_quick = 10;

        while (!token.IsCancellationRequested)
        {
            var foundLobbies = await SearchLobby_Quick(token);

            if (foundLobbies.Count == 0) continue;

            foreach(var lobby in foundLobbies)
            {
                bool r = await JoinLobby_Quick(lobby, token);
                if (r) return true;
            }

            //失敗
            loopCount_quick++;

            if (loopCount_quick > loopLimit_quick)
            {
                break;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(retryInterval_quick), cancellationToken:token);
        }

        //検索でロビー未発見なら作成
        try
        {
            bool r = await CreateLobby_Quick(token);
            
            //getcurrentlobby一回だと固定値なので、毎フレーム取り直さないといけない
            await UniTask.WaitUntil(() =>
            {
                var lobby = _lobbyManager.GetCurrentLobby();
                return lobby != null && lobby.IsValid() && lobby.Members.Count == 2;
            }, cancellationToken: token);

            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    async UniTask<HashSet<SearchedLobbyData_Quick>> SearchLobby_Quick(CancellationToken token)
    {
        var tcs = new UniTaskCompletionSource<HashSet<SearchedLobbyData_Quick>>();

        try
        {
            Dictionary<Lobby, LobbyDetails> findLobbies = new();

            _lobbyManager.SearchByAttribute(EosCommonData.LobbyQuickKey, EosCommonData.LOBBY_KEY_PATH, OnSearchCompleted);

            void OnSearchCompleted(Result result)
            {
                try
                {
                    if (result != Result.Success)
                    {
                        tcs.TrySetResult(new HashSet<SearchedLobbyData_Quick>(0));
                        return;
                    }

                    var searchedresults = _lobbyManager.GetSearchResults();

                    if (searchedresults.Count == 0)
                    {
                        tcs.TrySetResult(new HashSet<SearchedLobbyData_Quick>(0));
                        return;
                    }

                    var raw = _lobbyManager.GetSearchResults();
                    if (raw == null || raw.Count == 0)
                    {
                        tcs.TrySetResult(new HashSet<SearchedLobbyData_Quick>(0));
                        return;
                    }

                    var searchedLobbies = new HashSet<SearchedLobbyData_Quick>(capacity: 50);

                    foreach (var kv in raw)
                    {
                        var details = kv.Value;
                        if (details == null) continue;

                        // LobbyDetails -> LobbyId 取得
                        var opt = new LobbyDetailsCopyInfoOptions();
                        var r = details.CopyInfo(ref opt, out LobbyDetailsInfo? info);
                        if (r != Result.Success || !info.HasValue) continue;

                        string lobbyId = info.Value.LobbyId;
                        if (string.IsNullOrEmpty(lobbyId)) continue;

                        var lobbydata = new SearchedLobbyData_Quick (lobbyId, details);

                        searchedLobbies.Add(lobbydata);
                    }

                    // ついでに class field も「重複なし」に更新したいならここで作り直す
                    // searchedLobbies は Lobbyキーなので、ここではUI用だけ作るのが安全
                    // searchedLobbies = raw;

                    if (searchedLobbies.Count == 0)
                    {
                        tcs.TrySetResult(new HashSet<SearchedLobbyData_Quick>(0));
                        return;
                    }

                    tcs.TrySetResult(searchedLobbies);
                }
                finally
                {
                }
            }

            return await tcs.Task;

        }
        finally
        {

        }
    }

    async UniTask<bool> CreateLobby_Quick(CancellationToken token)
    {
        int loopCount_quick = 0;
        int loopLimit_quick = 10;

        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = EosCommonData.LobbyCommonId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
        };

        try
        {
            while (!token.IsCancellationRequested)
            {
                var lobbyCreate = new UniTaskCompletionSource<bool>();

                _lobbyManager.CreateLobby(lobbySettings, r =>
                {
                    if (r != Result.Success)
                    {
                        lobbyCreate.TrySetResult(false);
                        return;
                    }

                    Lobby myLobby = _lobbyManager.GetCurrentLobby();
                    lobbyCreate.TrySetResult(true);
                });

                bool r = await lobbyCreate.Task;

                if (!r && loopCount_quick > loopLimit_quick) return false;

                if (!r)
                {
                    Debug.Log("クイックロビー作成失敗");
                    loopCount_quick++;
                    await UniTask.Delay(TimeSpan.FromSeconds(retryInterval_quick), cancellationToken: token);
                    continue;
                }

                return true;
            }

        }
        finally
        {

        }
        return false;
    }

    async UniTask<bool> JoinLobby_Quick(SearchedLobbyData_Quick lobbyData, CancellationToken token)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        _lobbyManager.JoinLobby(lobbyData.lobbyId, lobbyData.details, presenceEnabled: false, result =>
        {
            if (result != Result.Success)
            {
                tcs.TrySetResult(false);
                return;
            }

            tcs.TrySetResult(true);
        });

        return await tcs.Task;
    }
}

public sealed class LobbySearchGuard
{
    // 現在“有効”な検索要求ID（これと一致した結果だけUIに反映する）
    private int _activeRequestId = 0;

    // 現在走っている検索数（任意：デバッグやUIのローディング表示用）
    private int _inFlightCount = 0;

    public int ActiveRequestId => Volatile.Read(ref _activeRequestId);
    public int InFlightCount => Volatile.Read(ref _inFlightCount);

    /// <summary>
    /// 新しい検索要求を開始し、requestId を返す
    /// </summary>
    public int BeginRequest()
    {
        // 新しい要求IDを発行して「これが最新」とする
        int id = Interlocked.Increment(ref _activeRequestId);
        Interlocked.Increment(ref _inFlightCount);
        return id;
    }

    /// <summary>
    /// この requestId の結果を採用してよいか？
    /// </summary>
    public bool IsLatest(int requestId)
    {
        return requestId == Volatile.Read(ref _activeRequestId);
    }

    /// <summary>
    /// 検索終了（finallyで呼ぶ）
    /// </summary>
    public void EndRequest()
    {
        Interlocked.Decrement(ref _inFlightCount);
    }
}
