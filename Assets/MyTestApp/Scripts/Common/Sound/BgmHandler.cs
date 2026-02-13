using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BgmHandler : MonoBehaviour
{
    [SerializeField] AudioSource audioSource;

    public enum BgmType
    {
        Title,
        Main,
    }

    [SerializeField] List<BgmData> bgmDatas;
    BgmData currentBgmData;

    public void PlayBgm(BgmType bgmType)
    {
        if (currentBgmData?.bgmType == bgmType) return;

        StopBgm();

        var bgmData = bgmDatas.FirstOrDefault(b => b.bgmType == bgmType);
        if (bgmData == null)
        {
            Debug.Log($"{bgmType}‚ÉŠY“–‚·‚éBGM‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ");
            return;
        }

        currentBgmData = bgmData;

        audioSource.generator = bgmData.clip;
        audioSource.Play();
    }

    public void StopBgm()
    {
        audioSource.Stop();
    }

    public BgmData GetCurretBgmData()
    {
        if(currentBgmData == null) return null; 
        return currentBgmData;
    }
}
