using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class ColorFadeService : Singleton<ColorFadeService>
{
    [SerializeField] Image fillColor;
    [SerializeField] float fadeTime = 0.3f;
    public async UniTask BlackFadeIn()
    {
        await fillColor.DOFade(1, fadeTime);
        BlackFadeOut();
    }

    void BlackFadeOut()
    {
        fillColor.DOFade(0,fadeTime/2);
    }
}
