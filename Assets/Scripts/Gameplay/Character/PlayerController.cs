using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Camera;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// The main coordinator for the player character.
    /// Orchestrates input, physics movement, and animation states.
    /// </summary>
    public class PlayerController : NetworkBehaviour
    {
        [Header("Components")]
        public CharacterMotor motor;
        public CharacterAnimationController animController;
        public PlayerInputHandler inputHandler;

        [Header("Legacy References (Hidden - Used by Editor Tools)")]
        [HideInInspector] public Rigidbody pelvisRigidbody;
        [HideInInspector] public Transform ghostRoot;
        [HideInInspector] public Transform ghostPelvis;
        [HideInInspector] public Animator ghostAnimator;

        [Header("Bone Strength Settings (Networked)")]
        public NetworkVariable<float> legMultiplier = new NetworkVariable<float>(1f);
        public NetworkVariable<float> armMultiplier = new NetworkVariable<float>(1f);
        public NetworkVariable<float> torsoMultiplier = new NetworkVariable<float>(1f);
        public NetworkVariable<float> headMultiplier = new NetworkVariable<float>(1f);
        public NetworkVariable<float> tailMultiplier = new NetworkVariable<float>(1f);

        [Header("Settings")]
        public float combatBoostMultiplier = 5f;
        public float tugOfWarBoost = 2.5f;

        private RagdollBoneFollower[] _followers;
        private GrabHandler[] _grabHandlers;

        private void OnValidate()
        {
            SyncLegacyReferences();
        }

        private void Awake()
        {
            _followers = GetComponentsInChildren<RagdollBoneFollower>();
            _grabHandlers = GetComponentsInChildren<GrabHandler>();
            
            // Deep search: Search from the root of this player hierarchy
            Transform root = transform.root;
            if (motor == null) motor = root.GetComponentInChildren<CharacterMotor>();
            if (animController == null) animController = root.GetComponentInChildren<CharacterAnimationController>();
            if (inputHandler == null) inputHandler = root.GetComponentInChildren<PlayerInputHandler>();
            
            // Search for legacy references globally in this prefab hierarchy if missing
            if (ghostRoot == null) ghostRoot = root.Find("Chibi_Monkey_00_Ghost");
            if (pelvisRigidbody == null)
            {
                var allRbs = root.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in allRbs) if (rb.name.ToLower().Contains("pelvis")) { pelvisRigidbody = rb; break; }
            }

            SyncLegacyReferences();
            IgnoreInternalCollisions();
        }

        private void IgnoreInternalCollisions()
        {
            var colliders = transform.root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }
        }

        private void SyncLegacyReferences()
        {
            if (motor != null)
            {
                // Only sync if these are NOT null, to prevent overwriting inspector values with null
                if (pelvisRigidbody != null) motor.pelvisRigidbody = pelvisRigidbody;
                if (ghostRoot != null) motor.ghostRoot = ghostRoot;
                if (ghostPelvis != null) motor.ghostPelvis = ghostPelvis;
                
                // Final deep search fallback for motor's pelvis
                if (motor.pelvisRigidbody == null)
                {
                    var allRbs = transform.root.GetComponentsInChildren<Rigidbody>();
                    foreach (var rb in allRbs) if (rb.name.ToLower().Contains("pelvis")) { motor.pelvisRigidbody = rb; break; }
                }
            }

            if (animController != null)
            {
                if (ghostAnimator != null) animController.ghostAnimator = ghostAnimator;
                if (ghostRoot != null) animController.ghostRoot = ghostRoot;
                
                // Final fallback for animController's animator if still null
                if (animController.ghostAnimator == null)
                    animController.ghostAnimator = GetComponentInChildren<Animator>();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                AssignCameraTarget();
            }

            // Đăng ký sự kiện thay đổi giá trị cho tất cả các xương
            legMultiplier.OnValueChanged += OnMultiplierChanged;
            armMultiplier.OnValueChanged += OnMultiplierChanged;
            torsoMultiplier.OnValueChanged += OnMultiplierChanged;
            headMultiplier.OnValueChanged += OnMultiplierChanged;
            tailMultiplier.OnValueChanged += OnMultiplierChanged;

            // Cập nhật lần đầu khi spawn
            ApplyBoneMultipliers();
        }

        public override void OnNetworkDespawn()
        {
            legMultiplier.OnValueChanged -= OnMultiplierChanged;
            armMultiplier.OnValueChanged -= OnMultiplierChanged;
            torsoMultiplier.OnValueChanged -= OnMultiplierChanged;
            headMultiplier.OnValueChanged -= OnMultiplierChanged;
            tailMultiplier.OnValueChanged -= OnMultiplierChanged;
        }

        private void OnMultiplierChanged(float previousValue, float newValue)
        {
            ApplyBoneMultipliers();
        }

        private void AssignCameraTarget()
        {
            var cam = UnityEngine.Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null && motor != null)
            {
                cam.target = motor.pelvisRigidbody != null ? motor.pelvisRigidbody.transform : transform;
            }
        }

        private void Update()
        {
            if (IsServerActive())
            {
                UpdateAnimatorState();
                UpdateTugOfWar();
                ApplyBoneMultipliers(); // Continuous update to handle combat boost
            }
        }

        private void UpdateTugOfWar()
        {
            if (motor == null) return;

            bool isAnyHandConnected = false;
            
            // Search handlers if not found yet (e.g. added at runtime)
            if (_grabHandlers == null || _grabHandlers.Length == 0)
                _grabHandlers = GetComponentsInChildren<GrabHandler>();

            if (_grabHandlers != null)
            {
                foreach (var h in _grabHandlers) if (h.IsConnected) { isAnyHandConnected = true; break; }
            }

            motor.movementForceMultiplier = isAnyHandConnected ? tugOfWarBoost : 1f;
        }

        private bool IsServerActive()
        {
            return IsServer || NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer);
        }

        private void UpdateAnimatorState()
        {
            if (motor != null && animController != null)
            {
                motor.SetOneShotActive(animController.IsPlayingOneShot);
            }
        }

        // ================= HOOKS FOR INPUT HANDLER (SERVER SIDE) =================
        
        public void OnInputReceived_Server(Vector3 moveDir, bool isRunning)
        {
            if (motor != null) motor.SetMovementInput(moveDir, isRunning);
            if (animController != null) animController.SetMovementInput(moveDir, isRunning);
        }

        public void OnJumpTriggered_Server(Vector3 moveDir)
        {
            if (motor != null && animController != null && motor.IsGrounded)
            {
                motor.ApplyJump(moveDir);
                animController.TriggerJump();
            }
        }

        public void OnDashTriggered_Server()
        {
            if (motor != null && animController != null && animController.CanDash())
            {
                motor.ApplyDash();
                animController.TriggerDash();
            }
        }

        public void OnAttackTriggered_Server()
        {
            if (animController != null && animController.CanAttack())
            {
                animController.TriggerAttack();
            }
        }

        public void OnGrabTriggered_Server()
        {
            if (animController != null)
            {
                animController.TriggerGrab();
                
                // Sync grab handlers with the state
                bool isGrabbing = animController.CurrentState == PlayerState.Grab;

                if (_grabHandlers == null || _grabHandlers.Length == 0)
                    _grabHandlers = GetComponentsInChildren<GrabHandler>();

                if (_grabHandlers != null)
                {
                    foreach (var h in _grabHandlers) h.SetActive(isGrabbing);
                }
            }
        }

        public void SetRagdollStrength(float factor)
        {
            if (!IsServer) return;
            legMultiplier.Value = factor;
            armMultiplier.Value = factor;
            torsoMultiplier.Value = factor;
            headMultiplier.Value = factor;
        }

        private void ApplyBoneMultipliers()
        {
            if (_followers == null) return;
            
            float armBoost = 1f;
            float torsoBoost = 1f;

            if (animController != null)
            {
                if (animController.CurrentState == PlayerState.Grab)
                {
                    armBoost = combatBoostMultiplier * 10f; // High stiffness for correct height
                    torsoBoost = 3f; // Stable torso
                }
                else if (animController.IsAttacking)
                {
                    armBoost = combatBoostMultiplier;
                }
            }

            foreach (var f in _followers)
            {
                switch (f.category)
                {
                    case BoneCategory.Leg: f.SetSpringMultiplier(legMultiplier.Value); break;
                    case BoneCategory.Arm: f.SetSpringMultiplier(armMultiplier.Value * armBoost); break;
                    case BoneCategory.Torso: f.SetSpringMultiplier(torsoMultiplier.Value * torsoBoost); break;
                    case BoneCategory.Head: f.SetSpringMultiplier(headMultiplier.Value); break;
                    case BoneCategory.Tail: f.SetSpringMultiplier(tailMultiplier.Value); break;
                }
            }
        }
    }
}
