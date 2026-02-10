using UnityEngine;

[CreateAssetMenu(fileName = "BgmData", menuName = "ScriptableObjects/BgmData", order = 1)]
public class BgmData : ScriptableObject
{
    public AudioClip clip;
    public string bgmName;
    public string composerName;
    public string composerSnsURL_X;
    public string downloadURL;
    public BgmHandler.BgmType bgmType;

    private void OnValidate()
    {
        if (clip != null) bgmName = clip.name;
    }
}