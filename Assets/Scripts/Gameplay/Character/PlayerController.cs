using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using EdgeParty.Gameplay.Camera;

namespace EdgeParty.Gameplay.Character
{
    public class PlayerController : NetworkBehaviour
    {
        [Header("References")]
        public Rigidbody pelvisRigidbody;
        public Transform ghostRoot;
        public Transform ghostPelvis;
        public Animator ghostAnimator;

        [Header("Movement")]
        public float walkForce = 75f;
        public float runForce = 125f;
        public float jumpImpulse = 50f;
        public float dashImpulse = 100f;
        public float rotationSpeed = 5f;
        [Range(0f, 1f)] public float airControlFactor = 0.15f;

        [Header("Bone Category Strength")]
        [Range(1f, 100f)] public float legMultiplier = 1f;  
        [Range(1f, 100f)] public float armMultiplier = 1f;
        [Range(1f, 100f)] public float torsoMultiplier = 1f;
        [Range(1f, 100f)] public float headMultiplier = 1f;

        [Header("Animation States")]
        public string noneState = "None";
        public string idleState = "IdleA";
        public string walkState = "Walk";
        public string runState = "Run";
        public string jumpState = "Jump";
        public string dashState = "Dash";

        private Transform _camTransform;
        
        // SERVER AUTHORITY CACHE
        private Vector3 _serverMoveDir;
        private bool _serverIsRunning;
        private Vector3 _lastFacingDirection = Vector3.forward;
        private bool _isPlayingOneShot;
        private float _oneShotTimer;
        private string _currentState = "";
        
        private RagdollBoneFollower[] _followers;
        private float _prevLegMul, _prevArmMul, _prevTorsoMul, _prevHeadMul;

        private void Awake()
        {
            if (global::UnityEngine.Camera.main != null)
                _camTransform = global::UnityEngine.Camera.main.transform;
                
            _followers = GetComponentsInChildren<RagdollBoneFollower>();

            if (ghostAnimator != null)
                ghostAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (ghostRoot != null)
                _lastFacingDirection = ghostRoot.forward;
        }

        private void Start()
        {
            ApplyBoneMultipliers();

            bool isOffline = NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer);
            if (isOffline)
            {
                AssignCameraTarget();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                AssignCameraTarget();
            }
        }

