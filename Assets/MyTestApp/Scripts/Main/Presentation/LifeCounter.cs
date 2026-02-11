using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LifeCounter : MonoBehaviour
{
    [SerializeField] GameObject lifePrefab;
    [SerializeField] Sprite heart_ful;
    [SerializeField]Sprite heart_blunk;
    [SerializeField] List<Image> lifes = new();
    int currentLife = 0;



    public void SetLifeCounter(int num)
    {
        for(int i = 0; i < num; i++)
        {
            lifes.Add(Instantiate(lifePrefab, transform).GetComponent<Image>());
        }
    }

    public void LoseLife()
    {
        if (currentLife >= lifes.Count) return;
        lifes[currentLife].sprite = heart_blunk;
        currentLife++;
    }

    public void ResetLife()
    {
        currentLife = 0;
        foreach(Image heart in lifes)
        {
            heart.sprite = heart_ful;
        }
    }
}
