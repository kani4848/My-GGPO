using UnityEngine;
using DG.Tweening;
public class LoadingAnimation : MonoBehaviour
{
    public float rotateVal = 10f;
    public float duration = 0.2f;

    void Start()
    {
        DOVirtual.DelayedCall(duration, () =>
        {
            transform.localRotation *= Quaternion.Euler(0, 0, rotateVal);
        }).SetLoops(-1);
    }
}
