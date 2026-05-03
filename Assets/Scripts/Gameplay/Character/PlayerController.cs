using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Camera;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Main coordinator. Now also wires PlayerStats into combat actions.
    /// </summary>
    public class PlayerController : NetworkBehaviour
    {
        [Header("Components")]
        public CharacterMotor motor;
        public CharacterAnimationController animController;
        public PlayerInputHandler inputHandler;
        public PlayerStats stats;               // ← new

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
        public float combatBoostMultiplier = 8f;

        private RagdollBoneFollower[] _followers;

        private void OnValidate() => SyncLegacyReferences();

        private void Awake()
        {
            _followers = GetComponentsInChildren<RagdollBoneFollower>();

            Transform root = transform.root;
            if (motor == null) motor = root.GetComponentInChildren<CharacterMotor>();
            if (animController == null) animController = root.GetComponentInChildren<CharacterAnimationController>();
            if (inputHandler == null) inputHandler = root.GetComponentInChildren<PlayerInputHandler>();
            if (stats == null) stats = root.GetComponentInChildren<PlayerStats>();

            if (ghostRoot == null) ghostRoot = root.Find("Chibi_Monkey_00_Ghost");
            if (pelvisRigidbody == null)
            {
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>())
                    if (rb.name.ToLower().Contains("pelvis")) { pelvisRigidbody = rb; break; }
            }

            SyncLegacyReferences();
            IgnoreInternalCollisions();
        }

        private void IgnoreInternalCollisions()
        {
            var cols = transform.root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++)
                for (int j = i + 1; j < cols.Length; j++)
                    Physics.IgnoreCollision(cols[i], cols[j]);
        }

        private void SyncLegacyReferences()
        {
            if (motor != null)
            {
                if (pelvisRigidbody != null) motor.pelvisRigidbody = pelvisRigidbody;
                if (ghostRoot != null) motor.ghostRoot = ghostRoot;
                if (ghostPelvis != null) motor.ghostPelvis = ghostPelvis;
                if (motor.pelvisRigidbody == null)
                    foreach (var rb in transform.root.GetComponentsInChildren<Rigidbody>())
                        if (rb.name.ToLower().Contains("pelvis")) { motor.pelvisRigidbody = rb; break; }
            }
            if (animController != null)
            {
                if (ghostAnimator != null) animController.ghostAnimator = ghostAnimator;
                if (ghostRoot != null) animController.ghostRoot = ghostRoot;
                if (animController.ghostAnimator == null)
                    animController.ghostAnimator = GetComponentInChildren<Animator>();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) AssignCameraTarget();

            legMultiplier.OnValueChanged += OnMultiplierChanged;
            armMultiplier.OnValueChanged += OnMultiplierChanged;
            torsoMultiplier.OnValueChanged += OnMultiplierChanged;
            headMultiplier.OnValueChanged += OnMultiplierChanged;
            tailMultiplier.OnValueChanged += OnMultiplierChanged;
            ApplyBoneMultipliers();

            // Wire death/respawn into animator
            if (stats != null)
            {
                stats.OnDied += () => animController?.OnDeath();
                stats.OnRespawned += () => animController?.OnRespawn();
            }
        }

        public override void OnNetworkDespawn()
        {
            legMultiplier.OnValueChanged -= OnMultiplierChanged;
            armMultiplier.OnValueChanged -= OnMultiplierChanged;
            torsoMultiplier.OnValueChanged -= OnMultiplierChanged;
            headMultiplier.OnValueChanged -= OnMultiplierChanged;
            tailMultiplier.OnValueChanged -= OnMultiplierChanged;
        }

        private void OnMultiplierChanged(float prev, float next) => ApplyBoneMultipliers();

        private void AssignCameraTarget()
        {
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null && motor != null)
                cam.target = motor.pelvisRigidbody != null ? motor.pelvisRigidbody.transform : transform;
        }

        private void Update()
        {
            if (!IsServerActive()) return;
            UpdateAnimatorState();
            ApplyBoneMultipliers();
        }

        private bool IsServerActive() =>
            IsServer || NetworkManager.Singleton == null
            || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer);

        private void UpdateAnimatorState()
        {
            if (motor != null && animController != null)
                motor.SetOneShotActive(animController.IsPlayingOneShot);
        }

        // ─── Input hooks ──────────────────────────────────────────────────

        public void OnInputReceived_Server(Vector3 moveDir, bool isRunning)
        {
            if (stats != null && stats.IsDead.Value) return;

            // Drain sprint stamina if actually running
            if (isRunning && moveDir.sqrMagnitude > 0.01f)
                stats?.DrainSprintStamina(Time.deltaTime);

            // Block sprint if out of stamina
            bool canRun = isRunning && (stats == null || stats.HasStaminaToSprint);

            motor?.SetMovementInput(moveDir, canRun);
            animController?.SetMovementInput(moveDir, canRun);
        }

        public void OnJumpTriggered_Server(Vector3 moveDir)
        {
            if (stats != null && stats.IsDead.Value) return;
            if (motor != null && animController != null && motor.IsGrounded)
            {
                motor.ApplyJump(moveDir);
                animController.TriggerJump();
            }
        }

        public void OnDashTriggered_Server()
        {
            if (stats != null && stats.IsDead.Value) return;
            if (animController == null || !animController.CanDash()) return;
            if (stats != null && !stats.SpendDashStamina()) return;   // check + deduct

            motor?.ApplyDash();
            animController.TriggerDash();
        }

        public void OnAttackTriggered_Server()
        {
            if (stats != null && stats.IsDead.Value) return;
            animController?.TriggerAttack();
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
            bool attacking = animController != null && animController.IsAttacking;

            foreach (var f in _followers)
            {
                bool isArm = f.category == BoneCategory.Arm;
                f.SetCombatMode(isArm && attacking);

                float mult = f.category switch
                {
                    BoneCategory.Leg => legMultiplier.Value,
                    BoneCategory.Arm => armMultiplier.Value * (attacking ? combatBoostMultiplier : 1f),
                    BoneCategory.Torso => torsoMultiplier.Value,
                    BoneCategory.Head => headMultiplier.Value,
                    BoneCategory.Tail => tailMultiplier.Value,
                    _ => 1f
                };
                f.SetSpringMultiplier(mult);
            }
        }
    }
}