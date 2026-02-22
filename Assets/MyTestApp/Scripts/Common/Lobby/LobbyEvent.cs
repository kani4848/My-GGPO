
using Cysharp.Threading.Tasks;

public static class LobbyEvent
{
    public delegate UniTask LobbyJoinAction(SearchedLobbyData data);
    public static event LobbyJoinAction RequestJoinLobbyEvent;
    public static void RaiseRequestJoinLobby(SearchedLobbyData data) => RequestJoinLobbyEvent?.Invoke(data);

    public delegate void LobbyStateChangedAction(LobbyState lobbyState);
    public static event LobbyStateChangedAction lobbyStateChangedEvent;
    public static void RaiseLobbyStateChanged(LobbyState lobbyState) => lobbyStateChangedEvent?.Invoke(lobbyState);
}
