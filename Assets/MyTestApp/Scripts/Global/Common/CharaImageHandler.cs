using System.Collections.Generic;
using UnityEngine;

public enum PlayerSide { P1, P2, NONE }

public interface ICharaImageHandler
{
    public void SetCharaImageData();
    public PlayerImageData GetCharaImageData_local();
    public Sprite GetCharaSprite_local();
    public PlayerImageData GetCharaImageData_notLocal();
    public PlayerImageData GetCpuImageDataByLevel(int cpuLv);
}

public class PlayerImageData
{
    public string name = "no name";
    public int charaId;
    public Sprite charaSprite;
    public Color hatCol;
    public Color umaCol;

    public PlayerImageData(int charaId, Sprite charaSprite, Color hatCol, Color umaCol)
    {
        this.charaId = charaId;
        this.charaSprite = charaSprite;
        this.hatCol = hatCol;
        this.umaCol = umaCol;
    }
}

public class CharaImageHandler : MonoBehaviour, ICharaImageHandler
{
    public List<Sprite> charaSprites = new();

    PlayerImageData playerImageData_local;
    PlayerImageData playerImageData_other;

    public void SetCharaImageData()
    {
        int charaId_local = Random.Range(0, 13);
        int charaId_notlocal = Random.Range(0, 13);

        playerImageData_local = new PlayerImageData(
            charaId_local,
            charaSprites[charaId_local],
            new Color(
                Random.value,
                Random.value,
                Random.value),
            new Color(
                Random.value,
                Random.value,
                Random.value)
            );

        playerImageData_other = new PlayerImageData(
            charaId_notlocal,
            charaSprites[charaId_notlocal],
            new Color(
                Random.value,
                Random.value,
                Random.value),
            new Color(
                Random.value,
                Random.value,
                Random.value)
            );
    }

    public Sprite GetCharaSprite_local()
    {
        return playerImageData_local.charaSprite;
    }

    public PlayerImageData GetCharaImageData_local()
    {
        return playerImageData_local;
    }

    public PlayerImageData GetCharaImageData_notLocal()
    {
        return playerImageData_other;
    }

    public PlayerImageData GetCpuImageDataByLevel(int cpuLv)
    {
        return new PlayerImageData(
           cpuLv,
           charaSprites[cpuLv],
           new Color(
               Random.value,
               Random.value,
               Random.value),
           new Color(
               Random.value,
               Random.value,
               Random.value)
           );
    }
}