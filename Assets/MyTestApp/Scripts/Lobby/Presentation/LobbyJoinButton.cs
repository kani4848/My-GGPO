using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyJoinButton : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI ownerName;
    [SerializeField] Image hat;
    [SerializeField] Image chara;
    [SerializeField] Button btn;

    public void SetLobbyDatas(SearchedLobbyData data)
    {
        ownerName.text = data.ownerName;
        hat.color = data.hatCol;
        chara.sprite = CharaImageHandler.Instance.charaSprites[data.charaId];

        btn.onClick.AddListener(() =>
        {
            LobbyEvent.RaiseRequestJoinLobby(data);
        });
    }
}
