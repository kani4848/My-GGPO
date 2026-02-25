using Codice.Client.BaseCommands.Filters;
using Cysharp.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Leaderboards;
using Epic.OnlineServices.Stats;
using PlayEveryWare.EpicOnlineServices;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using System;


public class StatService
{
    const int startRankPoint = 1000;
    const int baseChangePoint = 10;   // 基本変動
    const float coefMax = 2f;         // 格下が勝った時の最大倍率
    const float coefMin = 0.1f;       // 格上が勝った時の最小倍率
    const int noChangeGap = 50;       // 同格とみなす差
    const int maxGap = 500;           // 最大補正がかかる差

    const int decorateScale = 100;

    const string statName_rankPoint = "RankPoint";
    const string statName_NameCodec_Hi = "NameCodec_Hi";
    const string statName_NameCodec_Lo = "NameCodec_Lo";
    
    StatsInterface statInterface;


    readonly LeaderboardsInterface _leaderboards;
    const string boardId = "Ranking_RankPoint";
    const int rankerCount = 10;//取得する人数

    public StatService()
    {
        _leaderboards = EOSManager.Instance.GetEOSPlatformInterface().GetLeaderboardsInterface();
        statInterface = EOSManager.Instance.GetEOSStatsInterface();
    }

    enum QueryResult
    {
        Success,
        Error,
        NotFound,
    }

    public async UniTask<int> GetRankPoint(ProductUserId puid)
    {
        int rawPoint;

        (QueryResult result, int val) = await QueryRankPoint(puid);

        switch (result)
        {
            case QueryResult.Success:
                rawPoint = val; 
                break;

            case QueryResult.Error:
            case QueryResult.NotFound:
                Debug.Log("ランクポイントを初期化します");
                bool r = await IngestRankPoint(startRankPoint);
                rawPoint = r ? startRankPoint : -1;
                break;

            default:
                rawPoint = 0;
                break;
        }

        return Mathf.RoundToInt(rawPoint * decorateScale);
    }

    //EOSに保存されている値を取得
    async UniTask<(QueryResult result, int val)> QueryRankPoint(ProductUserId puid)
    {
        var tcs = new UniTaskCompletionSource<(QueryResult result, int val)>();

        var opt = new QueryStatsOptions
        {
            LocalUserId = EosCommonData.myPuid,
            TargetUserId = puid,
            StatNames = new Utf8String[] { statName_rankPoint },
            StartTime = null,
            EndTime = null,
        };

        statInterface.QueryStats(ref opt, null, (ref OnQueryStatsCompleteCallbackInfo info) =>
        {
            //クエリ成功ならコピーで取り出す
            var cpyOpt = new CopyStatByNameOptions
            {
                TargetUserId = puid,
                Name = statName_rankPoint,
            };

            var copyResult = statInterface.CopyStatByName(ref cpyOpt, out var stat);

            if (copyResult == Result.Success)
            {
                Stat _stat = (Stat)stat;
                tcs.TrySetResult((QueryResult.Success, (int)_stat.Value));
                return;
            }

            if (info.ResultCode == Result.NotFound)
            {
                //スタッツ呼び出し初回
                tcs.TrySetResult((QueryResult.NotFound, 0));
                return;
            }

            Debug.Log("スタッツ取得に失敗");
                tcs.TrySetResult((QueryResult.NotFound, 0));
            return;
        });

        return await tcs.Task;
    }

    //値を編集してEOSに反映
    public async UniTask<bool> IngestRankPoint(int delta)
    {
        var tcs = new UniTaskCompletionSource<bool>();

        var ingest = new IngestData
        {
            StatName = statName_rankPoint,
            IngestAmount = delta,
        };

        var opt = new IngestStatOptions
        {
            LocalUserId = EosCommonData.myPuid,
            TargetUserId = EosCommonData.myPuid,
            Stats = new IngestData[] { ingest },
        };

        statInterface.IngestStat(ref opt, null, (ref IngestStatCompleteCallbackInfo info) =>
        {
            if(info.ResultCode != Result.Success)
            {
                Debug.Log("スタッツの編集に失敗");
                tcs.TrySetResult(false);
                return;
            }

            tcs.TrySetResult(true);
        });

        return await tcs.Task;
    }

    async UniTask<(QueryResult result, string val)> QueryPlayerName(ProductUserId puid)
    {
        var tcs = new UniTaskCompletionSource<(QueryResult result, string val)>();

        var opt = new QueryStatsOptions
        {
            LocalUserId = EosCommonData.myPuid,
            TargetUserId = puid,
            StatNames = new Utf8String[] { statName_NameCodec_Lo, statName_NameCodec_Hi},
            StartTime = null,
            EndTime = null
        };

        statInterface.QueryStats(ref opt, null, (ref OnQueryStatsCompleteCallbackInfo info) =>
        {
            //１つめ
            int codec_lo;

            var cpyOpt_lo = new CopyStatByNameOptions
            {
                TargetUserId = puid,
                Name = statName_NameCodec_Lo,
            };

            var copyResult_lo = statInterface.CopyStatByName(ref cpyOpt_lo, out var stat_lo);

            switch (copyResult_lo)
            {
                case Result.Success:
                    Stat _stat = (Stat)stat_lo;
                    codec_lo = _stat.Value;
                    break;

                case Result.NotFound:
                    Debug.Log("lowが見つかりません");
                    //スタッツ呼び出し初回
                    tcs.TrySetResult((QueryResult.NotFound, ""));
                    return;
   
                default:
                    Debug.Log("lowの取得失敗");
                    tcs.TrySetResult((QueryResult.Error, ""));
                    return;

            }

            //2つめ
            int codec_hi;

            var cpyOpt_hi = new CopyStatByNameOptions
            {
                TargetUserId = puid,
                Name = statName_NameCodec_Hi,
            };

            var copyResult_hi = statInterface.CopyStatByName(ref cpyOpt_hi, out var stat_hi);

            switch (copyResult_hi)
            {
                case Result.Success:
                    Stat _stat = (Stat)stat_hi;
                    codec_hi = _stat.Value;
                    break;

                case Result.NotFound:
                    //スタッツ呼び出し初回
                    tcs.TrySetResult((QueryResult.NotFound, ""));
                    return;

                default:
                    tcs.TrySetResult((QueryResult.Error, ""));
                    return;
            }

            var decode = DisplayNameCodec40.DecodeFromTwoInts(codec_lo, codec_hi);
            tcs.TrySetResult((QueryResult.Success, decode));
            return;
        });

        return await tcs.Task;
    }

