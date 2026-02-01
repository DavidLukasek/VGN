using UnityEngine;
using UnityEngine.Audio;

public class VolumeSlider : MonoBehaviour
{
    public AudioMixer mixer;

    public void SetVolume(float value)
    {
        mixer.SetFloat("AudioMixerMasterVolume", Mathf.Log10(value) * 20f);
    }
}
