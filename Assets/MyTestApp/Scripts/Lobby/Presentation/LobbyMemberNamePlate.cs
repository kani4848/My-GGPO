using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class LobbyMemberNamePlate : MonoBehaviour
{
    [SerializeField] Image hat;
    [SerializeField] Image chara;

    [SerializeField] GameObject ownerRabel;
    [SerializeField] TextMeshProUGUI userName;
    [SerializeField] TextMeshProUGUI bounty;
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
        
        if(memberData.imageData.hatCol == default)
        {
            hat.gameObject.SetActive(false);
        }
        else
        {
            hat.gameObject.SetActive(true);
            hat.color = memberData.imageData.hatCol;
        }

        if(memberData.imageData.charaId == -1)
        {
            chara.gameObject.SetActive(false);
        }
        else
        {
            chara.gameObject.SetActive(true);
            chara.sprite = CharaImageHandler.Instance.GetCharaSpriteById(memberData.imageData.charaId);
        }

        bounty.text = "$" + memberData.rankPoint.ToString("N0");

        ownerRabel.SetActive(memberData.isOwner);
        puid.text = memberData.puid;

        ready.SetActive(memberData.ready);
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
