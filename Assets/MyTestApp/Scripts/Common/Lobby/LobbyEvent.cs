
using Cysharp.Threading.Tasks;

public static class LobbyEvent
{
    public delegate UniTask LobbyJoinAction(string id);
    public static event LobbyJoinAction lobbyJoinEvent;
    public static void RaiseLobbyJoin(string id) => lobbyJoinEvent?.Invoke(id);

    public delegate void LobbyStateChangedAction(LobbyState lobbyState);
    public static event LobbyStateChangedAction lobbyStateChangedEvent;
    public static void RaiseLobbyStateChanged(LobbyState lobbyState) => lobbyStateChangedEvent?.Invoke(lobbyState);
}