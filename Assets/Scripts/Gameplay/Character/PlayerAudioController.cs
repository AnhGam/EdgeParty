using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Lấy ghostAnimator trực tiếp từ PlayerController trên cùng GameObject.
    /// Không cần kéo gì vào Inspector ngoài các AudioClip.
    /// </summary>
    public class PlayerAudioController : NetworkBehaviour
    {
        [Header("Audio Clips")]
        public AudioClip walkClip;
        public AudioClip runClip;
        public AudioClip jumpClip;
        public AudioClip dashClip;

        private PlayerController _playerController;
        private Animator _animator;

        private AudioSource _loopSource;
        private AudioSource _oneShotSource;

        private int _walkHash, _runHash, _jumpHash, _dashHash;
        private int _lastStateHash = 0;
        private bool _ready = false;

        private void Start()
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

            StartCoroutine(InitWhenReady());
        }

        public override void OnNetworkSpawn()
        {
            bool isOffline = NetworkManager.Singleton == null
                          || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer);
            if (!isOffline && !IsOwner)
                enabled = false;
        }

        private IEnumerator InitWhenReady()
        {
            float timeout = 5f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                _playerController = GetComponent<PlayerController>();

                if (_playerController != null && _playerController.ghostAnimator != null && _playerController.animController != null)
                {
                    _animator = _playerController.ghostAnimator;

                    // Đọc state names từ CharacterAnimationController
                    var animCtrl = _playerController.animController;
                    _walkHash = Animator.StringToHash(animCtrl.walkState);
                    _runHash = Animator.StringToHash(animCtrl.runState);
                    _jumpHash = Animator.StringToHash(animCtrl.jumpState);
                    _dashHash = Animator.StringToHash(animCtrl.dashState);

                    AssignMixerGroup();
                    _ready = true;
                    Debug.Log($"[PlayerAudioController] OK - Animator: '{_animator.gameObject.name}'");
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.LogError("[PlayerAudioController] FAIL - ghostAnimator trong PlayerController vẫn null sau 5s. " +
                           "Hãy kéo Ghost object vào field ghostAnimator của PlayerController trong prefab.", this);
        }

        private void AssignMixerGroup()
        {
            if (AudioManager.Instance == null) return;
            if (AudioManager.Instance.sfxSource == null) return;
            var group = AudioManager.Instance.sfxSource.outputAudioMixerGroup;
            if (group == null) return;
            _loopSource.outputAudioMixerGroup = group;
            _oneShotSource.outputAudioMixerGroup = group;
        }

        private void Update()
        {
            if (!_ready || _animator == null) return;
            if (_animator.runtimeAnimatorController == null) return;

            int hash;
            try { hash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash; }
            catch { return; }

            if (hash == 0 || hash == _lastStateHash) return;

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

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_loopSource != null) _loopSource.Stop();
        }
    }
}