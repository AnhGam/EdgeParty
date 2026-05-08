using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Sources")]
    public AudioSource sfxSource;
    public AudioSource bgmSource;

    [Header("Audio Mixer Groups")]
    // Kéo AudioMixerGroup "SFX" và "Music" từ AudioMixer vào đây trong Inspector
    public AudioMixerGroup sfxMixerGroup;
    public AudioMixerGroup bgmMixerGroup;

    private void Awake()
    {
        // Singleton + tồn tại xuyên scene
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Giữ lại khi chuyển scene
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Gán AudioMixerGroup để Volume Slider có tác dụng
        if (sfxSource != null && sfxMixerGroup != null)
            sfxSource.outputAudioMixerGroup = sfxMixerGroup;

        if (bgmSource != null && bgmMixerGroup != null)
            bgmSource.outputAudioMixerGroup = bgmMixerGroup;
    }

    /// <summary>Phát một SFX một lần (không loop)</summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip);
    }

    /// <summary>Phát BGM (loop)</summary>
    public void PlayBGM(AudioClip clip)
    {
        if (clip == null) return;
        if (bgmSource.clip == clip && bgmSource.isPlaying) return; // Tránh restart nếu đang phát

        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    /// <summary>Dừng BGM</summary>
    public void StopBGM()
    {
        bgmSource.Stop();
    }
}