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

    SearchingLobby,

    InLobbySearchRoom,
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
