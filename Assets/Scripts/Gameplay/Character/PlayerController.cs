using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections;
using EdgeParty.Auth;
using EdgeParty.Gameplay.Camera;
using EdgeParty.Gameplay.Items;

namespace EdgeParty.Gameplay.Character
{
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

        // TeamID: synchronized across all clients
        public NetworkVariable<int> TeamID = new NetworkVariable<int>(0,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);

        [Header("Item Held")]
        // Item hiện tại player đang cầm (null = tay trống)
        public NetworkVariable<int> currentHeldItemNetVar = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
        public WeaponPickup.ItemType? CurrentHeldItem 
        { 
            get 
            { 
                if (currentHeldItemNetVar.Value == -1) return null; 
                return (WeaponPickup.ItemType)currentHeldItemNetVar.Value; 
            } 
        }
        
        public int heldItemCharges = 0;

        [Header("Weapon Hand Offsets")]
        public Vector3 bombHandOffset = Vector3.zero;
        public Vector3 bombHandRotation = Vector3.zero;
        public Vector3 gunHandOffset = new Vector3(-0.05f, 0.08f, 0.15f);
        public Vector3 gunHandRotation = new Vector3(0f, 90f, 0f);

        private float _lastStunGunFireTime = -99f;
        
        // Player display name – synced once at spawn, read by all clients for nameplates
        public NetworkVariable<FixedString64Bytes> playerNameSync = new NetworkVariable<FixedString64Bytes>(
            new FixedString64Bytes("Player"),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        
        [Header("Nameplate")]
        [SerializeField] private Transform headAnchor;
        [SerializeField] private NameplateUI nameplatePrefab;

        private NameplateUI nameplateInstance;

        [Header("Settings")]
        public float combatBoostMultiplier = 5f;
        public float tugOfWarBoost = 2.5f;

        [Header("Dead State")]
        [Tooltip("Giây tự hồi sau khi chết")]
        public float autoRespawnDelay = 10f;
        [Tooltip("Giây chờ sau khi spring phục hồi trước khi nhận phím")]
        public float inputBlockAfterRespawn = 1.5f;

        private RagdollBoneFollower[] _followers;
        private GrabHandler[] _grabHandlers;
        private RigidbodyConstraints _originalPelvisConstraints;

        // Trạng thái chết cục bộ (client-side): block input di chuyển, chỉ cho xoay camera
        private bool _isLocallyDead = false;
        private bool _isInputBlocked = false;  // block sau respawn cho đến khi nhân vật đứng ổn

        private void OnValidate() => SyncLegacyReferences();

        private void Awake()
        {
            autoRespawnDelay = 10f; // Force 10s auto-respawn delay programmatically to override inspector defaults
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
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
                    if (rb.name.ToLower().Contains("pelvis")) { pelvisRigidbody = rb; break; }
            }
            if (pelvisRigidbody != null)
            {
                _originalPelvisConstraints = pelvisRigidbody.constraints;
            }

            SyncLegacyReferences();
            IgnoreInternalCollisions();

            // Dynamically attach NetworkPlayerAppearance to replicate cosmetics in multiplayer
            var appearance = GetComponentInChildren<NetworkPlayerAppearance>(true);
            if (appearance == null)
            {
                appearance = gameObject.AddComponent<NetworkPlayerAppearance>();
                Debug.Log("[PlayerController] Dynamically attached NetworkPlayerAppearance to player!");
            }

            // Prevent active ragdoll limbs from sinking into the ground or jittering
            foreach (var rb in transform.root.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.maxDepenetrationVelocity = 25f;
                if (!rb.isKinematic)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                }
            }
        }

        private void IgnoreInternalCollisions()
        {
            // Bỏ qua va chạm giữa TẤT CẢ các bộ phận trong cùng một nhân vật.
            // Điều này cực kỳ quan trọng với Active Ragdoll để tránh việc tay đấm vào chân/đầu 
            // gây ra phản lực làm nhân vật bị bắn đi hoặc xoay ngược.
            var allColliders = GetComponentsInChildren<Collider>(true);
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
                    foreach (var rb in transform.root.GetComponentsInChildren<Rigidbody>(true))
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

            if (IsLocalPlayer)
            {
                AssignCameraTarget();
            }

            if (headAnchor != null && nameplatePrefab != null)
            {
                nameplateInstance = Instantiate(nameplatePrefab, headAnchor);
                nameplateInstance.transform.localPosition = Vector3.zero;
                // Show current synced name (may already be set for late-joiners)
                nameplateInstance?.SetPlayerName(playerNameSync.Value.ToString());
                nameplateInstance?.SetTeamColor(TeamID.Value);
                nameplateInstance.SetMicLevel(0);
            }

            playerNameSync.OnValueChanged += (_, newName) =>
            {
                nameplateInstance?.SetPlayerName(newName.ToString());
            };

            if (IsLocalPlayer)
            {
                // Write username into the network variable so all clients see it
                string myName = AuthService.Instance != null ? AuthService.Instance.CachedUsername : $"Player {OwnerClientId}";
                playerNameSync.Value = new FixedString64Bytes(myName);
            }

            if (IsServer)
            {
                // ─── Balanced 1v1 team assignment ───
                // Đếm số player đang có trên server → player chẵn vào Team2, lẻ vào Team1
                int connectedClients = NetworkManager.ConnectedClientsList.Count;
                TeamID.Value = (connectedClients % 2 == 1) ? 1 : 2;  // 1st=T1, 2nd=T2, 3rd=T1...

                Debug.Log($"[PlayerController] Client#{connectedClients} assigned to Team {TeamID.Value}");
                SpawnByTeam();

                // Subscribe trước khi TeamID có thể thay đổi
                TeamID.OnValueChanged += OnTeamChanged;

                if (NetworkManager != null && NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnSceneEvent += OnSceneEvent;
                }
            }

            legMultiplier.OnValueChanged += OnMultiplierChanged;
            armMultiplier.OnValueChanged += OnMultiplierChanged;
            torsoMultiplier.OnValueChanged += OnMultiplierChanged;
            headMultiplier.OnValueChanged += OnMultiplierChanged;
            tailMultiplier.OnValueChanged += OnMultiplierChanged;

            currentHeldItemNetVar.OnValueChanged += OnHeldItemChanged;
            UpdateWeaponVisuals(); // Khởi tạo visual vũ khí cho late-joiners

            ApplyBoneMultipliers();

            // Wire death/respawn into animator and camera
            if (stats != null)
            {
                stats.OnDied     += () => animController?.OnDeath();
                stats.OnRespawned += () => animController?.OnRespawn();

                // Ragdoll limp/restore — chạy trên tất cả clients vì followers là local
                stats.OnDied     += OnPlayerDied_Ragdoll;
                stats.OnRespawned += OnPlayerRespawned_Ragdoll;

                // Spectator camera: only relevant for the local owner
                if (IsLocalPlayer)
                {
                    stats.OnDied     += OnLocalPlayerDied;
                    stats.OnRespawned += OnLocalPlayerRespawned;
                }

                // Server auto-respawn sau delay
                if (IsServer)
                {
                    stats.OnDied += () => StartCoroutine(AutoRespawnCoroutine());
                }
            }
        }

        private void OnTeamChanged(int oldTeam, int newTeam)
        {
            nameplateInstance?.SetTeamColor(newTeam);
            SpawnByTeam();
            Debug.Log($"[PlayerController] {playerNameSync.Value} → Team {newTeam}");
        }

        // ─── Item Pickup API (gọi bởi WeaponPickup.cs) ─────────────────────────

        public void PickupItem(WeaponPickup.ItemType type)
        {
            if (!IsServer) return;
            currentHeldItemNetVar.Value = (int)type;
            heldItemCharges = 1;
            Debug.Log($"[PlayerController] {playerNameSync.Value} picked up {type}");
        }


        public void ConsumeHeldItem()
        {
            if (!IsServer) return;
            currentHeldItemNetVar.Value = -1;
        }

        private void OnHeldItemChanged(int oldVal, int newVal)
        {
            if (newVal != -1)
            {
                UpdateWeaponVisuals();
                
                // Play pickup sound on all clients
                var pickupSFX = Resources.Load<AudioClip>("Audios/Gameplay/Item_pickup");
                if (pickupSFX != null && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX(pickupSFX);
                }
            }
            else if (newVal == -1 && oldVal != -1)
            {
                SpawnWeaponDisappearVFX();
                UpdateWeaponVisuals();
            }
        }

        private static Material _particleMat;
        private static Material GetParticleMaterial()
        {
            if (_particleMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null) _particleMat = new Material(shader);
            }
            return _particleMat;
        }

        private void SpawnWeaponDisappearVFX()
        {
            Transform hand = FindRightHand();
            if (hand == null) return;

            var vfx = new GameObject("WeaponDisappearVFX");
            vfx.transform.position = hand.position;
            var ps = vfx.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 0.4f;
            main.startSpeed = 2f;
            main.startSize = 0.15f;
            
            // Cyan colored sparks for gun, orange smoke for bomb
            Color sparksColor = (CurrentHeldItem == WeaponPickup.ItemType.StunGun) 
                ? new Color(0.3f, 0.9f, 1f, 0.8f) 
                : new Color(1f, 0.4f, 0.1f, 0.8f);
            main.startColor = sparksColor;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var renderer = vfx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (GetParticleMaterial() != null) renderer.material = GetParticleMaterial();

            ps.Play();
            Destroy(vfx, 1f);
        }

        private GameObject _weaponVisualInstance;

        private void UpdateWeaponVisuals()
        {
            if (_weaponVisualInstance != null)
            {
                Destroy(_weaponVisualInstance);
                _weaponVisualInstance = null;
            }

            if (CurrentHeldItem == null) return;

            Transform hand = FindRightHand();
            if (hand == null) return;

            string prefabName = CurrentHeldItem == WeaponPickup.ItemType.Bomb ? "BombBall" : "Cosmic_Retro_Blaster_2_5";
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Debug.LogWarning($"[PlayerController] Failed to load weapon prefab '{prefabName}' from Resources!");
                return;
            }

            _weaponVisualInstance = Instantiate(prefab, hand);

            // Strip only specific Netcode/Pickup scripts to make it a dumb visual, but keep StunGun component
            foreach (var mono in _weaponVisualInstance.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mono is NetworkObject || mono is WeaponPickup)
                {
                    Destroy(mono);
                }
            }

            // STOP PHYSICS from simulating before destruction
            foreach (var rbChild in _weaponVisualInstance.GetComponentsInChildren<Rigidbody>())
            {
                rbChild.isKinematic = true;
                rbChild.detectCollisions = false;
                Destroy(rbChild);
            }

            foreach (var col in _weaponVisualInstance.GetComponentsInChildren<Collider>())
            {
                Destroy(col);
            }

            if (CurrentHeldItem == WeaponPickup.ItemType.Bomb)
            {
                _weaponVisualInstance.transform.localPosition = bombHandOffset;
                _weaponVisualInstance.transform.localRotation = Quaternion.Euler(bombHandRotation);
                _weaponVisualInstance.transform.localScale = Vector3.one * 0.4f;
            }
            else
            {
                _weaponVisualInstance.transform.localPosition = gunHandOffset;
                _weaponVisualInstance.transform.localRotation = Quaternion.Euler(gunHandRotation);
                _weaponVisualInstance.transform.localScale = Vector3.one * 1.2f;
            }

            _weaponVisualInstance.SetActive(true);
        }

        private Transform FindRightHand()
        {
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            foreach (var t in allChildren)
            {
                if (t.name.ToLower().Contains("hand_r") || t.name.ToLower().Contains("hand.r") || t.name.ToLower().Contains("r_hand"))
                {
                    return t;
                }
            }
            return transform;
        }



        public override void OnNetworkDespawn()
        {
            legMultiplier.OnValueChanged -= OnMultiplierChanged;
            armMultiplier.OnValueChanged -= OnMultiplierChanged;
            torsoMultiplier.OnValueChanged -= OnMultiplierChanged;
            headMultiplier.OnValueChanged -= OnMultiplierChanged;
            tailMultiplier.OnValueChanged -= OnMultiplierChanged;
            TeamID.OnValueChanged -= OnTeamChanged;
            
            currentHeldItemNetVar.OnValueChanged -= OnHeldItemChanged;

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
            if (GetComponent<TestDummy>() != null) return;
            if (SpawnManager.Instance == null) return;

            Vector3 pos = SpawnManager.Instance.GetSpawnPosition(TeamID.Value);
            Teleport(pos);
        }

        public void Teleport(Vector3 position)
        {
            Rigidbody[] rigidbodies = transform.root.GetComponentsInChildren<Rigidbody>(true);
            
            // 1. Temporarily make all rigidbodies kinematic to disable joint physics solver during teleport
            System.Collections.Generic.List<bool> wasKinematic = new System.Collections.Generic.List<bool>();
            foreach (var rb in rigidbodies)
            {
                wasKinematic.Add(rb.isKinematic);
                rb.isKinematic = true;
            }

            // 2. Move the root to the target position
            transform.root.position = position;

            // 3. If ghostRoot is somehow not a child of root, move it as well
            if (ghostRoot != null && !ghostRoot.IsChildOf(transform.root))
            {
                ghostRoot.position = position;
            }

            // 4. Restore kinematic state, reset velocities, and snap ragdoll bones to ghost bones
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                var rb = rigidbodies[i];
                rb.isKinematic = wasKinematic[i];
                
                if (!rb.isKinematic)
                {
                    var f = rb.GetComponent<RagdollBoneFollower>();
                    if (f != null && f.targetBone != null)
                    {
                        rb.position = f.targetBone.position;
                        rb.rotation = f.targetBone.rotation;
                    }
                    else if (motor != null && rb == motor.pelvisRigidbody && motor.ghostPelvis != null)
                    {
                        rb.position = motor.ghostPelvis.position;
                        rb.rotation = motor.ghostPelvis.rotation;
                    }
                    else
                    {
                        rb.position = rb.transform.position;
                        rb.rotation = rb.transform.rotation;
                    }

                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }



        /// <summary>Called by DeathScreenUI when countdown ends. Requests respawn on server.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRespawnRpc()
        {
            if (stats == null) return;
            Vector3 currentPos = pelvisRigidbody != null ? pelvisRigidbody.position : transform.position;

            Vector3 origin = currentPos + Vector3.up * 2f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 10f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            Vector3 groundPoint = currentPos;
            bool foundGround = false;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform.root))
                {
                    continue; // Ignore self
                }
                groundPoint = hit.point;
                foundGround = true;
                break;
            }
            
            Vector3 pos;
            if (foundGround)
            {
                pos = groundPoint + Vector3.up * 0.2f;
            }
            else
            {
                pos = currentPos + Vector3.up * 1.5f;
            }

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
            // Real-time show/hide nameplate based on settings
            if (nameplateInstance != null)
            {
                bool showNames = PlayerPrefs.GetInt("ShowPlayerNames", 1) == 1;
                if (nameplateInstance.gameObject.activeSelf != showNames)
                {
                    nameplateInstance.gameObject.SetActive(showNames);
                }
            }

            if (!IsServerActive()) return;
            UpdateAnimatorState();
            UpdateTugOfWar();
            // Không apply bone multipliers khi đang dead (spring đã về 0)
            if (!_isLocallyDead)
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
            if (ForestGameManager.Instance != null && !ForestGameManager.Instance.IsMatchActive)
            {
                motor?.SetMovementInput(Vector3.zero, false);
                animController?.SetMovementInput(Vector3.zero, false);
                return;
            }
            if (stats != null && stats.IsDead.Value) return;
            // Block input sau khi vừa hồi (cho nhân vật đứng ổn)
            if (_isInputBlocked) return;

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
            if (ForestGameManager.Instance != null && !ForestGameManager.Instance.IsMatchActive) return;
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
            // Only allow jumping when grounded to prevent infinite spam
            if (motor != null && !motor.IsGrounded) return;
            if (motor != null && animController != null)
            {
                motor.ApplyJump(moveDir);
                animController.TriggerJump();
            }
        }

        public void OnDashTriggered_Server()
        {
            if (ForestGameManager.Instance != null && !ForestGameManager.Instance.IsMatchActive) return;
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
            if (animController == null || !animController.CanDash()) return;
            if (stats != null && !stats.SpendDashStamina()) return;   // check + deduct

            motor?.ApplyDash();
            animController.TriggerDash();
        }

        public void OnAttackTriggered_Server(Vector3 aimDirection = default)
        {
            if (ForestGameManager.Instance != null && !ForestGameManager.Instance.IsMatchActive) return;
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;

            Transform aimTransform = ghostRoot != null ? ghostRoot : transform;
            Vector3 centerPos = pelvisRigidbody != null ? pelvisRigidbody.position : transform.position;

            Vector3 finalAimDir = (aimDirection != default && aimDirection.sqrMagnitude > 0.001f) ? aimDirection : aimTransform.forward;

            // Rotate character's visual orientation towards aimDirection if aiming
            Vector3 flatAimDir = Vector3.ProjectOnPlane(finalAimDir, Vector3.up).normalized;
            if (flatAimDir.sqrMagnitude > 0.001f && ghostRoot != null)
            {
                ghostRoot.rotation = Quaternion.LookRotation(flatAimDir, Vector3.up);
            }

            if (CurrentHeldItem == WeaponPickup.ItemType.Bomb)
            {
                Vector3 spawnPos = centerPos + Vector3.up * 1.5f + finalAimDir * 0.8f;
                Vector3 throwDir = (finalAimDir + Vector3.up * 0.3f).normalized;
                
                // Server logical bomb (networked)
                var prefab = Resources.Load<GameObject>("BombBall");
                NetworkObject bombNetObj = null;

                if (EdgeParty.ConnectionManagement.NetworkObjectPool.Singleton != null && prefab != null)
                {
                    bombNetObj = EdgeParty.ConnectionManagement.NetworkObjectPool.Singleton.GetNetworkObject(prefab, spawnPos, Quaternion.identity);
                    if (!bombNetObj.IsSpawned) bombNetObj.Spawn();
                }
                else
                {
                    var bombGo = Instantiate(prefab, spawnPos, Quaternion.identity);
                    bombNetObj = bombGo.GetComponent<NetworkObject>();
                    bombNetObj.Spawn();
                }

                var bombItem = bombNetObj.GetComponent<BombItem>();
                if (bombItem != null)
                {
                    bombItem.ThrowBomb(throwDir, 12f, this);
                }

                ConsumeHeldItem();
                animController?.ForceTriggerAttack();
                return;
            }
            else if (CurrentHeldItem == WeaponPickup.ItemType.StunGun)
            {
                if (heldItemCharges <= 0) return;

                // 5s cooldown check
                if (Time.time - _lastStunGunFireTime < 5f)
                {
                    return;
                }
                _lastStunGunFireTime = Time.time;

                Vector3 finalForwardDir = aimTransform.forward;
                Vector3 origin = centerPos + Vector3.up * 0.8f + finalForwardDir * 0.4f;
                Vector3 direction = finalForwardDir;

                var stunGun = GetComponentInChildren<EdgeParty.Gameplay.Items.StunGun>();
                float range = stunGun != null ? stunGun.stunRange : 2.5f;

                UseStunGunServerRpc(origin, direction, range);

                heldItemCharges--;
                if (heldItemCharges <= 0)
                {
                    StartCoroutine(DelayedConsumeHeldItem(0.6f));
                }
                animController?.ForceTriggerAttack();
                return;
            }

            // Default punch attack:
            // Rotate character's visual orientation towards aimDirection if aiming
            Vector3 flatAimDirDefault = Vector3.ProjectOnPlane(finalAimDir, Vector3.up).normalized;
            if (flatAimDirDefault.sqrMagnitude > 0.001f && ghostRoot != null)
            {
                ghostRoot.rotation = Quaternion.LookRotation(flatAimDirDefault, Vector3.up);
            }
            animController?.TriggerAttack();
        }

        [Rpc(SendTo.Server)]
        private void UseStunGunServerRpc(Vector3 origin, Vector3 direction, float range)
        {
            Vector3 hitPoint = origin + direction * range;
            ulong targetId = 0;

            RaycastHit[] hits = Physics.SphereCastAll(origin, 0.4f, direction, range);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            Debug.Log($"[StunGun] UseStunGunServerRpc. Origin: {origin}, Dir: {direction}, Range: {range}. Total Raycast Hits: {hits.Length}");

            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger)
                {
                    continue; // Skip trigger colliders without logging
                }

                // Correct target resolution support for nested player structure
                var targetStats = hit.collider.GetComponentInParent<PlayerStats>();
                if (targetStats == null) targetStats = hit.collider.GetComponentInChildren<PlayerStats>();

                PlayerController targetController = null;
                if (targetStats != null)
                {
                    targetController = targetStats.GetComponentInChildren<PlayerController>();
                    if (targetController == null) targetController = targetStats.GetComponentInParent<PlayerController>();
                }
                else
                {
                    targetController = hit.collider.GetComponentInParent<PlayerController>();
                    if (targetController == null) targetController = hit.collider.GetComponentInChildren<PlayerController>();
                }

                // Skip if hitting self (either root child or self reference)
                if (hit.collider.transform.IsChildOf(transform.root) || targetController == this)
                {
                    continue; // Ignore self collision without logging
                }

                hitPoint = hit.point;
                Debug.Log($"[StunGun] Hit solid collider: {hit.collider.name} at {hitPoint}");

                if (targetController != null)
                {
                    if (targetController.IsSpawned && targetController.NetworkObject != null)
                        targetId = targetController.NetworkObjectId;

                    if (targetController.stats != null)
                    {
                        targetController.stats.TakeDamage(100f, direction);
                    }

                    Debug.Log($"[StunGun] Dealt 100 damage to target player {targetController.name} (ID: {targetId})");
                }
                break; // Stop at first non-shooter solid collider hit
            }

            ulong shooterId = NetworkObjectId;
            FireStunGunBeamClientRpc(origin, hitPoint, shooterId, targetId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FireStunGunBeamClientRpc(Vector3 origin, Vector3 hitPoint, ulong shooterId, ulong targetId)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(shooterId, out var shooterNetObj))
            {
                var shooterController = shooterNetObj.GetComponent<PlayerController>();
                if (shooterController == null)
                {
                    shooterController = shooterNetObj.GetComponentInChildren<PlayerController>();
                }
                if (shooterController != null)
                {
                    var stunGun = shooterController.GetComponentInChildren<EdgeParty.Gameplay.Items.StunGun>();
                    shooterController.StartCoroutine(shooterController.BeamRoutine(origin, hitPoint, shooterController.transform, stunGun));
                }
            }
        }

        private IEnumerator BeamRoutine(Vector3 origin, Vector3 hitPoint, Transform shooterTransform, EdgeParty.Gameplay.Items.StunGun stunGun)
        {
            GameObject vfxInstance = null;
            AudioClip hitSFX = null;
            LineRenderer lr = null;
            Material beamMat = null;

            // Follow the gun directly if available, otherwise fallback to shooter transform
            Transform startTransform = (stunGun != null) ? stunGun.transform : shooterTransform;

            if (stunGun != null)
            {
                if (stunGun.electricVFXPrefab != null)
                {
                    vfxInstance = Instantiate(stunGun.electricVFXPrefab, origin, Quaternion.identity);
                    vfxInstance.transform.SetParent(shooterTransform);
                    
                    // Call Play on the BaseVfx component to correctly initialize and run the beam VFX
                    var baseVfx = vfxInstance.GetComponent<PixPlays.ElementalVFX.BaseVfx>();
                    if (baseVfx == null) baseVfx = vfxInstance.GetComponentInChildren<PixPlays.ElementalVFX.BaseVfx>();
                    if (baseVfx != null)
                    {
                        var vfxData = new PixPlays.ElementalVFX.VfxData(startTransform, hitPoint, 0.5f, 1f);
                        baseVfx.Play(vfxData);
                    }

                    // Force play all particle systems in the custom VFX as fallback
                    var particleSystems = vfxInstance.GetComponentsInChildren<ParticleSystem>();
                    foreach (var ps in particleSystems)
                    {
                        ps.Play();
                    }
                }
                hitSFX = stunGun.stunHitSFX;
            }

            // Always create a purple line renderer for visual feedback of the beam connection
            GameObject beamLineGo = new GameObject("StunGunBeam_Line");
            beamLineGo.transform.SetParent(shooterTransform);
            lr = beamLineGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.12f;
            lr.endWidth = 0.12f;

            // Safe URP shader lookup to prevent NullReferenceException on material creation
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");

            if (shader != null)
            {
                beamMat = new Material(shader);
                Color purple = new Color(0.6f, 0f, 1f, 1f); // Purple
                if (beamMat.HasProperty("_BaseColor")) beamMat.SetColor("_BaseColor", purple);
                else if (beamMat.HasProperty("_Color")) beamMat.SetColor("_Color", purple);
                lr.material = beamMat;
            }

            lr.startColor = new Color(0.6f, 0f, 1f, 1f); // Purple
            lr.endColor = new Color(0.6f, 0f, 1f, 1f);

            if (hitSFX == null)
            {
                hitSFX = Resources.Load<AudioClip>("Audios/electricShock_sfx");
            }

            if (hitSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(hitSFX);

            Vector3 localOffset = startTransform != null ? startTransform.InverseTransformPoint(origin) : Vector3.zero;

            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                if (startTransform == null) break;
                
                Vector3 currentPos = startTransform.TransformPoint(localOffset);
                if (lr != null)
                {
                    lr.SetPosition(0, currentPos);
                    lr.SetPosition(1, hitPoint);
                }

                if (vfxInstance != null)
                {
                    vfxInstance.transform.position = currentPos;
                    vfxInstance.transform.LookAt(hitPoint);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (vfxInstance != null)
                Destroy(vfxInstance);
            if (beamLineGo != null)
                Destroy(beamLineGo);
            if (beamMat != null)
                Destroy(beamMat);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DespawnWeaponEffectClientRpc()
        {
            // Just destroy the visual weapon
        }

        private IEnumerator DelayedConsumeHeldItem(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (heldItemCharges <= 0 && CurrentHeldItem == WeaponPickup.ItemType.StunGun)
            {
                ConsumeHeldItem();
            }
        }

        public void OnGrabStarted_Server()
        {
            if (ForestGameManager.Instance != null && !ForestGameManager.Instance.IsMatchActive) return;
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
            if (animController == null) return;
            if (animController.IsGrabbing) return; // đã đang grab

            animController.StartGrab();

            if (_grabHandlers == null || _grabHandlers.Length == 0)
                _grabHandlers = GetComponentsInChildren<GrabHandler>();

            if (_grabHandlers != null)
                foreach (var h in _grabHandlers) h.SetActive(true);
        }

        public void OnGrabReleased_Server()
        {
            if (animController == null) return;
            animController.StopGrab();

            if (_grabHandlers == null || _grabHandlers.Length == 0)
                _grabHandlers = GetComponentsInChildren<GrabHandler>();

            if (_grabHandlers != null)
                foreach (var h in _grabHandlers) h.SetActive(false);
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
                // Không boost arm/torso khi Grab để tránh việc spring khắng ragdoll dựng đứng
                if (animController.IsAttacking)
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
        public void SetMicLevel(float value)
        {
            if (nameplateInstance != null)
            {
                nameplateInstance.SetMicLevel(value);
            }
        }

        public void SetDisplayName(string playerName)
        {
            if (nameplateInstance != null)
            {
                nameplateInstance.SetPlayerName(playerName);
            }
        }

        // ─── Dead State: Ragdoll limp / restore ───────────────────────────

        private void OnPlayerDied_Ragdoll()
        {
            _isLocallyDead = true;
            _isInputBlocked = true;

            // Dừng movement
            if (motor != null)
            {
                motor.SetMovementInput(Vector3.zero, false);
                motor.enabled = false;
            }

            if (pelvisRigidbody != null)
            {
                pelvisRigidbody.constraints = RigidbodyConstraints.None;
            }

            // Toàn bộ khớp về spring = 0 và mở khoá limit → gục tự nhiên theo hướng bị đánh
            if (_followers == null || _followers.Length == 0)
                _followers = GetComponentsInChildren<RagdollBoneFollower>();
            
            foreach (var f in _followers)
            {
                f.SetSpringMultiplier(0f);
                // Unlock limits on the root pelvis bone only, to let the player fall over naturally
                // while keeping limb joint limits intact to prevent them from crumpling/collapsing.
                if (f.gameObject.name.ToLower().Contains("pelvis"))
                {
                    f.UnlockAllLimits();
                }
            }

            Debug.Log($"[PlayerController] {playerNameSync.Value} died — ragdoll limp.");
        }

        private void OnPlayerRespawned_Ragdoll()
        {
            _isLocallyDead = false;

            if (motor != null)
            {
                motor.enabled = true;
            }

            if (pelvisRigidbody != null)
            {
                pelvisRigidbody.constraints = _originalPelvisConstraints;
            }

            // Khôi phục spring về multiplier = 1 và khôi phục các limits
            if (_followers == null || _followers.Length == 0)
                _followers = GetComponentsInChildren<RagdollBoneFollower>();
            foreach (var f in _followers)
            {
                f.SetSpringMultiplier(1f);
                f.RestoreLimits();
            }

            // Đợi thêm inputBlockAfterRespawn giây cho nhân vật đứng dậy ổn định
            StartCoroutine(UnblockInputAfterDelay(inputBlockAfterRespawn));

            Debug.Log($"[PlayerController] {playerNameSync.Value} respawned — spring restored, input blocked for {inputBlockAfterRespawn}s.");
        }

        private IEnumerator UnblockInputAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            _isInputBlocked = false;
            Debug.Log($"[PlayerController] Input unblocked.");
        }

        // ─── Server Auto-Respawn ───────────────────────────────────────────

        private IEnumerator AutoRespawnCoroutine()
        {
            yield return new WaitForSeconds(autoRespawnDelay);

            if (stats == null || !stats.IsDead.Value) yield break;  // đã hồi rồi (edge case)

            Vector3 currentPos = pelvisRigidbody != null ? pelvisRigidbody.position : transform.position;

            Vector3 origin = currentPos + Vector3.up * 2f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 10f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            Vector3 groundPoint = currentPos;
            bool foundGround = false;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform.root))
                {
                    continue; // Ignore self
                }
                groundPoint = hit.point;
                foundGround = true;
                break;
            }
            
            Vector3 pos;
            if (foundGround)
            {
                pos = groundPoint + Vector3.up * 0.2f;
            }
            else
            {
                pos = currentPos + Vector3.up * 1.5f;
            }

            bool wasFellOffMap = stats.fellOffMap; // Capture before Respawn clears it
            stats.Respawn(pos);
            Teleport(pos);
            Debug.Log($"[PlayerController] Auto-respawned {playerNameSync.Value} after {autoRespawnDelay}s at {pos}. FellOffMap={wasFellOffMap}");
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void StunPlayerClientRpc(float duration, Vector3 hitPoint)
        {
            StartCoroutine(StunRoutine(duration, hitPoint));
        }

        private IEnumerator StunRoutine(float duration, Vector3 hitPoint)
        {
            _isInputBlocked = true;
            _isLocallyDead = true;

            if (_followers == null || _followers.Length == 0)
                _followers = GetComponentsInChildren<RagdollBoneFollower>();
            foreach (var f in _followers)
                f.SetSpringMultiplier(0.15f);

            GameObject vfxInstance = SpawnBuiltinElectricVFX(transform);

            var stunSFX = Resources.Load<AudioClip>("Audios/electricShock_sfx");
            if (stunSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(stunSFX);

            yield return new WaitForSeconds(duration);

            // If we died during the stun, do NOT wake up (let the death/respawn handling take over)
            if (stats != null && stats.IsDead.Value) yield break;

            if (pelvisRigidbody != null)
            {
                // Nudge pelvis up slightly to avoid clipping through the ground when muscles stiffen
                pelvisRigidbody.position += Vector3.up * 0.25f;
            }

            _isLocallyDead = false;
            foreach (var f in _followers)
                f.SetSpringMultiplier(1f);

            if (vfxInstance != null) Destroy(vfxInstance);
            
            StartCoroutine(UnblockInputAfterDelay(1.0f));
        }

        private GameObject SpawnBuiltinElectricVFX(Transform parent)
        {
            var root = new GameObject("ElectricVFX_Auto");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 0.8f;

            var sparkSys = root.AddComponent<ParticleSystem>();
            sparkSys.Stop();
            var main = sparkSys.main;
            main.duration = 3f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.3f, 0.9f, 1f),
                new Color(1f, 1f, 0.3f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = sparkSys.emission;
            emission.rateOverTime = 80f;

            var shape = sparkSys.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f;
            if (GetParticleMaterial() != null) renderer.material = GetParticleMaterial();

            sparkSys.Play();
            return root;
        }

        // ─── Spectator Camera (local owner only) ─────────────────────────

        private void OnLocalPlayerDied()
        {
            // Chỉ xoay camera, không di chuyển
            // Camera vẫn nhìn về phía nhân vật đang nằm (không đổi target)
            // Nếu muốn spectate đối thủ thì enable dòng dưới:
            // var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            // if (cam != null) cam.target = FindLivingTeammate() ?? transform;
            Debug.Log("[PlayerController] Local player died. Camera rotation only.");
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
