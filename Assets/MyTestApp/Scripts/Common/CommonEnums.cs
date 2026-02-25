public enum GameMode
{
    None,
    Online,
    Solo,
    Local,
}


public enum LobbyState
{
    None,

    InLobbySearchRoom,
    
    JoiningLobby,
    JoiningSuccess,
    JoiningError,

    QuickMatch,

    InLobby_NoReady,
    InLobby_Ready,
    InLobby_AllReady,
    LeavingLobby,

    GoMain,
    GoRanking,
    GoTitle,
    
    Error,
}

//メインシーン
public enum MatchResult
{
    NONE = 0,
    WIN_P1 = 1,
    WIN_P2 = 2,
    DRAW = 3,
}
