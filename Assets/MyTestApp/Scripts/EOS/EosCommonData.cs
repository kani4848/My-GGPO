using Epic.OnlineServices;

public static class EosCommonData
{
    public static ProductUserId myPuid { get; set; }

    //メンバー属性キーは必ず大文字
    public static string HB_KEY = "HB";
    public static string HB_STALE_KEY = "STALE";
    public static string MEMBER_KEY_READY = "READY";

    public static string MEMBER_KEY_HAT = "HAT";
    public static string MEMBER_KEY_UMA = "UMA";
    public static string MEMBER_KEY_CHARA = "CHARA";

    //ロビーキー、IDは必ず小文字
    public static string LobbyCommonKey = "bucket";
    public static string LobbyCommonId = "test";

    public static string LOBBY_KEY_PATH = "pass";
    public static string LOBBY_KEY_OWNER_NAME = "owner";
    public static string LOBBY_KEY_CHARA = "chara";
    public static string LOBBY_KEY_HAT = "hat";
}
