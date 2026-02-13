using UnityEditor.Rendering;
using UnityEngine;

public class SE_Handler : MonoBehaviour
{
    [SerializeField] AudioSource audio_se;

    [SerializeField] AudioClip walk;
    [SerializeField] AudioClip start;
    [SerializeField] AudioClip signal;
    [SerializeField] AudioClip shot;
    [SerializeField] AudioClip down;
    [SerializeField] AudioClip buttonClick;

    public enum SoundType
    {
        WALK,
        START,
        SIGNAL,
        SHOT,
        DOWN,
        BUTTON,
    }


    public void PlaySE(SoundType type)
    {
        AudioClip se;

        switch (type)
        {
            case SoundType.WALK: se = walk; break;
            case SoundType.START: se = start; break;
            case SoundType.SIGNAL: se = signal; break;
            case SoundType.SHOT: se = shot; break;
            case SoundType.DOWN: se = down; break;
            case SoundType.BUTTON: se = buttonClick; break;
            default:se = down; break;
        }

        audio_se.PlayOneShot(se);
    }
}
