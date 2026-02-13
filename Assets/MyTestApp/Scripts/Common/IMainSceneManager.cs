using Cysharp.Threading.Tasks;

public enum MainGameState
{
    NONE,
    INITIALIZE,

    ROUND_SETUP,
    ROUND_START,
    MAIN_GAME,

    SHOT_WHITE_OUT,

    RESULT,

    END_MENU,

    GO_TITLE,
    GO_LOBBY,
    QUICK_MATCH,
}


public interface IMainSceneManager
{
    public UniTask StartFlow(IEosService eosService, ICharaImageHandler _charaImageHandler, GameMode mode);

    public MainGameState state { get; set; }
}
