
using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;

public static class LobbyMemberEvent
{
    public delegate void MemberJoined(PlayerData lobbyMemberData);
    public static event MemberJoined AppliedUserName;
    public static void RaiseAppliedUserName(PlayerData lobbyMemberData) => AppliedUserName?.Invoke(lobbyMemberData);

    public delegate void MemberChanged(PlayerData member);
    public static event MemberChanged Joined;
    public static event MemberChanged Ready;
    public static event MemberChanged Left;
    public static event MemberChanged Death;
    public static event MemberChanged Revive;
    public static event MemberChanged OwnerChanged;
    public static event MemberChanged HeartBeat;

    public static void RaiseJoined(PlayerData member) => Joined?.Invoke(member);
    public static void RaiseReady(PlayerData member) => Ready?.Invoke(member);
    public static void RaiseLeft(PlayerData member) => Left?.Invoke(member);
    public static void RaiseDeath(PlayerData member) => Death?.Invoke(member);
    public static void RaiseRevive(PlayerData member) => Revive?.Invoke(member);
    public static void RaiseOwnerChanged(PlayerData member) => OwnerChanged?.Invoke(member);
    public static void RaiseHeartBeat(PlayerData member) => HeartBeat?.Invoke(member);
}
