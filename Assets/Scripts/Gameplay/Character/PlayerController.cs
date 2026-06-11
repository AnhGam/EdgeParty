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

        // TeamID can be used for team-based logic, such as friendly fire or team-specific buffs
        public NetworkVariable<int> TeamID = new NetworkVariable<int>(0,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        
        [Header("Nameplate")]
        [SerializeField] private Transform headAnchor;
        [SerializeField] private NameplateUI nameplatePrefab;

        private NameplateUI nameplateInstance;

        [Header("Settings")]
        public float combatBoostMultiplier = 5f;
        public float tugOfWarBoost = 2.5f;
        private RagdollBoneFollower[] _followers;
        private GrabHandler[] _grabHandlers;

        private void OnValidate() => SyncLegacyReferences();

        private void Awake()
        {
            _followers = GetComponentsInChildren<RagdollBoneFollower>();
            _grabHandlers = GetComponentsInChildren<GrabHandler>();
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
            // Bỏ qua va chạm giữa TẤT CẢ các bộ phận trong cùng một nhân vật.
            // Điều này cực kỳ quan trọng với Active Ragdoll để tránh việc tay đấm vào chân/đầu 
            // gây ra phản lực làm nhân vật bị bắn đi hoặc xoay ngược.
            var allColliders = transform.root.GetComponentsInChildren<Collider>();
            for (int i = 0; i < allColliders.Length; i++)
            {
                for (int j = i + 1; j < allColliders.Length; j++)
                {
                    Physics.IgnoreCollision(allColliders[i], allColliders[j]);
                }
            }
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
            Debug.Log("PLAYER SPAWNED");

            // Camera cho người chơi local
            if (IsOwner)
            {
                AssignCameraTarget();
            }

            // Tạo Nameplate cho tất cả player
            if (headAnchor != null && nameplatePrefab != null)
            {
                nameplateInstance = Instantiate(nameplatePrefab, headAnchor);
                nameplateInstance.transform.localPosition = Vector3.zero;
                nameplateInstance.SetPlayerName($"Player {OwnerClientId}");
                nameplateInstance.SetMicLevel(0);
            }

            // Logic server
            if (IsServer)
            {
                // Team 1 = Red, Team 2 = Blue
                TeamID.Value = Random.Range(1, 3);

                Debug.Log("SPAWN TEAM: " + TeamID.Value);

                // Spawn theo team
                SpawnByTeam();

                // Nếu Team thay đổi thì spawn lại
                TeamID.OnValueChanged += OnTeamChanged;

                if (NetworkManager != null && NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnSceneEvent += OnSceneEvent;
                }
            }

            // Đăng ký event multiplier
            legMultiplier.OnValueChanged += OnMultiplierChanged;
            armMultiplier.OnValueChanged += OnMultiplierChanged;
            torsoMultiplier.OnValueChanged += OnMultiplierChanged;
            headMultiplier.OnValueChanged += OnMultiplierChanged;
            tailMultiplier.OnValueChanged += OnMultiplierChanged;

            // Áp dụng giá trị ban đầu
            ApplyBoneMultipliers();

            // Wire death/respawn into animator and camera
            if (stats != null)
            {
                stats.OnDied     += () => animController?.OnDeath();
                stats.OnRespawned += () => animController?.OnRespawn();

                // Spectator camera: only relevant for the local owner
                if (IsOwner)
                {
                    stats.OnDied     += OnLocalPlayerDied;
                    stats.OnRespawned += OnLocalPlayerRespawned;
                }
            }
        }



        public override void OnNetworkDespawn()
        {
            legMultiplier.OnValueChanged -= OnMultiplierChanged;
            armMultiplier.OnValueChanged -= OnMultiplierChanged;
            torsoMultiplier.OnValueChanged -= OnMultiplierChanged;
            headMultiplier.OnValueChanged -= OnMultiplierChanged;
            tailMultiplier.OnValueChanged -= OnMultiplierChanged;
            TeamID.OnValueChanged -= OnTeamChanged;

            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
            }
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted || sceneEvent.SceneEventType == SceneEventType.LoadComplete)
            {
                if (sceneEvent.SceneName == "DemoScene_Forest")
                {
                    Debug.Log($"[PlayerController] Scene {sceneEvent.SceneName} loaded. Spawning Player {OwnerClientId} to team {TeamID.Value}");
                    SpawnByTeam();
                }
            }
        }

        void SpawnByTeam()
        {
            if (SpawnManager.Instance == null) return;

            Vector3 pos = SpawnManager.Instance.GetSpawnPosition(TeamID.Value);
            Teleport(pos);
        }

        public void Teleport(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>();
            foreach (var rb in rigidbodies)
            {
                rb.position += offset;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            transform.position = position;
            if (ghostRoot != null)
            {
                ghostRoot.position = position;
            }
        }

        void OnTeamChanged(int oldValue, int newValue)
        {
            SpawnByTeam();
        }

        /// <summary>Called by DeathScreenUI when countdown ends. Requests respawn on server.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRespawnRpc()
        {
            if (stats == null) return;
            Vector3 pos = SpawnManager.Instance != null
                ? SpawnManager.Instance.GetSpawnPosition(TeamID.Value)
                : transform.position + Vector3.up * 2f;
            stats.Respawn(pos);
            Teleport(pos);
        }

        private void OnMultiplierChanged(float prev, float next) => ApplyBoneMultipliers();

        public void AssignCameraTarget()
        {
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null && motor != null)
                cam.target = motor.pelvisRigidbody != null ? motor.pelvisRigidbody.transform : transform;
        }

        private void Update()
        {
            if (!IsServerActive()) return;
            UpdateAnimatorState();
            UpdateTugOfWar();
            ApplyBoneMultipliers();
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
            if (motor != null && animController != null)
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
            tailMultiplier.Value = factor;
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
                float mult = f.category switch
                {
                    BoneCategory.Leg => legMultiplier.Value,
                    BoneCategory.Arm => armMultiplier.Value * armBoost,
                    BoneCategory.Torso => torsoMultiplier.Value * torsoBoost,
                    BoneCategory.Head => headMultiplier.Value,
                    BoneCategory.Tail => tailMultiplier.Value,
                    _ => 1f
                };

                // Stiffen leg joints during dash attack to keep character upright
                if (f.category == BoneCategory.Leg && animController != null && animController.IsPlayingOneShot)
                {
                    if (animController.ghostAnimator != null)
                    {
                        var info = animController.ghostAnimator.GetCurrentAnimatorStateInfo(0);
                        if (info.IsName(animController.dashState))
                        {
                            mult *= 3.0f;
                        }
                    }
                }

                f.SetSpringMultiplier(mult);
            }
        }
        /// <summary>
        /// Cập nhật mức độ mic (0 - 100)
        /// </summary>
        public void SetMicLevel(float value)
        {
            if (nameplateInstance != null)
            {
                nameplateInstance.SetMicLevel(value);
            }
        }

        /// <summary>
        /// Cập nhật tên hiển thị
        /// </summary>
        public void SetDisplayName(string playerName)
        {
            if (nameplateInstance != null)
            {
                nameplateInstance.SetPlayerName(playerName);
            }
        }

        // ─── Spectator Camera (local owner only) ─────────────────────────

        private void OnLocalPlayerDied()
        {
            // Try to find a living teammate and watch them
            Transform spectateTarget = FindLivingTeammate();
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (cam != null)
            {
                cam.target = spectateTarget != null ? spectateTarget : transform;
            }
        }

        private void OnLocalPlayerRespawned()
        {
            // Return camera to self
            AssignCameraTarget();
        }

        private Transform FindLivingTeammate()
        {
            int myTeam = TeamID.Value;
            var allPlayers = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var pc in allPlayers)
            {
                if (pc == this) continue;
                if (pc.TeamID.Value != myTeam) continue;
                if (pc.stats == null || pc.stats.IsDead.Value) continue;
                // Return pelvis or transform
                if (pc.motor != null && pc.motor.pelvisRigidbody != null)
                    return pc.motor.pelvisRigidbody.transform;
                return pc.transform;
            }
            return null;
        }
    }
}