        private void AssignCameraTarget()
        {
            var cam = UnityEngine.Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null)
            {
                cam.target = pelvisRigidbody != null ? pelvisRigidbody.transform : transform;
            }
        }

        private void Update()
        {
            // 1. CHỈ CHỦ NHÂN (Hoặc Offline) mới được đọc bàn phím
            bool isOffline = NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer;
            bool isLocalController = isOffline || IsOwner;

            if (isLocalController)
            {
                ReadAndSendInput(isOffline);
            }

            // 2. CHỈ TRÊN SERVER (Hoặc Offline) mới chạy Animation và tính hướng
            bool canSimulate = isOffline || IsServer;
            if (!canSimulate || ghostAnimator == null) return;

            SimulateServerUpdate();
        }

        private void FixedUpdate()
        {
            bool isOffline = NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer;
            bool canSimulate = isOffline || IsServer;
            if (!canSimulate) return;

            SimulateServerPhysics();
        }

        // ================= INPUT (CHẠY TRÊN CLIENT/OWNER) =================
        private void ReadAndSendInput(bool isOffline)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1;
            if (keyboard.sKey.isPressed) input.y -= 1;
            if (keyboard.aKey.isPressed) input.x -= 1;
            if (keyboard.dKey.isPressed) input.x += 1;

            bool isRunning = keyboard.leftShiftKey.isPressed;
            Vector3 worldMoveDir = GetCameraRelativeDirection(input);

            if (isOffline)
            {
                _serverMoveDir = worldMoveDir;
                _serverIsRunning = isRunning;
                if (keyboard.spaceKey.wasPressedThisFrame) TriggerJump(worldMoveDir);
                else if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) TriggerDash();
            }
            else
            {
                // Bắn Input lên Server liên tục
                SubmitInputServerRpc(worldMoveDir, isRunning);

                // Bắn Trigger riêng lẻ để không bị hụt frame
                if (keyboard.spaceKey.wasPressedThisFrame) TriggerJumpServerRpc(worldMoveDir);
                else if (keyboard.leftShiftKey.wasPressedThisFrame && input.sqrMagnitude < 0.01f) TriggerDashServerRpc();
            }
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (_camTransform == null) return Vector3.zero;
            Vector3 camForward = Vector3.ProjectOnPlane(_camTransform.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(_camTransform.right, Vector3.up).normalized;
            return (camForward * input.y + camRight * input.x).normalized;
        }

        // ================= RPCs (TRUYỀN INPUT LÊN SERVER) =================
        [ServerRpc]
        private void SubmitInputServerRpc(Vector3 moveDir, bool isRunning)
        {
            _serverMoveDir = moveDir;
            _serverIsRunning = isRunning;
        }

        [ServerRpc]
        private void TriggerJumpServerRpc(Vector3 moveDir)
        {
            TriggerJump(moveDir);
        }

        [ServerRpc]
        private void TriggerDashServerRpc()
        {
            TriggerDash();
        }

        // ================= SIMULATION (CHẠY TRÊN SERVER) =================
        private void TriggerJump(Vector3 moveDir)
        {
            PlayState(jumpState, true);
            _isPlayingOneShot = true;
            _oneShotTimer = 0f;
            _serverMoveDir = Vector3.zero;

            if (pelvisRigidbody != null)
            {
                var vel = pelvisRigidbody.linearVelocity;
                pelvisRigidbody.linearVelocity = new Vector3(vel.x * 0.3f, vel.y, vel.z * 0.3f);
                pelvisRigidbody.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
                
                // Trợ lực nhỏ theo hướng camera để nhảy vọt tới
                if (moveDir.sqrMagnitude > 0.01f)
                    pelvisRigidbody.AddForce(moveDir * (jumpImpulse * 0.2f), ForceMode.Impulse);
            }
        }

        private void TriggerDash()
        {
            PlayState(dashState, true);
            _isPlayingOneShot = true;
            _oneShotTimer = 0f;
            _serverMoveDir = Vector3.zero;

            if (pelvisRigidbody != null && ghostRoot != null)
            {
                Vector3 dashDir = ghostRoot.forward;
                pelvisRigidbody.AddForce(dashDir * dashImpulse, ForceMode.Impulse);
            }
        }

        private void SimulateServerUpdate()
        {
            if (_prevLegMul != legMultiplier || _prevArmMul != armMultiplier ||
                _prevTorsoMul != torsoMultiplier || _prevHeadMul != headMultiplier)
            {
                ApplyBoneMultipliers();
            }

            bool hasMovement = _serverMoveDir.sqrMagnitude > 0.01f;

            if (_isPlayingOneShot)
            {
                _oneShotTimer += Time.deltaTime;
                var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                
                if ((info.IsName(_currentState) && info.normalizedTime >= 0.95f) || _oneShotTimer > 1.5f) 
                {
                    _isPlayingOneShot = false;
                }
                else 
                {
                    if (hasMovement)
                    {
                        _lastFacingDirection = _serverMoveDir;
                    }
                    return; 
                }
            }

            if (hasMovement)
            {
                ApplyBoneMultipliers(); 
                _lastFacingDirection = _serverMoveDir;

                PlayState(_serverIsRunning ? runState : walkState);

                var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(_currentState) && info.normalizedTime >= 1f)
                {
                    PlayState(_currentState, true);
                }
            }
            else
            {
                _serverMoveDir = Vector3.zero;
                ApplyBoneMultipliers();
                PlayState(noneState); 
            }
        }

        private void SimulateServerPhysics()
        {
            if (pelvisRigidbody != null && ghostPelvis != null)
            {
                if (ghostRoot != null && _lastFacingDirection.sqrMagnitude > 0.001f)
                {
                    ghostRoot.rotation = Quaternion.LookRotation(_lastFacingDirection, Vector3.up);
                }

                Quaternion targetRot = ghostPelvis.rotation;
                Quaternion deltaRot = targetRot * Quaternion.Inverse(pelvisRigidbody.rotation);

                deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;

                if (Mathf.Abs(angle) > 0.01f)
                {
                    Vector3 torque = axis.normalized * (angle * rotationSpeed);
                    pelvisRigidbody.AddTorque(torque, ForceMode.Acceleration);
                }
            }

            if (pelvisRigidbody == null || _serverMoveDir.sqrMagnitude < 0.01f) return;

            float force = _serverIsRunning ? runForce : walkForce;

            if (_isPlayingOneShot)
                force *= airControlFactor;

            pelvisRigidbody.AddForce(_serverMoveDir * force, ForceMode.Acceleration);
        }

        private void PlayState(string stateName, bool restart = false)
        {
            if (ghostAnimator == null) return;
            if (!restart && _currentState == stateName) return;
            ghostAnimator.Play(stateName, 0, restart ? 0f : -1f);
            _currentState = stateName;
        }

        private void ApplyBoneMultipliers()
        {
            if (_followers == null) return;
            foreach (var f in _followers)
            {
                switch (f.category)
                {
                    case BoneCategory.Leg: f.SetSpringMultiplier(legMultiplier); break;
                    case BoneCategory.Arm: f.SetSpringMultiplier(armMultiplier); break;
                    case BoneCategory.Torso: f.SetSpringMultiplier(torsoMultiplier); break;
                    case BoneCategory.Head: f.SetSpringMultiplier(headMultiplier); break;
                }
            }
            _prevLegMul = legMultiplier;
            _prevArmMul = armMultiplier;
            _prevTorsoMul = torsoMultiplier;
            _prevHeadMul = headMultiplier;
        }
    }
}
