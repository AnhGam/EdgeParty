using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace EdgeParty.Core
{
    /// <summary>
    /// Singleton AudioManager – gọi từ bất kỳ đâu qua AudioManager.Instance.
    ///
    /// CÁCH DÙNG:
    ///   AudioManager.Instance.PlaySFX("Click");
    ///   AudioManager.Instance.PlaySFX("Hover");
    ///   AudioManager.Instance.StopMusic();
    ///   AudioManager.Instance.SetMusicVolume(0.5f);
    ///   AudioManager.Instance.SetSFXVolume(0.8f);
    ///
    /// SETUP (Unity Editor):
    ///   1. Tạo empty GameObject tên "AudioManager" trong scene đầu tiên (ví dụ: MainMenu).
    ///   2. Gắn script AudioManager vào GameObject đó.
    ///   3. Trong Inspector, thêm các Sound vào danh sách "Sounds".
    ///      Mỗi Sound có: name, clip, volume (0–1), pitch (0.5–1.5), isMusic.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────
        public static AudioManager Instance { get; private set; }

        // ─── Data ─────────────────────────────────────────────────────

        [Serializable]
        public class Sound
        {
            [Tooltip("Tên dùng để gọi âm thanh (ví dụ: \"Click\", \"Punch\")")]
            public string name;

            public AudioClip clip;

            [Range(0f, 1f)]
            public float volume = 1f;

            [Range(0.5f, 1.5f)]
            public float pitch = 1f;

            [Tooltip("True = nhạc nền (loop). False = hiệu ứng âm thanh (SFX)")]
            public bool isMusic = false;
        }

        [Header("Sound Library")]
        [Tooltip("Kéo tất cả AudioClip vào đây và đặt tên để gọi bằng PlaySFX/PlayMusic")]
        [SerializeField] private Sound[] sounds;

        [SerializeField] private AudioMixerGroup sfxMixerGroup; // (Tùy chọn) Mixer để quản lý nhóm âm thanh, nếu cần thiết.
        [SerializeField] private AudioMixerGroup musicMixerGroup; // (Tùy chọn) Mixer để quản lý nhóm nhạc nền, nếu cần thiết.

        // ─── Audio Sources ────────────────────────────────────────────

        [Header("Volume")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume    = 1f;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private List<AudioSource> _sfxSourcePool;
        private int _nextPoolIndex = 0;
        private const int PoolSize = 6;

        // Lookup nhanh bằng Dictionary (O(1))
        private Dictionary<string, Sound> _soundMap;

        // ─── Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            // Singleton – chỉ giữ một instance duy nhất
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Tạo AudioSource cho Music
            _musicSource              = gameObject.AddComponent<AudioSource>();
            _musicSource.loop         = true;
            _musicSource.volume       = musicVolume;
            _musicSource.playOnAwake  = false;
            _musicSource.outputAudioMixerGroup = musicMixerGroup; // (Tùy chọn) Gán vào mixer nếu có

            // Tạo pool AudioSource cho SFX
            _sfxSourcePool = new List<AudioSource>();
            for (int i = 0; i < PoolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.loop = false;
                source.volume = sfxVolume;
                source.outputAudioMixerGroup = sfxMixerGroup;
                source.playOnAwake = false;
                _sfxSourcePool.Add(source);
            }
            
            if (_sfxSourcePool.Count > 0)
            {
                _sfxSource = _sfxSourcePool[0];
            }

            // Build Dictionary
            _soundMap = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
            if (sounds != null)
            {
                foreach (var s in sounds)
                {
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    if (!_soundMap.ContainsKey(s.name))
                        _soundMap[s.name] = s;
                    else
                        Debug.LogWarning($"[AudioManager] Tên âm thanh bị trùng: \"{s.name}\". Chỉ giữ lại âm thanh đầu tiên.");
                }
            }
        }

        // ─── Public API ───────────────────────────────────────────────

        /// <summary>Phát một hiệu ứng âm thanh (SFX) theo tên.</summary>
        public void PlaySFX(string soundName)
        {
            if (!TryGetSound(soundName, out var s)) return;
            if (_sfxSourcePool == null || _sfxSourcePool.Count == 0) return;

            var source = _sfxSourcePool[_nextPoolIndex];
            _nextPoolIndex = (_nextPoolIndex + 1) % _sfxSourcePool.Count;

            source.pitch  = s.pitch;
            source.volume = s.volume * sfxVolume;
            source.PlayOneShot(s.clip);
        }

        /// <summary>Phát nhạc nền (tự động dừng bài cũ nếu đang chạy).</summary>
        public void PlayMusic(string soundName)
        {
            if (!TryGetSound(soundName, out var s)) return;

            // Tránh restart nhạc nếu đang phát cùng bài
            if (_musicSource.clip == s.clip && _musicSource.isPlaying) return;

            _musicSource.clip   = s.clip;
            _musicSource.pitch  = s.pitch;
            _musicSource.volume = s.volume * musicVolume;
            _musicSource.Play();
        }

        /// <summary>Dừng nhạc nền.</summary>
        public void StopMusic() => _musicSource.Stop();

        /// <summary>Dừng tất cả SFX đang phát.</summary>
        public void StopAllSFX()
        {
            if (_sfxSourcePool != null)
            {
                foreach (var source in _sfxSourcePool)
                {
                    if (source != null) source.Stop();
                }
            }
        }

        /// <summary>Cập nhật âm lượng nhạc nền (0–1).</summary>
        public void SetMusicVolume(float volume)
        {
            musicVolume          = Mathf.Clamp01(volume);
            _musicSource.volume  = musicVolume;
        }

        /// <summary>Cập nhật âm lượng SFX (0–1).</summary>
        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            if (_sfxSourcePool != null)
            {
                foreach (var source in _sfxSourcePool)
                {
                    if (source != null) source.volume = sfxVolume;
                }
            }
        }

        /// <summary>Kiểm tra xem nhạc nền có đang phát không.</summary>
        public bool IsMusicPlaying => _musicSource.isPlaying;

        // ─── Internals ────────────────────────────────────────────────

        private bool TryGetSound(string soundName, out Sound sound)
        {
            if (_soundMap.TryGetValue(soundName, out sound)) return true;
            Debug.LogWarning($"[AudioManager] Không tìm thấy âm thanh: \"{soundName}\". Kiểm tra lại tên trong Inspector.");
            return false;
        }

        // Sync Inspector sliders tại Runtime (Edit Mode thay đổi volume thấy ngay)
        private void OnValidate()
        {
            if (_musicSource != null) _musicSource.volume = musicVolume;
            if (_sfxSourcePool != null)
            {
                foreach (var source in _sfxSourcePool)
                {
                    if (source != null) source.volume = sfxVolume;
                }
            }
        }
    }
}
