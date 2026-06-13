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
        public WeaponPickup.ItemType? CurrentHeldItem { get; private set; } = null;
        public int heldItemCharges = 0;
        
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
        public float autoRespawnDelay = 5f;
        [Tooltip("Giây chờ sau khi spring phục hồi trước khi nhận phím")]
        public float inputBlockAfterRespawn = 1.5f;

        private RagdollBoneFollower[] _followers;
        private GrabHandler[] _grabHandlers;

        // Trạng thái chết cục bộ (client-side): block input di chuyển, chỉ cho xoay camera
        private bool _isLocallyDead = false;
        private bool _isInputBlocked = false;  // block sau respawn cho đến khi nhân vật đứng ổn

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

            // Dynamically attach NetworkPlayerAppearance to replicate cosmetics in multiplayer
            var appearance = GetComponentInChildren<NetworkPlayerAppearance>(true);
            if (appearance == null)
            {
                appearance = gameObject.AddComponent<NetworkPlayerAppearance>();
                Debug.Log("[PlayerController] Dynamically attached NetworkPlayerAppearance to player!");
            }
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

            if (IsOwner)
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

            if (IsOwner)
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
                if (IsOwner)
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
            CurrentHeldItem = type;
            heldItemCharges = (type == WeaponPickup.ItemType.Bomb) ? 1 : 3;
            NotifyPickupClientRpc((int)type);
            Debug.Log($"[PlayerController] {playerNameSync.Value} picked up {type}");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPickupClientRpc(int itemTypeInt)
        {
            var type = (WeaponPickup.ItemType)itemTypeInt;
            CurrentHeldItem = type;
            UpdateWeaponVisuals();
        }

        public void ConsumeHeldItem()
        {
            if (!IsServer) return;
            CurrentHeldItem = null;
            NotifyConsumeClientRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyConsumeClientRpc()
        {
            SpawnWeaponDisappearVFX();
            CurrentHeldItem = null;
            UpdateWeaponVisuals();
        }

        private void SpawnWeaponDisappearVFX()
        {
            Transform hand = FindRightHand();
            if (hand == null) return;

            var vfx = new GameObject("WeaponDisappearVFX");
            vfx.transform.position = hand.position;
            var ps = vfx.AddComponent<ParticleSystem>();
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
            emission.SetBurst(0, new ParticleSystem.Burst(0f, 20));

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var renderer = vfx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

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
            if (prefab == null) return;

            _weaponVisualInstance = Instantiate(prefab, hand);
            _weaponVisualInstance.transform.localPosition = Vector3.zero;
            _weaponVisualInstance.transform.localRotation = Quaternion.identity;

            foreach (var col in _weaponVisualInstance.GetComponentsInChildren<Collider>()) col.enabled = false;
            var rb = _weaponVisualInstance.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);
            var netObj = _weaponVisualInstance.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);
            
            var bombItem = _weaponVisualInstance.GetComponent<BombItem>();
            if (bombItem != null) Destroy(bombItem);
            var stunGun = _weaponVisualInstance.GetComponent<StunGun>();
            if (stunGun != null) Destroy(stunGun);

            if (CurrentHeldItem == WeaponPickup.ItemType.Bomb)
            {
                _weaponVisualInstance.transform.localScale = Vector3.one * 0.4f;
            }
            else
            {
                _weaponVisualInstance.transform.localScale = Vector3.one * 0.6f;
                _weaponVisualInstance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
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
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
            if (motor != null && animController != null)
            {
                motor.ApplyJump(moveDir);
                animController.TriggerJump();
            }
        }

        public void OnDashTriggered_Server()
        {
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
            if (animController == null || !animController.CanDash()) return;
            if (stats != null && !stats.SpendDashStamina()) return;   // check + deduct

            motor?.ApplyDash();
            animController.TriggerDash();
        }

        public void OnAttackTriggered_Server()
        {
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;

            if (CurrentHeldItem == WeaponPickup.ItemType.Bomb)
            {
                Vector3 spawnPos = transform.position + Vector3.up * 1.5f + transform.forward * 0.8f;
                var bombGo = Instantiate(Resources.Load<GameObject>("BombBall"), spawnPos, Quaternion.identity);
                var netObj = bombGo.GetComponent<NetworkObject>();
                netObj.Spawn();

                var bombItem = bombGo.GetComponent<BombItem>();
                if (bombItem != null)
                {
                    Vector3 throwDir = (transform.forward + Vector3.up * 0.3f).normalized;
                    bombItem.ThrowBomb(throwDir, 12f);
                }

                ConsumeHeldItem();
                return;
            }
            else if (CurrentHeldItem == WeaponPickup.ItemType.StunGun)
            {
                Vector3 origin = transform.position + Vector3.up * 1.2f;
                Vector3 direction = transform.forward;
                float stunRange = 6f;

                PlayStunGunShotEffectsClientRpc(origin, direction);

                if (Physics.Raycast(origin, direction, out RaycastHit hit, stunRange))
                {
                    var targetController = hit.collider.GetComponentInParent<PlayerController>();
                    if (targetController != null && targetController != this)
                    {
                        targetController.StunPlayerClientRpc(2.5f, hit.point);
                    }
                }

                heldItemCharges--;
                if (heldItemCharges <= 0)
                {
                    ConsumeHeldItem();
                }
                return;
            }

            animController?.TriggerAttack();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayStunGunShotEffectsClientRpc(Vector3 origin, Vector3 direction)
        {
            var beam = new GameObject("StunGunBeam");
            var lr = beam.AddComponent<LineRenderer>();
            lr.startWidth = 0.08f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.SetPositions(new Vector3[] { origin, origin + direction * 6f });
            
            lr.startColor = new Color(0.3f, 0.9f, 1f, 0.8f);
            lr.endColor = new Color(0.3f, 0.9f, 1f, 0.1f);
            lr.material = new Material(Shader.Find("Sprites/Default"));
            
            Destroy(beam, 0.15f);

            var shotSFX = Resources.Load<AudioClip>("Audios/gun_hit_sfx");
            if (shotSFX == null) shotSFX = Resources.Load<AudioClip>("Audios/electricShock_sfx");
            if (shotSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(shotSFX);
        }

        public void OnGrabTriggered_Server()
        {
            if (stats != null && stats.IsDead.Value) return;
            if (_isInputBlocked) return;
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
            motor?.SetMovementInput(Vector3.zero, false);

            // Toàn bộ khớp về spring = 0 → nằm xuống đất tự nhiên
            if (_followers == null || _followers.Length == 0)
                _followers = GetComponentsInChildren<RagdollBoneFollower>();
            foreach (var f in _followers)
                f.SetSpringMultiplier(0f);

            Debug.Log($"[PlayerController] {playerNameSync.Value} died — ragdoll limp.");
        }

        private void OnPlayerRespawned_Ragdoll()
        {
            _isLocallyDead = false;

            // Khôi phục spring về multiplier = 1
            if (_followers == null || _followers.Length == 0)
                _followers = GetComponentsInChildren<RagdollBoneFollower>();
            foreach (var f in _followers)
                f.SetSpringMultiplier(1f);

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

            Vector3 pos = SpawnManager.Instance != null
                ? SpawnManager.Instance.GetSpawnPosition(TeamID.Value)
                : transform.position + Vector3.up * 2f;

            stats.Respawn(pos);
            Teleport(pos);
            Debug.Log($"[PlayerController] Auto-respawned {playerNameSync.Value} after {autoRespawnDelay}s.");
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
