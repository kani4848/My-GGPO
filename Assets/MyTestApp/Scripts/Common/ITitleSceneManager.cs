
public enum TitleState
{
    None,
    GoOnline,
    GoLocal,
    GoSolo,
}

public interface ITitleSceneManager
{
    public TitleState state { get; set; }
    public void Init(PlayerData PlayerData);
    public string GetPlayerName();
}
