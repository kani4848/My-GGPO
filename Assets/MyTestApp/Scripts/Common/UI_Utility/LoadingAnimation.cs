using UnityEngine;
using DG.Tweening;
public class LoadingAnimation : MonoBehaviour
{
    public float rotateVal = 10f;
    public float duration = 0.2f;

    Tween tween;

    void Start()
    {
        tween = DOVirtual.DelayedCall(duration, () =>
        {
            transform.localRotation *= Quaternion.Euler(0, 0, rotateVal);
        }).SetLoops(-1);
    }

    private void OnDisable()
    {
        tween.Kill();
    }
}
