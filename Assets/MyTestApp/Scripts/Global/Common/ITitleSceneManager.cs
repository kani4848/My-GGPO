
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
    public void Init(ICharaImageHandler charaImageHandler);
    public string GetPlayerName();
}
