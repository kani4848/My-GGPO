using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyJoinButton : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI ownerName;
    [SerializeField] Image hat;
    [SerializeField] Image chara;
    [SerializeField] Button btn;
    [SerializeField] TextMeshProUGUI bounty;

    public void SetLobbyDatas(SearchedLobbyData data)
    {
        ownerName.text = data.ownerName;
        hat.color = data.hatCol;
        chara.sprite = CharaImageHandler.Instance.charaSprites[data.charaId];

        bounty.text = "$"+data.rankPoint.ToString("N0");

        btn.onClick.AddListener(() =>
        {
            LobbyEvent.RaiseRequestJoinLobby(data);
        });
    }
}
