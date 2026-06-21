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
                // Sử dụng vị trí để tạo seed cố định, giúp mọi Client đều random ra cùng 1 số
                Random.InitState(Mathf.RoundToInt(transform.position.x * 100f + transform.position.y * 10f + transform.position.z));
                startDelayOffset = Random.Range(0f, 5f);
            }
        }

        private void FixedUpdate()
        {
            double time = Time.fixedTime;
            // Đồng bộ thời gian chuyển động của nền tảng với ServerTime
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            {
                time = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time;
            }

            float timeF = (float)time + startDelayOffset;
            float offset = Mathf.PingPong(timeF * speed, moveDistance);
            Vector3 targetPos = _startPos + direction.normalized * offset;
            _rb.MovePosition(targetPos);
        }
    }
}
