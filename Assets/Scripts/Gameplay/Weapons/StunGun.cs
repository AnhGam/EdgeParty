using UnityEngine;

namespace EdgeParty.Gameplay.Items
{
    public class StunGun : MonoBehaviour
    {
        [Header("Stun Settings")]
        public float stunDuration = 2.5f;      // Giây bị choáng
        public float stunRange = 2.5f;           // Tầm bắn / tầm đánh

        [Header("VFX / SFX")]
        [Tooltip("Particle System tia điện — để trống để dùng built-in")]
        public GameObject electricVFXPrefab;
        [Tooltip("Kéo electricShock_sfx.wav vào đây")]
        public AudioClip stunHitSFX;
        [Tooltip("(Optional) Tiếng điện rè loop trong khi choáng")]
        public AudioClip stunLoopSFX;

        private void Awake()
        {
            // Auto-load SFX nếu không gán trong Inspector
            if (stunHitSFX == null)
                stunHitSFX = Resources.Load<AudioClip>("Audios/electricShock_sfx");
        }
    }
}