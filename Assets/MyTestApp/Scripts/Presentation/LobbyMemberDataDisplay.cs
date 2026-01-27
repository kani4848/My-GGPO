using TMPro;
using UnityEngine;


public class LobbyMemberDataDisplay : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI userName;
    [SerializeField] TextMeshProUGUI puid;

    public void SetInfo(string _userName, string _puid)
    {
        userName.text = _userName;
        puid.text = _puid;
    }
}
