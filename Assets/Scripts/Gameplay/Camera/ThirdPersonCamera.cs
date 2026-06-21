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

        [Header("Dynamic Zoom (Feel)")]
        public float runZoomOffset = 0.8f;      // Lùi ra xa khi chạy
        public float inputZoomOffset = -0.3f;   // Tiến lại gần một chút khi mới bấm nút (để có cảm giác phản hồi)
        public float zoomSmoothSpeed = 4f;

        [Header("Orbit Settings")]
        public float sensitivity = 0.2f;
        public float minPitch = -20f;
        public float maxPitch = 60f;

        private float _yaw;
        private float _pitch;
        private Transform _prevTarget;
        private float _baseDistance;
        private float _currentZoomOffset;

        private void Start()
        {
            _baseDistance = distance;
            if (target != null)
            {
                InitializeRotationFromPosition();
                _prevTarget = target;
            }
            else
            {
                // Initialize yaw and pitch based on current rotation
                Vector3 angles = transform.eulerAngles;
                _yaw = angles.y;
                _pitch = angles.x;
                if (_pitch > 180f) _pitch -= 360f;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }
        }

        private void InitializeRotationFromPosition()
        {
            if (target == null) return;
            Vector3 targetPos = target.position + lookAtOffset;
            Vector3 dir = transform.position - targetPos;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(-dir.normalized, Vector3.up);
                Vector3 angles = targetRot.eulerAngles;
                _yaw = angles.y;
                _pitch = angles.x;
                if (_pitch > 180f) _pitch -= 360f;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            // ROOT FIX: If Alt is held, force cursor to be visible and unlocked
            // This prevents any other logic from locking the cursor while we want to use the HUD
            if (Keyboard.current.leftAltKey.isPressed)
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                return; // Skip locking logic below
            }

            // Toggle cursor lock with Escape (delegated to HUDController if present)
            if (Keyboard.current.escapeKey.wasPressedThisFrame && HUDController.Instance == null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                // Check if clicking inside the top-left corner HUD area (approx 320x320)
                // In Unity, mouse position y is 0 at the bottom, but GUI is 0 at the top.
                bool inHUDArea = mousePos.x < 320 && (Screen.height - mousePos.y) < 320;

                // Safety check: EventSystem might be null if not added to the scene
                var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                bool overUI = eventSystem != null && eventSystem.IsPointerOverGameObject();

                // Only lock if NOT clicking on the HUD or any other UI
                if (Cursor.lockState == CursorLockMode.None && !overUI && !inHUDArea)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                _prevTarget = null;
                return;
            }

            if (target != _prevTarget)
            {
                _prevTarget = target;
                InitializeRotationFromPosition();
            }

            // Only rotate if the cursor is locked
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // 1. Read Mouse Input (Input System)
                Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

                float sensX = PlayerPrefs.GetFloat("CameraSensitivityX", 50f);
                float sensY = PlayerPrefs.GetFloat("CameraSensitivityY", 50f);
                bool invertX = PlayerPrefs.GetInt("InvertCameraX", 0) == 1;
                bool invertY = PlayerPrefs.GetInt("InvertCameraY", 0) == 1;

                float sensMultiplierX = sensX / 50f;
                float sensMultiplierY = sensY / 50f;

                _yaw += mouseDelta.x * sensitivity * sensMultiplierX * (invertX ? -1f : 1f);
                _pitch -= mouseDelta.y * sensitivity * sensMultiplierY * (invertY ? -1f : 1f);
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            // 2. Dynamic Zoom Logic
            float targetZoomOffset = 0f;
            
            bool hasInput = false;
            bool isSprinting = false;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.aKey.isPressed || 
                    Keyboard.current.sKey.isPressed || Keyboard.current.dKey.isPressed)
                {
                    hasInput = true;
                }
                if (Keyboard.current.leftShiftKey.isPressed)
                {
                    isSprinting = true;
                }
            }

            if (hasInput && isSprinting)
            {
                targetZoomOffset = runZoomOffset;
            }
            else if (hasInput)
            {
                targetZoomOffset = inputZoomOffset;
            }

            _currentZoomOffset = Mathf.Lerp(_currentZoomOffset, targetZoomOffset, zoomSmoothSpeed * Time.deltaTime);
            float currentDistance = _baseDistance + _currentZoomOffset;

            // 3. Calculate Target Rotation & Position
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 targetPos = target.position + lookAtOffset;
            Vector3 desiredPosition = targetPos - (rotation * Vector3.forward * currentDistance);

            // 4. Apply smooth movement
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
