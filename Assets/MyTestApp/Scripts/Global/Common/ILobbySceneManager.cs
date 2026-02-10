
public interface ILobbySceneManager
{
    public LobbyState state { get; set; }
    public void Init(IEosService eosService) { }
}