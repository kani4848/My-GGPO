using TMPro;
using UnityEngine;


public class LobbyMemberDataDisplay : MonoBehaviour
{
    [SerializeField] GameObject ownerRabel;
    [SerializeField] TextMeshProUGUI userName;
    [SerializeField] GameObject disconnect;
    [SerializeField] GameObject heartBeat;
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

    public void HeartBeat()
    {
        Vector3 scale = heartBeat.transform.localScale;
        heartBeat.transform.localScale = new Vector3(scale.x * -1, scale.y, scale.z);
    }
}
