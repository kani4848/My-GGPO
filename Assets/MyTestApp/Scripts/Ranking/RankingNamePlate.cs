using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RankingNamePlate : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI rank;
    [SerializeField] TextMeshProUGUI playerName;
    [SerializeField] TextMeshProUGUI point;

    [SerializeField] Image baseImage;
    [SerializeField] Color hilightCol;

    public void SetData(RankRow data)
    {
        rank.text = data.Rank.ToOrdinal();
        playerName.text = data.playerName;
        point.text = "$" + data.Score.ToString("N0");
    }

    public void Hilight()
    {
        baseImage.color = hilightCol;
    }
}

public static class RankSuffixUtility
{
    /// <summary>
    /// 数値に英語順位サフィックスを付ける
    /// </summary>
    public static string ToOrdinal(this int number)
    {
        int abs = Mathf.Abs(number);

        int lastTwoDigits = abs % 100;

        // 11,12,13 は例外で th
        if (lastTwoDigits >= 11 && lastTwoDigits <= 13)
        {
            return number + "th";
        }

        switch (abs % 10)
        {
            case 1: return number + "st";
            case 2: return number + "nd";
            case 3: return number + "rd";
            default: return number + "th";
        }
    }
}
