
using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;

public static class LobbyMemberEvent
{
    public delegate void MemberJoined(PlayerData lobbyMemberData);
    public static event MemberJoined AppliedUserName;
    public static void RaiseAppliedUserName(PlayerData lobbyMemberData) => AppliedUserName?.Invoke(lobbyMemberData);

    public delegate void MemberChanged(PlayerData member);
    public static event MemberChanged MemberJoinedEvent;
    public static event MemberChanged MemberReadyEvent;
    public static event MemberChanged MemberLeftEvent;
    public static event MemberChanged MemberHbStopEvent;
    public static event MemberChanged MemberReviveEvent;
    public static event MemberChanged OwnerChangedEvent;
    public static event MemberChanged HeartBeatEvent;

    public static void RaiseJoined(PlayerData member) => MemberJoinedEvent?.Invoke(member);
    public static void RaiseReady(PlayerData member) => MemberReadyEvent?.Invoke(member);
    public static void RaiseLeft(PlayerData member) => MemberLeftEvent?.Invoke(member);
    public static void RaiseDeath(PlayerData member) => MemberHbStopEvent?.Invoke(member);
    public static void RaiseRevive(PlayerData member) => MemberReviveEvent?.Invoke(member);
    public static void RaiseOwnerChanged(PlayerData member) => OwnerChangedEvent?.Invoke(member);
    public static void RaiseHeartBeat(PlayerData member) => HeartBeatEvent?.Invoke(member);
}
