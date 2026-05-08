using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Gameplay.Character
{
    public class PlayerAudioController : NetworkBehaviour
    {
        [Header("Audio Clips")]
        public AudioClip walkClip;
        public AudioClip runClip;
        public AudioClip jumpClip;
        public AudioClip dashClip;

        [Header("State Names (phải khớp tên trong Animator)")]
        public string walkState = "Walk";
        public string runState = "Run";
        public string jumpState = "Jump";
        public string dashState = "Dash";

        // Không cần kéo tay — tự tìm
        private Animator _animator;
        private AudioSource _loopSource;
        private AudioSource _oneShotSource;

        private int _walkHash, _runHash, _jumpHash, _dashHash;
        private int _lastStateHash = 0;
        private bool _ready = false;

        private void Awake()
        {
            _loopSource = gameObject.AddComponent<AudioSource>();
            _loopSource.loop = true;
            _loopSource.playOnAwake = false;
            _loopSource.spatialBlend = 0f;
            _loopSource.volume = 1f;

            _oneShotSource = gameObject.AddComponent<AudioSource>();
            _oneShotSource.loop = false;
            _oneShotSource.playOnAwake = false;
            _oneShotSource.spatialBlend = 0f;
            _oneShotSource.volume = 1f;

            _walkHash = Animator.StringToHash(walkState);
            _runHash = Animator.StringToHash(runState);
            _jumpHash = Animator.StringToHash(jumpState);
            _dashHash = Animator.StringToHash(dashState);
        }

        private void Start()
        {
            // Tìm Animator có RuntimeAnimatorController (tức là có controller thật)
            // trong toàn bộ cây con của object này
            var allAnimators = GetComponentsInChildren<Animator>(includeInactive: true);
            foreach (var anim in allAnimators)
            {
                if (anim.runtimeAnimatorController != null)
                {
                    _animator = anim;
                    break;
                }
            }

            if (_animator == null)
            {
                Debug.LogWarning($"[PlayerAudioController] Không tìm thấy Animator có Controller trên {gameObject.name}. SFX sẽ không hoạt động.");
                return;
            }

            // Gán MixerGroup nếu có
            if (AudioManager.Instance?.sfxSource != null)
            {
                var group = AudioManager.Instance.sfxSource.outputAudioMixerGroup;
                if (group != null)
                {
                    _loopSource.outputAudioMixerGroup = group;
                    _oneShotSource.outputAudioMixerGroup = group;
                }
            }

            _ready = true;
            Debug.Log($"[PlayerAudioController] Tìm thấy Animator: {_animator.gameObject.name}");
        }

        public override void OnNetworkSpawn()
        {
            bool isOffline = NetworkManager.Singleton == null
                          || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer);
            if (!isOffline && !IsOwner)
                enabled = false;
        }

        private void Update()
        {
            if (!_ready) return;

            // Guard: animator phải đang có controller và đang active
            if (!_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null) return;

            var info = _animator.GetCurrentAnimatorStateInfo(0);
            int hash = info.shortNameHash;

            if (hash == _lastStateHash) return;

            OnStateChanged(_lastStateHash, hash);
            _lastStateHash = hash;
        }

        private void OnStateChanged(int fromHash, int toHash)
        {
            bool wasLooping = fromHash == _walkHash || fromHash == _runHash;
            bool willLoop = toHash == _walkHash || toHash == _runHash;

            if (wasLooping && !willLoop)
                _loopSource.Stop();

            if (toHash == _walkHash) SwitchLoop(walkClip);
            else if (toHash == _runHash) SwitchLoop(runClip);
            else if (toHash == _jumpHash) { _loopSource.Stop(); PlayOneShot(jumpClip); }
            else if (toHash == _dashHash) { _loopSource.Stop(); PlayOneShot(dashClip); }
            else _loopSource.Stop();
        }

        private void SwitchLoop(AudioClip clip)
        {
            if (clip == null) return;
            if (_loopSource.clip == clip && _loopSource.isPlaying) return;
            _loopSource.clip = clip;
            _loopSource.Play();
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip == null) return;
            _oneShotSource.PlayOneShot(clip);
        }

        private void OnDestroy()
        {
            _loopSource?.Stop();
        }
    }
}