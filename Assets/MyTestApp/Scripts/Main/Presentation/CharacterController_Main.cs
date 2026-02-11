using DG.Tweening;
using UnityEngine;
using static UnityEditor.PlayerSettings;
using UnityEngine.EventSystems;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine.UI;

public class CharacterController_Main : MonoBehaviour
{
    [SerializeField] GameObject alive;
    [SerializeField] GameObject dead;
    [SerializeField] GameObject localMarker;
    [SerializeField] LifeCounter lifeCounter;
    [SerializeField] GameObject cutin;
    [SerializeField] Image hat_alive;
    [SerializeField] Image hat_dead;

    [SerializeField] List<Image> charaImages = new();
    
    PlayerImageData myImageData;

    bool isOwner;
    bool isSoloCpu;

    public void Init(bool _isOwner, PlayerImageData charaImageData, bool soloMode = false)
    {
        isSoloCpu = !_isOwner && soloMode;

        int lifeCount = soloMode ? 5 : 3;
        lifeCounter.SetLifeCounter(lifeCount);

        myImageData = charaImageData;
        hat_alive.color = myImageData.hatCol;
        hat_dead.color = myImageData.hatCol;

        alive.transform.DOLocalMoveX(-walkDistance * 4, 0);
        OnRestart();
        
        isOwner = _isOwner;
        localMarker.SetActive(isOwner);

        DOTween.Sequence()
            .Append(localMarker.transform.DOLocalMoveY(-100, 0.3f)).SetRelative()
            .SetLoops(-1, LoopType.Restart)
            ;


        foreach (var image in charaImages)
        {
            image.sprite = myImageData.charaSprite;
        }
    }

    float walkDistance = 600/4;
    float walkDuration = 1.9f / 8;

    public async UniTask WalkAnimation()
    {
        await DOTween.Sequence()
            .Append(alive.transform.DOLocalMoveX(walkDistance, walkDuration).SetRelative())
            .AppendInterval(walkDuration)

            .Append(alive.transform.DOLocalMoveX(walkDistance, walkDuration).SetRelative())
            .AppendInterval(walkDuration)

            .Append(alive.transform.DOLocalMoveX(walkDistance, walkDuration).SetRelative())
            .AppendInterval(walkDuration)

            .Append(alive.transform.DOLocalMoveX(walkDistance, walkDuration).SetRelative())
            .AppendInterval(walkDuration)

            .AsyncWaitForCompletion()
            ;
    }

    public async UniTask CutInAnimation()
    {
        float slideDistance = 1920;
        float slideTime = 0.2f;

        lifeCounter.gameObject.SetActive(false);
        localMarker.SetActive(false);

        await DOTween.Sequence()
        .Append(cutin.transform.DOMoveX(0, slideTime))
        .AppendInterval(2.5f)
        .Append(cutin.transform.DOMoveX(-960, 0))
        .AsyncWaitForCompletion()
        ;
    }

    public void OnRestart()
    {
        localMarker.SetActive(isOwner);
        alive.SetActive(true);
        dead.SetActive(false);
        lifeCounter.gameObject.SetActive(false);
    }


    public void GoDead()
    {
        alive.SetActive(false);
        dead.SetActive(true);
        lifeCounter.LoseLife();
    }

    public void ShowLife(bool show)
    {
        if (isSoloCpu) return;
        lifeCounter.gameObject.SetActive(show);
    }

    public void OnTimeUp()
    {
        lifeCounter.gameObject.SetActive(true);
        lifeCounter.LoseLife();
    }

    public void StepBack()
    {
        Debug.Log("ステップバック");
        alive.transform.DOLocalMoveX(-walkDistance * 4, 0);
    }

    public PlayerImageData GetCharaImageData()
    {
        return myImageData;
    }

    public void UpdateCharaImage(PlayerImageData charaImageData)
    {
        myImageData = charaImageData;
        hat_alive.color = myImageData.hatCol;
        hat_dead.color = myImageData.hatCol;

        foreach (var image in charaImages)
        {
            image.sprite = myImageData.charaSprite;
        }
    }
}
