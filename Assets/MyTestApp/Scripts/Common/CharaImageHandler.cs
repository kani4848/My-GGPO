using System.Collections.Generic;
using UnityEngine;

public enum PlayerSide { P1, P2, NONE }

public interface ICharaImageHandler
{   
    public Sprite GetCharaSpriteById(int id);
}

public class PlayerImageData
{
    public int charaId;
    public Color hatCol;
    public Color umaCol;

    public PlayerImageData(int charaId = -1, Color hatCol = default, Color umaCol = default)
    {
        this.charaId = charaId == -1 ? Random.Range(0, 13) : charaId;
        this.hatCol = hatCol == default? new Color(
               Random.value,
               Random.value,
               Random.value):hatCol;
        this.umaCol = umaCol == default ? new Color(
               Random.value,
               Random.value,
               Random.value) : umaCol;
    }
}

public class CharaImageHandler : Singleton<CharaImageHandler>, ICharaImageHandler
{
    public List<Sprite> charaSprites = new();

    public Sprite GetCharaSpriteById( int id)
    {
        return charaSprites[id];
    }
}
