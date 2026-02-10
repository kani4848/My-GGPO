using UnityEngine;

public class SoundManager : Singleton<SoundManager>
{
    [SerializeField] BgmHandler bgmHandler;
    [SerializeField] SE_Handler seHandler;

    public void PlayBgm(BgmHandler.BgmType bgmType)
    {
        bgmHandler.PlayBgm(bgmType);
    }

    public void StopBgm()
    {
        bgmHandler.StopBgm();
    }

    public void PlaySE(SE_Handler.SoundType soundType)
    {
        seHandler.PlaySE(soundType);
    }
}
