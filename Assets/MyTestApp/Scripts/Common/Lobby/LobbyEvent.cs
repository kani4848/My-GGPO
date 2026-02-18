
using Cysharp.Threading.Tasks;

public static class LobbyEvent
{
    public delegate UniTask LobbyJoinAction(string id);
    public static event LobbyJoinAction RequestJoinLobbyEvent;
    public static void RaiseRequestJoinLobby(string id) => RequestJoinLobbyEvent?.Invoke(id);

    public delegate void LobbyStateChangedAction(LobbyState lobbyState);
    public static event LobbyStateChangedAction lobbyStateChangedEvent;
    public static void RaiseLobbyStateChanged(LobbyState lobbyState) => lobbyStateChangedEvent?.Invoke(lobbyState);
}
