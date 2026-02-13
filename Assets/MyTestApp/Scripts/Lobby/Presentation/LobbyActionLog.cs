using TMPro;
using UnityEngine;

public enum LobbyLogType
{
    JOIN,
    LEAVE,
    DISCONNECT,
    REVIVE,
    OWNER_CHANGED,
}


public class LobbyActionLog : MonoBehaviour
{
    public string id;
    string userName;
    LobbyLogType type;
    [SerializeField]TextMeshProUGUI text;

    public void UpdateData(string _id, string _username, LobbyLogType _type)
    {
        id = _id;
        userName = _username;
        type = _type;

        UpdateText();
    }

    public void UpdateNameText(string newName)
    {
        userName = newName;
        UpdateText ();
    }

    void UpdateText()
    {
        text.text = $"{userName} {GetActionText(type)}";
    }

    string GetActionText(LobbyLogType type)
    {
        switch (type)
        {
            case LobbyLogType.JOIN:
                return "joined the lobby.";;  
            case LobbyLogType.LEAVE:
                return "leave the lobby.";
            case LobbyLogType.REVIVE:
                return "is reconnected.";
            case LobbyLogType.DISCONNECT:
                return "is disconnected.";
            case LobbyLogType.OWNER_CHANGED:
                return "becomes new lobby owner.";
            default:
                return "";
        }
    }
}
