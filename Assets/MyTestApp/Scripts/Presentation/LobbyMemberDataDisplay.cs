using TMPro;
using UnityEngine;


public class LobbyMemberDataDisplay : MonoBehaviour
{
    [SerializeField] GameObject ownerRabel;
    [SerializeField] TextMeshProUGUI userName;
    [SerializeField] GameObject disconnect;
    public TextMeshProUGUI puid;

    public void SetInfo(string _puid)
    {
        ownerRabel.SetActive(false);
        puid.text = _puid;
    }

    public void SetUserName(string _userName)
    {
        userName.text = _userName;
    }

    public void SetOwner(bool active)
    {
        ownerRabel.SetActive(active);
    }

    public void SetDisconnect(bool active)
    {
        disconnect.SetActive(active);
    }
}
