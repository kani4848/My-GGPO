using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class LobbyMemberNamePlate : MonoBehaviour
{
    [SerializeField] Image hat;
    [SerializeField] Image chara;

    [SerializeField] GameObject ownerRabel;
    [SerializeField] TextMeshProUGUI userName;
    [SerializeField] GameObject disconnect;
    [SerializeField] GameObject heartBeat;
    [SerializeField] GameObject ready;

    public TextMeshProUGUI puid;

    private void OnEnable()
    {
        hat.gameObject.SetActive(false);
        chara.gameObject.SetActive(false);
        ownerRabel.gameObject.SetActive(false);
        ready.gameObject.SetActive(false);
        disconnect.gameObject.SetActive(false);
    }

    public void UpdateImage(PlayerData memberData)
    {
        userName.text = memberData.name;
        
        if(memberData.hatCol == Color.black)
        {
            hat.gameObject.SetActive(false);
        }
        else
        {
            hat.gameObject.SetActive(true);
            hat.color = memberData.hatCol;
        }

        if(memberData.charaId == -1)
        {
            chara.gameObject.SetActive(false);
        }
        else
        {
            chara.gameObject.SetActive(true);
            chara.sprite = CharaImageHandler.Instance.GetCharaSpriteById(memberData.charaId);
        }

        ownerRabel.SetActive(false);
        puid.text = memberData.puid;
    }

    public void SetReady(bool _ready)
    {
        ready.SetActive(_ready);
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
