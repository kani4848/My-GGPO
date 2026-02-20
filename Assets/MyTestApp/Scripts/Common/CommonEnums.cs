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
    JoiningError,
    LeavingLobby,

    InLobby_NoReady,
    InLobby_Ready,
    InLobby_AllReady,

    GoMain,
    GoTitle,
    
    Error,
}
