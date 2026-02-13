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
    SearchingLobby,
    CreateLobbyAndJoin,

    Joining,

    InLobby,
    LeavingLobby,

    Ready,
    ConnectingOpponent,
    GoMain,

    GoTitle,
    
    Error,
}
