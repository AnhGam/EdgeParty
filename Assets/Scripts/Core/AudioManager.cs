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
                        GameObject go = new GameObject("CoreAudioManager");
                        _instance = go.AddComponent<AudioManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }
        private static AudioManager _instance;

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

        // ─── BGM Per-Scene Config ─────────────────────────────────────

        [System.Serializable]
        public class SceneBGM
        {
            [Tooltip("Tên scene (case-insensitive, dùng Contains)")]
            public string sceneNameContains;
            [Tooltip("Tên clip trong soundMap để phát khi vào scene này")]
            public string bgmClipName;
        }

        [Header("BGM theo Scene")]
        [Tooltip("Mỗi entry: scene nào thì phát BGM nào. Để trống để auto-detect.")]
        [SerializeField] private SceneBGM[] sceneBGMs;

        // Tên clip mặc định cho MainMenu và in-game (auto-detect từ Resources/Audios)
        private const string MainMenuBGMHint = "Colossal"; // matches "Aylex - Colossal (freetouse.com)"
        private const string InGameBGMHint   = "goofy";   // matches "goofy ahh bgm"

        // Scene keywords nào thì được coi là in-game (không phải menu)
        private static readonly string[] InGameSceneKeywords = { "Sample", "Game", "Map", "Forest", "Playground", "Level" };

        // ─── Audio Sources ────────────────────────────────────────────

        [Header("Volume")]
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.1f;
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
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureAudioListener();

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

            // Dynamically load all clips from Resources/Audios/
            var loadedClips = Resources.LoadAll<AudioClip>("Audios");
            if (loadedClips != null)
            {
                foreach (var clip in loadedClips)
                {
                    if (clip == null) continue;
                    string clipName = clip.name;
                    if (!_soundMap.ContainsKey(clipName))
                    {
                        var s = new Sound
                        {
                            name = clipName,
                            clip = clip,
                            volume = 1f,
                            pitch = 1f,
                            isMusic = clipName.Contains("bgm") || clipName.Contains("Colossal")
                        };
                        _soundMap[clipName] = s;
                    }
                }
            }

        }

        private void Start()
        {
            // Phát một bài BGM duy nhất suốt cả game, không đổi theo scene
            PlaySingleBGM();
        }

        /// <summary>Phát BGM duy nhất (goofy ahh bgm). Nếu đang phát rồi thì không restart.</summary>
        private void PlaySingleBGM()
        {
            if (_soundMap == null || _musicSource == null) return;
            if (_musicSource.isPlaying) return; // Đã có nhạc rồi, không làm gì

            // Tìm clip theo hint "goofy"
            AudioClip target = null;
            foreach (var kv in _soundMap)
            {
                if (kv.Value.clip != null &&
                    kv.Key.IndexOf(InGameBGMHint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = kv.Value.clip;
                    break;
                }
            }

            // Fallback: bất kỳ clip isMusic nào
            if (target == null)
            {
                foreach (var kv in _soundMap)
                {
                    if (kv.Value.isMusic && kv.Value.clip != null)
                    {
                        target = kv.Value.clip;
                        break;
                    }
                }
            }

            if (target != null)
            {
                _musicSource.clip   = target;
                _musicSource.pitch  = 1f;
                _musicSource.volume = musicVolume;
                _musicSource.Play();
                Debug.Log($"[AudioManager] BGM bắt đầu: \"{target.name}\"");
            }
            else
            {
                Debug.LogWarning("[AudioManager] Không tìm thấy clip BGM nào trong Resources/Audios.");
            }
        }

        // ─── Public API ───────────────────────────────────────────────

        public void PlaySFX(string soundName)
        {
            if (!TryGetSound(soundName, out var s)) return;
            if (_sfxSourcePool == null || _sfxSourcePool.Count == 0) return;

            var source = _sfxSourcePool[_nextPoolIndex];
            _nextPoolIndex = (_nextPoolIndex + 1) % _sfxSourcePool.Count;

            source.pitch  = s.pitch;

            float volumeScale = 1f;
            if (soundName.IndexOf("Coin_pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.15f;
            }
            else if (soundName.IndexOf("Item_pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.3f;
            }
            else if (soundName.IndexOf("electricShock_sfx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.07f;
            }
            else if (soundName.IndexOf("EXPLOSION_sfx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.08f;
            }
            else if (soundName.IndexOf("Hover", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.33f;
            }

            source.volume = s.volume * sfxVolume;
            source.PlayOneShot(s.clip, volumeScale);
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

        public AudioSource sfxSourceExposed
        {
            get
            {
                if (_sfxSourcePool != null && _sfxSourcePool.Count > 0)
                {
                    return _sfxSourcePool[0];
                }
                return null;
            }
        }

        public AudioSource bgmSourceExposed => _musicSource;

        /// <summary>Phát một hiệu ứng âm thanh (SFX) theo AudioClip.</summary>
        public void PlaySFX(AudioClip clip)
        {
            if (clip == null) return;
            if (_sfxSourcePool == null || _sfxSourcePool.Count == 0) return;

            var source = _sfxSourcePool[_nextPoolIndex];
            _nextPoolIndex = (_nextPoolIndex + 1) % _sfxSourcePool.Count;

            source.pitch  = 1f;

            float volumeScale = 1f;
            string clipName = clip.name;
            if (clipName.IndexOf("Coin_pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.15f;
            }
            else if (clipName.IndexOf("Item_pickup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.3f;
            }
            else if (clipName.IndexOf("electricShock_sfx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.07f;
            }
            else if (clipName.IndexOf("EXPLOSION_sfx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.08f;
            }
            else if (clipName.IndexOf("Hover", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                volumeScale = 0.33f;
            }

            source.volume = sfxVolume;
            source.PlayOneShot(clip, volumeScale);
        }

        /// <summary>Phát nhạc nền theo AudioClip.</summary>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) return;

            // Tránh restart nhạc nếu đang phát cùng bài
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;

            _musicSource.clip   = clip;
            _musicSource.pitch  = 1f;
            _musicSource.volume = musicVolume;
            _musicSource.Play();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Always unpause audio when a new scene loads
            AudioListener.pause = false;
            StartCoroutine(EnsureAudioListenerDelayed());
            // Không switch BGM — một bài duy nhất suốt game
        }

        /// <summary>Chọn và phát BGM phù hợp với tên scene.</summary>
        private void PlayBGMForScene(string sceneName)
        {
            if (_soundMap == null || _musicSource == null) return;

            // 1) Ưu tiên config thủ công trong sceneBGMs
            if (sceneBGMs != null)
            {
                foreach (var entry in sceneBGMs)
                {
                    if (!string.IsNullOrEmpty(entry.sceneNameContains) &&
                        sceneName.IndexOf(entry.sceneNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_soundMap.TryGetValue(entry.bgmClipName, out var s))
                        {
                            PlayBGMClipDirect(s.clip);
                            Debug.Log($"[AudioManager] BGM → \"{s.clip.name}\" (scene: {sceneName})");
                            return;
                        }
                    }
                }
            }

            // 2) Auto-detect dùng danh sách InGameSceneKeywords
            //    Nếu tên scene chứa bất kỳ keyword nào → in-game (goofy ahh bgm)
            //    Còn lại → menu (Colossal)
            bool isInGame = false;
            foreach (var keyword in InGameSceneKeywords)
            {
                if (sceneName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isInGame = true;
                    break;
                }
            }

            string hint = isInGame ? InGameBGMHint : MainMenuBGMHint;
            Debug.Log($"[AudioManager] Scene \"{sceneName}\" → {(isInGame ? "InGame" : "Menu")} BGM (hint: {hint})");

            AudioClip bestClip = null;
            foreach (var kv in _soundMap)
            {
                if (kv.Value.clip != null &&
                    kv.Key.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bestClip = kv.Value.clip;
                    break;
                }
            }

            // Fallback: không tìm được theo hint → lấy clip isMusic đầu tiên
            if (bestClip == null)
            {
                foreach (var kv in _soundMap)
                {
                    if (kv.Value.isMusic && kv.Value.clip != null)
                    {
                        bestClip = kv.Value.clip;
                        break;
                    }
                }
            }

            if (bestClip != null)
            {
                PlayBGMClipDirect(bestClip);
                Debug.Log($"[AudioManager] BGM → \"{bestClip.name}\" (scene: {sceneName})");
            }
            else
            {
                Debug.LogWarning($"[AudioManager] Không tìm thấy clip BGM nào cho scene: {sceneName}");
            }
        }

        /// <summary>Phát clip nhạc nền trực tiếp, skip nếu đang phát cùng bài.</summary>
        private void PlayBGMClipDirect(AudioClip clip)
        {
            if (clip == null || _musicSource == null) return;
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;
            _musicSource.clip   = clip;
            _musicSource.pitch  = 1f;
            _musicSource.volume = musicVolume;
            _musicSource.Play();
        }

        private System.Collections.IEnumerator EnsureAudioListenerDelayed()
        {
            yield return null;
            EnsureAudioListener();
        }

        public void EnsureAudioListener()
        {
#if UNITY_2023_1_OR_NEWER
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
            AudioListener[] listeners = FindObjectsOfType<AudioListener>();
#endif

            // Strategy: CoreAudioManager ALWAYS keeps its own AudioListener active.
            // Any other AudioListeners found on OTHER objects get disabled to avoid the
            // "multiple AudioListeners" warning. This prevents the bug where our listener
            // gets disabled when entering a game scene and never re-enabled on return.
            AudioListener ourListener = GetComponent<AudioListener>();

            // Ensure we have our own listener
            if (ourListener == null)
            {
                Debug.Log("[AudioManager] Creating AudioListener on AudioManager.");
                ourListener = gameObject.AddComponent<AudioListener>();
            }
            ourListener.enabled = true;

            // Disable any OTHER listeners (e.g., on cameras)
            foreach (var l in listeners)
            {
                if (l != ourListener && l.gameObject != gameObject && l.enabled)
                {
                    Debug.Log($"[AudioManager] Disabling extra AudioListener on: {l.gameObject.name}");
                    l.enabled = false;
                }
            }
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
