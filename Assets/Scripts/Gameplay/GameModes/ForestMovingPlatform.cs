using UnityEngine;

namespace EdgeParty.Gameplay.GameModes
{
    [RequireComponent(typeof(Rigidbody))]
    public class ForestMovingPlatform : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float moveDistance = 4.5f;
        public float speed = 1.5f;
        public Vector3 direction = Vector3.up;
        public float startDelayOffset = 0f;

        private Rigidbody _rb;
        private Vector3 _startPos;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _startPos = transform.position;

            // Randomize starting phase offset if not explicitly set
            if (startDelayOffset == 0f)
            {
                startDelayOffset = Random.Range(0f, 5f);
            }
        }

        private void FixedUpdate()
        {
            float time = Time.fixedTime + startDelayOffset;
            float offset = Mathf.PingPong(time * speed, moveDistance);
            Vector3 targetPos = _startPos + direction.normalized * offset;
            _rb.MovePosition(targetPos);
        }
    }
}
