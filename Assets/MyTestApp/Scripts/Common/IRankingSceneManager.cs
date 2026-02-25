using Cysharp.Threading.Tasks;

public enum RankingSceneState
{
    Global,
    MyRank,
    GoLobby,
}

public interface IRankingSceneManager
{
    public RankingSceneState State { get; set; }

    public UniTask Init(IEosService eosService);
}
