using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
#if UNITY_2023_1_OR_NEWER
                _instance = FindFirstObjectByType<AudioManager>();
#else
                _instance = FindObjectOfType<AudioManager>();
#endif
                if (_instance == null)
                {
                    GameObject go = new GameObject("GlobalAudioManagerProxy");
                    _instance = go.AddComponent<AudioManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    private static AudioManager _instance;

    [Header("Audio Mixer Groups (Proxy to Core)")]
    public AudioMixerGroup sfxMixerGroup;
    public AudioMixerGroup bgmMixerGroup;

    public AudioSource sfxSource
    {
        get => EdgeParty.Core.AudioManager.Instance != null ? EdgeParty.Core.AudioManager.Instance.sfxSourceExposed : null;
    }

    public AudioSource bgmSource
    {
        get => EdgeParty.Core.AudioManager.Instance != null ? EdgeParty.Core.AudioManager.Instance.bgmSourceExposed : null;
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>Phát một SFX một lần (không loop)</summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        EdgeParty.Core.AudioManager.Instance?.PlaySFX(clip);
    }

    /// <summary>Phát BGM (loop)</summary>
    public void PlayBGM(AudioClip clip)
    {
        if (clip == null) return;
        EdgeParty.Core.AudioManager.Instance?.PlayMusic(clip);
    }

    /// <summary>Dừng BGM</summary>
    public void StopBGM()
    {
        EdgeParty.Core.AudioManager.Instance?.StopMusic();
    }
}