using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using EdgeParty.Gameplay.Camera;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Handles reading local player input and transmitting it to the server.
    /// Part of the character refactor.
    /// </summary>
    public class PlayerInputHandler : NetworkBehaviour
    {
        private PlayerController _controller;
        private Transform _camTransform;

        private void Awake()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (_controller == null)
            {
                _controller = transform.root.GetComponentInChildren<PlayerController>();
            }

            var activeThirdPersonCam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (activeThirdPersonCam != null)
            {
                _camTransform = activeThirdPersonCam.transform;
            }
            else if (_camTransform == null && global::UnityEngine.Camera.main != null)
            {
                _camTransform = global::UnityEngine.Camera.main.transform;
            }
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null)
            {
                // Truly offline mode
                ReadAndSendInput(true);
                return;
            }

            // Only proceed if we are either spawned in the network OR we are purposely in an offline state (not listening)
            bool isListening = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
            bool isOffline = !isListening;
            
            if (isListening && !IsSpawned) return; // Prevent RPC errors during initialization

            bool isLocalController = isOffline || IsLocalPlayer;
            if (isLocalController)
            {
                // Self-healing for third person camera target
                var thirdPersonCam = Object.FindFirstObjectByType<ThirdPersonCamera>();
                if (thirdPersonCam != null && thirdPersonCam.target == null)
                {
                    _controller.AssignCameraTarget();
                }

                ReadAndSendInput(isOffline);
            }
        }

        private void ReadAndSendInput(bool isOffline)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null) return;

            // Self-healing for camera: prioritize the active ThirdPersonCamera
            var activeThirdPersonCam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (activeThirdPersonCam != null)
            {
                _camTransform = activeThirdPersonCam.transform;
            }
            else if (_camTransform == null && global::UnityEngine.Camera.main != null)
            {
                _camTransform = global::UnityEngine.Camera.main.transform;
            }

            // Ensure we have a controller
            if (_controller == null)
            {
                FindReferences();
                if (_controller == null) return; 
            }

            // Load keybinds from PlayerPrefs
            string forwardKey = PlayerPrefs.GetString("KeybindForward", "W");
            string backwardKey = PlayerPrefs.GetString("KeybindBackward", "S");
            string leftKey = PlayerPrefs.GetString("KeybindLeft", "A");
            string rightKey = PlayerPrefs.GetString("KeybindRight", "D");
            string jumpKey = PlayerPrefs.GetString("KeybindJump", "Space");

            Vector2 input = Vector2.zero;
            if (IsKeyPressed(forwardKey)) input.y += 1;
            if (IsKeyPressed(backwardKey)) input.y -= 1;
            if (IsKeyPressed(leftKey)) input.x -= 1;
            if (IsKeyPressed(rightKey)) input.x += 1;

            bool isRunning = keyboard.leftShiftKey.isPressed;
            Vector3 worldMoveDir = GetCameraRelativeDirection(input);

            bool isAttackPressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || keyboard.jKey.wasPressedThisFrame;

            Vector3 aimDirection = _camTransform != null ? _camTransform.forward : _controller.transform.forward;

            if (isOffline)
            {
                _controller.OnInputReceived_Server(worldMoveDir, isRunning);
                if (WasKeyPressedThisFrame(jumpKey)) _controller.OnJumpTriggered_Server(worldMoveDir);
                if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) _controller.OnDashTriggered_Server();
                if (isAttackPressed) _controller.OnAttackTriggered_Server(aimDirection);
                if (keyboard.eKey.wasPressedThisFrame) _controller.OnGrabTriggered_Server();
            }
            else
            {
                // Continuous input
                SubmitInputServerRpc(worldMoveDir, isRunning);

                // Individual triggers to ensure no frames are missed
                if (WasKeyPressedThisFrame(jumpKey)) TriggerJumpServerRpc(worldMoveDir);
                
                if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) TriggerDashServerRpc();
                if (isAttackPressed) TriggerAttackServerRpc(aimDirection);
                if (keyboard.eKey.wasPressedThisFrame) TriggerGrabServerRpc();
            }
        }

        private string MapKeyCodeToKeyName(string keyName)
        {
            string mappedName = keyName.Trim();
            if (mappedName.StartsWith("Alpha", System.StringComparison.OrdinalIgnoreCase) && mappedName.Length == 6 && char.IsDigit(mappedName[5]))
            {
                return "Digit" + mappedName[5];
            }
            if (mappedName.Equals("LeftControl", System.StringComparison.OrdinalIgnoreCase)) return "LeftCtrl";
            if (mappedName.Equals("RightControl", System.StringComparison.OrdinalIgnoreCase)) return "RightCtrl";
            if (mappedName.Equals("Space", System.StringComparison.OrdinalIgnoreCase)) return "Space";
            return mappedName;
        }

        private bool IsKeyPressed(string keyName)
        {
            if (Keyboard.current == null) return false;
            
            string mappedName = MapKeyCodeToKeyName(keyName);
            
            if (System.Enum.TryParse(mappedName, true, out Key resultKey))
            {
                return Keyboard.current[resultKey].isPressed;
            }
            return false;
        }

        private bool WasKeyPressedThisFrame(string keyName)
        {
            if (Keyboard.current == null) return false;
            
            string mappedName = MapKeyCodeToKeyName(keyName);

            if (System.Enum.TryParse(mappedName, true, out Key resultKey))
            {
                return Keyboard.current[resultKey].wasPressedThisFrame;
            }
            return false;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (_camTransform == null || input.sqrMagnitude < 0.001f) return Vector3.zero;
            Vector3 camForward = Vector3.ProjectOnPlane(_camTransform.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(_camTransform.right, Vector3.up).normalized;
            return (camForward * input.y + camRight * input.x).normalized;
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SubmitInputServerRpc(Vector3 moveDir, bool isRunning)
        {
            _controller.OnInputReceived_Server(moveDir, isRunning);
        }

        [ServerRpc]
        private void TriggerJumpServerRpc(Vector3 moveDir)
        {
            _controller.OnJumpTriggered_Server(moveDir);
        }

        [ServerRpc]
        private void TriggerDashServerRpc()
        {
            _controller.OnDashTriggered_Server();
        }

        [ServerRpc]
        private void TriggerAttackServerRpc(Vector3 aimDirection)
        {
            _controller.OnAttackTriggered_Server(aimDirection);
        }

        [ServerRpc]
        private void TriggerGrabServerRpc()
        {
            _controller.OnGrabTriggered_Server();
        }
    }
}
