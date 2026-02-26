
public static class LobbyMemberEvent
{
    public delegate void MemberJoined(PlayerData lobbyMemberData);
    public static event MemberJoined UpdatePlayerData;
    public static void RaiseUpdatePlayerData(PlayerData lobbyMemberData) => UpdatePlayerData?.Invoke(lobbyMemberData);

    public delegate void MemberChanged(PlayerData member);
    public static event MemberChanged MemberJoinedEvent;
    public static event MemberChanged MemberHbStopEvent;
    public static event MemberChanged MemberReviveEvent;
    public static event MemberChanged OwnerChangedEvent;
    public static event MemberChanged HeartBeatEvent;

    public static void RaiseJoined(PlayerData member) => MemberJoinedEvent?.Invoke(member);
    public static void RaiseRevive(PlayerData member) => MemberReviveEvent?.Invoke(member);
    public static void RaiseOwnerChanged(PlayerData member) => OwnerChangedEvent?.Invoke(member);
    public static void RaiseHeartBeat(PlayerData member) => HeartBeatEvent?.Invoke(member);


    public delegate void OnMemberLeft(string puid);
    public static event OnMemberLeft MemberLeftEvent;
    public static event OnMemberLeft MemberSleepEvent;
    public static void RaiseLeft(string puid) => MemberLeftEvent?.Invoke(puid);
    public static void RaiseDeath(string puid) => MemberSleepEvent?.Invoke(puid);
}
