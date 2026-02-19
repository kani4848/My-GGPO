
using Cysharp.Threading.Tasks;

public interface ILobbySceneManager
{
    public LobbyState state { get; set; }
    public UniTask<LobbyState> StartFlow(IEosService eosService);
}
