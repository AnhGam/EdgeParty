using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

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

            if (_camTransform == null && global::UnityEngine.Camera.main != null)
                _camTransform = global::UnityEngine.Camera.main.transform;
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

            bool isLocalController = isOffline || IsOwner;
            if (isLocalController)
            {
                ReadAndSendInput(isOffline);
            }
        }

        private void ReadAndSendInput(bool isOffline)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null) return;

            // Self-healing for camera if it was missing during Awake
            if (_camTransform == null && global::UnityEngine.Camera.main != null)
                _camTransform = global::UnityEngine.Camera.main.transform;

            // Ensure we have a controller
            if (_controller == null)
            {
                FindReferences();
                if (_controller == null) return; 
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1;
            if (keyboard.sKey.isPressed) input.y -= 1;
            if (keyboard.aKey.isPressed) input.x -= 1;
            if (keyboard.dKey.isPressed) input.x += 1;

            bool isRunning = keyboard.leftShiftKey.isPressed;
            Vector3 worldMoveDir = GetCameraRelativeDirection(input);

            if (isOffline)
            {
                _controller.OnInputReceived_Server(worldMoveDir, isRunning);
                if (keyboard.spaceKey.wasPressedThisFrame) _controller.OnJumpTriggered_Server(worldMoveDir);
                if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) _controller.OnDashTriggered_Server();
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) _controller.OnAttackTriggered_Server();
                if (keyboard.eKey.wasPressedThisFrame) _controller.OnGrabTriggered_Server();
            }
            else
            {
                // Continuous input
                SubmitInputServerRpc(worldMoveDir, isRunning);

                // Individual triggers to ensure no frames are missed
                if (keyboard.spaceKey.wasPressedThisFrame) TriggerJumpServerRpc(worldMoveDir);
                else if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) TriggerDashServerRpc();
                
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) TriggerAttackServerRpc();
                if (keyboard.eKey.wasPressedThisFrame) TriggerGrabServerRpc();
            }
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
        private void TriggerAttackServerRpc()
        {
            _controller.OnAttackTriggered_Server();
        }

        [ServerRpc]
        private void TriggerGrabServerRpc()
        {
            _controller.OnGrabTriggered_Server();
        }
    }
}
