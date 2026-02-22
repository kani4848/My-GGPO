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

    //ロビーのキーは必ず小文字
    
    //バケットID…マッチンググループの区別、のはずなんだがそれは何十万という規模のお話。マッチング識別検索はアトリビュートでやる。
    public static string MyBacketId = "backet";
    
    //ロビーアトリビュートキー
    public static string LobbyAttributeKey_PATH = "pass";
    public static string LobbyAttributeKey_NAME = "owner";
    public static string LobbyAttributeKey_CHARA = "chara";
    public static string LobbyAttributeKey_HAT = "hat";
    public static string LobbyAttributeKey_MatchingType = "matching";

    //マッチング識別用アトリビュートバリュー（キーはmatching type）
    public static string LobbyAttributeValue_Common = "commmoooon";
    public static string LobbyAttributeValue_Password = "password";
    public static string LobbyAttributeValue_QuickMatch = "quickquickquick";
}
