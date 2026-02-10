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

    Connecting,
    Connected,
    Ready,

    LeavingLobby,

    GoTitle,
    GoMain,

    Error,
}