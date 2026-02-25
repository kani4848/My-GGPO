using TMPro;
using UnityEngine;

public class RankingNamePlate : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI rank;
    [SerializeField] TextMeshProUGUI playerName;
    [SerializeField] TextMeshProUGUI point;

    public void SetData(RankRow data)
    {
        rank.text = data.Rank.ToString();
        playerName.text = data.playerName;
        point.text = "$" + data.Score.ToString("N0");
    }
}