    public async UniTask<bool> IngestPlayerName(string name)
    {
        var codec = DisplayNameCodec40.EncodeToTwoInts(name);

        var tcs = new UniTaskCompletionSource<bool>();

        var ingest_lo = new IngestData
        {
            StatName = statName_NameCodec_Lo,
            IngestAmount = codec.lo,
        };

        var ingest_hi = new IngestData
        {
            StatName = statName_NameCodec_Hi,
            IngestAmount = codec.hi,
        };

        var opt = new IngestStatOptions
        {
            LocalUserId = EosCommonData.myPuid,
            TargetUserId = EosCommonData.myPuid,
            Stats = new IngestData[] { ingest_lo, ingest_hi },
        };

        statInterface.IngestStat(ref opt, null, (ref IngestStatCompleteCallbackInfo info) =>
        {
            if (info.ResultCode != Result.Success)
            {
                Debug.Log("名前スタッツの編集に失敗");
                tcs.TrySetResult(false);
                return;
            }

            tcs.TrySetResult(true);
        });

        return await tcs.Task;
    }



    /// <summary>
    /// 勝者の獲得ポイントを返す（常に正の値）
    /// 敗者には -delta を適用すればゼロサム
    /// </summary>
    public static int GetWinnerDelta(int winnerPoint, int loserPoint)
    {
        if (loserPoint == 0) return 0;

        int gap = winnerPoint - loserPoint;   // +なら勝者が格上
        int clampedGap = Mathf.Clamp(gap, -maxGap, maxGap);
        int gapAbs = Mathf.Abs(clampedGap);

        // 同格帯は係数1
        if (gapAbs <= noChangeGap)
        {
            return baseChangePoint;
        }

        // 0〜1に正規化
        float t = gapAbs / (float)maxGap;

        float coef;

        if (gap > 0)
        {
            // 勝者が格上 → 増えにくい
            coef = Mathf.Lerp(1f, coefMin, t);
        }
        else
        {
            // 勝者が格下 → 増えやすい
            coef = Mathf.Lerp(1f, coefMax, t);
        }

        int changePointCoef = Mathf.RoundToInt(baseChangePoint * coef);

        //獲得できるのは敗者の持っているポイント分のみ
        if(loserPoint < changePointCoef)　return loserPoint;

        return changePointCoef;
    }


    /// <summary>
    /// LeaderboardId の上位 N 件を取得する
    /// </summary>
    public async UniTask<List<RankRow>> QueryTopRanksAsync(
        CancellationToken token = default)
    {
        var tcs = new UniTaskCompletionSource<List<RankRow>>();

        var opt = new QueryLeaderboardRanksOptions
        {
            LocalUserId = EosCommonData.myPuid,
            LeaderboardId = boardId,
            // ApiVersion は C# ラッパーだと自動で最新が入る構成が多い（手動指定が必要な版もある）
        };

        List<RankRow> rows = new();

        _leaderboards.QueryLeaderboardRanks(ref opt, null, (ref OnQueryLeaderboardRanksCompleteCallbackInfo info) =>
        {
            try
            {
                if (token.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(token);
                    return;
                }

                if (info.ResultCode != Result.Success)
                {
                    Debug.Log($"リーダボード取得に失敗");
                    tcs.TrySetException(new Exception($"QueryLeaderboardRanks failed: {info.ResultCode}"));
                    return;
                }

                // 1) 取得件数
                var countOpt = new GetLeaderboardRecordCountOptions();

                uint count = _leaderboards.GetLeaderboardRecordCount(ref countOpt);

                // 2) Copy で取り出す
                rows = new List<RankRow>((int)Math.Min(count, (uint)rankerCount));
                uint take = Math.Min(count, (uint)rankerCount);

                Debug.Log($"{take}件のスタッツが見つかりました");

                for (uint i = 0; i < take; i++)
                {
                    var copyOpt = new CopyLeaderboardRecordByIndexOptions
                    {
                        LeaderboardRecordIndex = i,
                    };

                    LeaderboardRecord? record;
                    var r = _leaderboards.CopyLeaderboardRecordByIndex(ref copyOpt, out record);
                    if (r != Result.Success || record == null)continue;

                    // record.Rank は 1 始まり
                    rows.Add(new RankRow
                    {
                        Rank = (int)record.Value.Rank,
                        Score = record.Value.Score,
                        UserId = record.Value.UserId,
                    });
                }

                tcs.TrySetResult(rows);
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        });

        await tcs.Task;

        for(int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var codec = await QueryPlayerName(row.UserId);
            string userName = codec.result == QueryResult.Success ? codec.val : "---";
            Debug.Log(userName);
            row.playerName = userName;
        }

        return rows;
    }
}

