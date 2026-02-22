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
using PlayEveryWare.EpicOnlineServices;

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

    public async UniTask<LobbyData> CreateAndJoinAsync(string lobbyPath, PlayerData playerData_local, bool quick = false)
    {
        var lobbySettings = new Lobby
        {
            MaxNumLobbyMembers = maxMembers,
            BucketId = EosCommonData.MyBacketId,
            LobbyPermissionLevel = LobbyPermissionLevel.Publicadvertised, // テスト向け
            PresenceEnabled = false,
            AllowInvites = false,
            RTCRoomEnabled = false, // 今回不要
        };

        //マッチング識別用アトリビュート
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData .LobbyAttributeKey_MatchingType,
            ValueType = AttributeType.String,
            AsString = quick ? EosCommonData.LobbyAttributeValue_QuickMatch : EosCommonData.LobbyAttributeValue_Common,
            Visibility = LobbyAttributeVisibility.Public,
        });

        //名前アトリビュート
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LobbyAttributeKey_NAME,
            ValueType = AttributeType.String,
            AsString = playerData_local.name,
            Visibility = LobbyAttributeVisibility.Public,
        });


        //キャラアトリビュート
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LobbyAttributeKey_CHARA,
            ValueType = AttributeType.Int64,
            AsInt64 = playerData_local.imageData.charaId,
            Visibility = LobbyAttributeVisibility.Public,
        });


        //帽子アトリビュート
        lobbySettings.Attributes.Add(new LobbyAttribute
        {
            Key = EosCommonData.LobbyAttributeKey_HAT,
            ValueType = AttributeType.Int64,
            AsInt64 = EOS_Service.PackRgb(playerData_local.imageData.hatCol),
            Visibility = LobbyAttributeVisibility.Public,
        });

        //パスワードアトリビュート
        if (lobbyPath != "")
        {
            lobbySettings.Attributes.Add(new LobbyAttribute
            {
                Key = EosCommonData.LobbyAttributeKey_PATH,
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

    string disposedLobbyId;

    public async UniTask<List<SearchedLobbyData>> SearchLobby_Common(string lobbyPath = "")
    {
        string attKey;
        string attValue;

        if (lobbyPath == "")//パスワードなしで検索
        {
            attKey = EosCommonData.LobbyAttributeKey_MatchingType;
            attValue = EosCommonData.LobbyAttributeValue_Common;
        }
        else//パスワードありで検索
        {
            attKey = EosCommonData.LobbyAttributeKey_PATH;
            attValue = lobbyPath;
        }

        return await SearchLobby(attKey, attValue);
    }

    async UniTask<List<SearchedLobbyData>> SearchLobby(string attKey, string attValue)
    {
        var tcs = new UniTaskCompletionSource<List<SearchedLobbyData>>();

        // 追加：この検索を「最新」として登録
        int requestId = searchGuard.BeginRequest();

        Dictionary<Lobby, LobbyDetails> findLobbies = new();
        _lobbyManager.SearchByAttribute(attKey, attValue, OnSearchCompleted);

        return await tcs.Task;

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
                    if (uniqueByLobbyId.ContainsKey(lobbyId)) continue;

                    if (lobbyId == disposedLobbyId) continue;

                    //既に破棄され削除待ちのロビーを除外
                    if (EOS_Service.IsDisposedId(lobbyId)) continue;

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
        var p = lobby.Attributes.FirstOrDefault(a => a.Key == EosCommonData.LobbyAttributeKey_PATH);
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
            AttrKey = EosCommonData.LobbyAttributeKey_NAME,
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
            AttrKey = EosCommonData.LobbyAttributeKey_CHARA
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
            AttrKey = EosCommonData.LobbyAttributeKey_HAT
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
    
    const float retryInterval_quick = 0.2f;
    public async UniTask<bool> QuickMatch_FindOpponent(CancellationToken token)
    {
        int loopCount = 0;
        const int loopLimit = 3;

        try
        {
            while (!token.IsCancellationRequested && loopCount < loopLimit)
            {
                Debug.Log($"クイックマッチサーチ試行{loopCount}回目");
                var foundLobbies = await SearchLobby(
                    EosCommonData.LobbyAttributeKey_MatchingType,
                    EosCommonData.LobbyAttributeValue_QuickMatch);

                if (foundLobbies.Count == 0)
                {
                    loopCount++;
                    await UniTask.Delay(TimeSpan.FromSeconds(retryInterval_quick), cancellationToken: token);
                    continue;
                }

                foreach (var lobby in foundLobbies)
                {
                    bool joinResult = await JoinLobby_Quick(lobby.lobbyId);
                    if (joinResult)
                    {
                        Debug.Log($"クイックマッチ:ロビー発見、参加成功");
                        return true;
                    }
                }

                //失敗
                loopCount++;

                await UniTask.Delay(TimeSpan.FromSeconds(retryInterval_quick), cancellationToken: token);
            }

        }
        catch (OperationCanceledException)
        {
            Debug.Log($"ロビー検索中にクイックマッチがキャンセルされました");
            return false;
        }
        return false;
    }

    async UniTask<bool> JoinLobby_Quick(string lobbyId)
    {
        LobbyDetails details;
        var tcs = new UniTaskCompletionSource<bool>();

        try
        {
            var targetLobbyInfo = searchedLobbies.First(m => m.Key.Id == lobbyId);
            details = targetLobbyInfo.Value;

            _lobbyManager.JoinLobby(lobbyId, details, presenceEnabled: false, result =>
            {
                tcs.TrySetResult(result == Result.Success);
            });
        }
        catch (NullReferenceException)
        {
            Debug.Log("ロビー参加失敗");
            tcs.TrySetResult(false);
        }

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
