using UnityEngine;
using UnityEngine.InputSystem;

namespace EdgeParty.Gameplay.Camera
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Follow Settings")]
        public Transform target;
        public Vector3 lookAtOffset = new Vector3(0f, 1f, 0f);
        public float distance = 5f;
        public float smoothSpeed = 10f;

        [Header("Orbit Settings")]
        public float sensitivity = 0.2f;
        public float minPitch = -20f;
        public float maxPitch = 60f;

        private float _yaw;
        private float _pitch;

        private void Start()
        {
            // Lock and hide cursor for better experience
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Initialize yaw and pitch based on current rotation
            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // 1. Read Mouse Input (Input System)
            Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

            _yaw += mouseDelta.x * sensitivity;
            _pitch -= mouseDelta.y * sensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            // 2. Calculate Target Rotation & Position
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetPos = target.position + lookAtOffset;
            Vector3 desiredPosition = targetPos - (rotation * Vector3.forward * distance);

            // 3. Apply smooth movement
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
            transform.LookAt(targetPos);
        }

        // Context menu to lock/unlock cursor if needed for debugging
        [ContextMenu("Lock Cursor")]
        public void LockCursor() => Cursor.lockState = CursorLockMode.Locked;
        
        [ContextMenu("Unlock Cursor")]
        public void UnlockCursor() => Cursor.lockState = CursorLockMode.None;
    }
}
