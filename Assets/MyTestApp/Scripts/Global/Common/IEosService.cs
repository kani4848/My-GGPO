using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices.Samples;

public interface IEosService
{
    public EOSManager EOSManager { get; set; }
    public EOSLobbyManager lobbyManager { get; set; }
    public ProductUserId myPuid { get; set; }
    public ProductUserId remotePUID{ get; set;}
    public ProductUserId localPUID { get; set; }
}
