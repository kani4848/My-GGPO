using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LifeCounter : MonoBehaviour
{
    [SerializeField]Sprite heart_blunk;
    [SerializeField] List<Image> lifes = new();
    int currentLife = 0;

    public void LoseLife()
    {
        if (currentLife >= lifes.Count) return;
        lifes[currentLife].sprite = heart_blunk;
        currentLife++;
    }
}
