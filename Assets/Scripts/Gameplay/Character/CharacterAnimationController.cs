using UnityEngine;
using System.Collections;
using System.Linq;

namespace EdgeParty.Gameplay.Character
{
    public enum PlayerState { None, Idle, Walk, Run, Attack, Dash, InAir }

    /// <summary>
    /// Drives the Ghost animator using two layers:
    ///   Layer 0 – Base Layer  : full-body locomotion (Idle, Walk, Run, Jump, Dash, InAir)
    ///   Layer 1 – UpperBody   : upper-body attack overlay (ATK1, ATK2, ATK3 combo)
    ///
    /// The UpperBody layer must exist in MonkeyGameplay.controller with:
    ///   • BlendingMode = Override (or Additive if you prefer)
    ///   • AvatarMask   = UpperBodyMask  (spine_01 + everything above)
    ///   • States       : ATK1, ATK2, ATK3, AirATK  (+ empty "None" default state)
    ///   • Default weight = 0  (we set it to 1 when attacking)
    ///
    /// Requires: PlayerStats on same prefab hierarchy (for stamina checks + hitbox calls).
    /// </summary>
    public class CharacterAnimationController : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────
        [Header("References")]
        public Animator ghostAnimator;
        public Transform ghostRoot;

        [Header("Layer Indices")]
        public int baseLayerIndex = 0;
        public int upperBodyLayerIndex = 0;   // All states are in Base Layer 0 in this project

        [Header("Base Layer State Names")]
        public string idleState = "IdleA";
        public string walkState = "Walk";
        public string walkLState = "WalkL";
        public string walkRState = "WalkR";
        public string runState = "Run";
        public string runLState = "RunL";
        public string runRState = "RunR";
        public string jumpState = "Jump";
        public string dashState = "Dash";
        public string inAirState = "None";

        [Header("Upper Body Attack State Names")]
        public string atk1State = "RightATK";
        public string airAtkState = "AirATK";

        [Header("Test Animation Override")]
        [Tooltip("Kéo thả bất kỳ Animation Clip nào vào đây (ví dụ trong thư mục Pspsps Animations) để test nhanh đòn đấm mà không cần mở Animator Controller!")]
        public AnimationClip testAttackClip;

        [Header("Attack Settings")]
        [Tooltip("Tổng thời gian đòn đánh")]
        public float totalAttackDuration = 1.5f;

        /* --- PROCEDURAL TUNING COMMENTED OUT ---
        [Header("Attack Tuning (Procedural)")]
        [Range(-180f, 180f)] public float targetPunchZ = -90f;
        [Range(-180f, 180f)] public float targetPunchX = -130f;
        [Range(-180f, 180f)] public float targetPunchY = -30f;

        [Space]
        public float swingTime = 0.8f;      // Thời gian vung ra
        public float holdTime = 0.2f;       // Thời gian giữ tư thế đấm
        public float recoveryTime = 0.5f;   // Thời gian thu tay về mượt mà

        [Range(0f, 1f)]
        public float hookStartThreshold = 0.6f; // Chỉ bắt đầu quật X khi vung Z đã đạt 60%
        */

        [Header("Combo Settings")]
        [Tooltip("Window (0‒1 normalizedTime) within which the next punch input queues")]
        public float comboInputWindow = 0.45f;
        [Tooltip("normalizedTime at which the hitbox activates (arm swings through)")]
        public float hitboxStartTime = 0.25f;
        [Tooltip("normalizedTime at which the hitbox deactivates")]
        public float hitboxEndTime = 0.65f;
        [Tooltip("Speed multiplier for the upper-body attack layer")]
        public float attackAnimSpeed = 1.1f;
        [Tooltip("Cooldown AFTER a full combo before attacking again")]
        public float attackCooldown = 0.4f;

        [Header("Dash Settings")]
        public float dashCooldown = 1.5f;
        public float dashAnimSpeed = 1.2f;
        public string mirrorParam = "Mirror";
        public string dashSpeedParam = "DashSpeed";

        [Header("Upper Body Layer Blend")]
        [Tooltip("How fast the upper-body layer weight blends in/out")]
        public float upperBodyBlendSpeed = 8f;

        // ─── Punch hitboxes (assign both fist RagdollBoneFollower GOs) ────
        [Header("Punch Hitboxes")]
        [Tooltip("PunchHitbox component on the right-hand physics bone")]
        public PunchHitbox rightFistHitbox;
        [Tooltip("PunchHitbox component on the left-hand physics bone")]
        public PunchHitbox leftFistHitbox;

        // ─── Public state ─────────────────────────────────────────────────
        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public bool IsPlayingOneShot { get; private set; }
        public bool IsAttacking => _upperBodyActive;
        public bool AttackMirror => _attackMirror;
        public bool CanAttack() => _attackCooldownTimer <= 0f && !_isDead;
        public bool CanDash() => _dashTimer <= 0f && !_isDead;

        // ─── Private ──────────────────────────────────────────────────────
        private CharacterMotor _motor;
        private PlayerStats _stats;

        // Locomotion
        private Vector3 _moveDir;
        private bool _isRunning;
        private string _activeBaseState;
        private bool _isDead;

        // Upper-body attack
        private bool _upperBodyActive;
        private float _upperBodyWeight;
        private int _comboStep;           // 0=none,1=ATK1,2=ATK2,3=ATK3
        private bool _comboQueued;
        private string _currentAtkState;
        private bool _hitboxOpen;
        private bool _attackStateEntered;

        private float _dashTimer;
        private float _attackCooldownTimer;
        private bool _dashMirror;
        private bool _attackMirror;
        private Quaternion _baseLowerArmRot; // Lưu tư thế cẳng tay để khóa cứng khi đấm
        private AnimatorOverrideController _overrideController;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            FindReferences();

            // PROACTIVE CORRECTION (Verified via MCP Scan):
            // The project's Animator uses Layer 0 and state name 'RightATK'.
            // We force những giá trị này để đè lên các giá trị sai trong Inspector (nếu có).
            atk1State = "RightATK";
            airAtkState = "AirATK";
            if (upperBodyLayerIndex == 1) upperBodyLayerIndex = 0;
            Debug.Log($"[EdgeParty Attack Debug] Awake completed! forced atk1State={atk1State}, airAtkState={airAtkState}, upperBodyLayerIndex={upperBodyLayerIndex}");
        }

        private void FindReferences()
        {
            _motor = GetComponentInParent<CharacterMotor>();
            if (_motor == null) _motor = GetComponent<CharacterMotor>();
            if (_motor == null)
            {
                var ctrl = GetComponentInParent<PlayerController>();
                if (ctrl != null) _motor = ctrl.motor;
            }

            _stats = GetComponentInParent<PlayerStats>();
            if (_stats == null) _stats = GetComponentInChildren<PlayerStats>();
        }

        // ─── Public input API (called by PlayerController on server) ──────

        public void SetMovementInput(Vector3 moveDir, bool isRunning)
        {
            _moveDir = moveDir;
            _isRunning = isRunning;
        }

        public void TriggerJump()
        {
            PlayBaseState(jumpState, restart: true);
            CurrentState = PlayerState.InAir;
            IsPlayingOneShot = true;
        }

        public void TriggerDash()
        {
            if (!CanDash()) return;
            _dashTimer = dashCooldown;

            // Toggle mirroring for alternating sides
            _dashMirror = !_dashMirror;
            if (ghostAnimator != null)
            {
                // Safety: check if parameters exist to avoid warnings if user hasn't added them yet
                foreach (var param in ghostAnimator.parameters)
                {
                    if (param.name == mirrorParam) ghostAnimator.SetBool(mirrorParam, _dashMirror);
                    if (param.name == dashSpeedParam) ghostAnimator.SetFloat(dashSpeedParam, dashAnimSpeed);
                }
            }

            PlayBaseState(dashState, restart: true);
            CurrentState = PlayerState.Dash;
            IsPlayingOneShot = true;
        }

        /// <summary>
        /// Begin or queue the next punch in a combo.
        /// Returns false if stamina is insufficient.
        /// </summary>
        public bool TriggerAttack()
        {
            if (_isDead) return false;
            if (_stats != null && !_stats.HasStaminaForAttack) return false;

            if (!_upperBodyActive)
            {
                if (_stats != null) _stats.SpendAttackStamina();
                StartAttack();
                return true;
            }
            return false;
        }

        // ─── Main update ──────────────────────────────────────────────────
        private void Update()
        {
            float dt = Time.deltaTime;

            _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - dt);
            _dashTimer = Mathf.Max(0f, _dashTimer - dt);



            DetermineBaseState();
            UpdateBaseAnimator();
            UpdateAttackHitbox();
            ApplySpeedMultiplier();
        }

        private bool IsServerActive()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || nm.IsServer;
        }

        // ─── Attack (simple: same as Dash, just PlayBaseState on Layer 0) ─

        private void StartAttack()
        {
            bool inAir = _motor != null && !_motor.IsGrounded;

            // Luôn luôn đấm tay phải (animation gốc) để test 1 tay đơn giản nhất theo yêu cầu
            _attackMirror = false;
            _currentAtkState = inAir ? airAtkState : atk1State;

            ApplyTestAttackClip();

            PlayBaseState(_currentAtkState, restart: true);

            CurrentState = PlayerState.Attack;
            IsPlayingOneShot = true;

            Debug.Log($"[EdgeParty Attack Debug] StartAttack called! _currentAtkState={_currentAtkState}, _attackMirror={_attackMirror}");

            _upperBodyActive = true;
            _hitboxOpen = false;

            /* --- PROCEDURAL JOINTS & SPRINGS (COMMENTED OUT) ---
            // CHỤP TƯ THẾ CẲNG TAY HIỆN TẠI ĐỂ KHÓA CỨNG
            if (ghostAnimator != null)
            {
                Transform lowerGhost = ghostAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                if (lowerGhost != null) _baseLowerArmRot = lowerGhost.localRotation;
            }

            // Tự động tính tổng thời gian dựa trên các phase để đồng bộ hóa việc khóa khớp
            totalAttackDuration = swingTime + holdTime + recoveryTime;

            if (ghostAnimator != null)
            {
                var ragdollRoot = transform.root;
                // KHÓA CỨNG CƠ THỂ VÀ MỞ TUNG CÁNH TAY
                var allJoints = ragdollRoot.GetComponentsInChildren<ConfigurableJoint>();
                foreach (var j in allJoints)
                {
                    string n = j.name.ToLower();
                    if (n == "upperarm_l")
                    {
                        j.angularXMotion = ConfigurableJointMotion.Free;
                        j.angularYMotion = ConfigurableJointMotion.Free;
                        j.angularZMotion = ConfigurableJointMotion.Free;
                    }
                    else if (n == "lowerarm_l")
                    {
                        // KHÓA CỨNG KHỚP KHUỶU TAY để triệt tiêu lực vẩy
                        j.angularXMotion = ConfigurableJointMotion.Locked;
                        j.angularYMotion = ConfigurableJointMotion.Locked;
                        j.angularZMotion = ConfigurableJointMotion.Locked;
                    }
                    else if (n.Contains("pelvis"))
                    {
                        // Giới hạn Pelvis X để không bị ngửa ra sau (-5 đến 5 độ)
                        j.angularXMotion = ConfigurableJointMotion.Limited;
                        var limit = j.highAngularXLimit; limit.limit = 5f; j.highAngularXLimit = limit;
                        limit = j.lowAngularXLimit; limit.limit = -5f; j.lowAngularXLimit = limit;
                    }
                    else if (n.Contains("spine"))
                    {
                        // Khóa Angular Y của Spine để không bị xoay người quá đà gây nghiêng
                        j.angularYMotion = ConfigurableJointMotion.Locked;
                    }
                }
            }
            */
        }

        /// <summary>
        /// Simple hitbox management during attack — runs every frame in Update.
        /// </summary>
        private void UpdateAttackHitbox()
        {
            if (!_upperBodyActive || ghostAnimator == null) return;

            var info = ghostAnimator.GetCurrentAnimatorStateInfo(baseLayerIndex);
            if (info.IsName(atk1State) || info.IsName(airAtkState))
            {
                ManageHitbox(info.normalizedTime);
            }
        }

        private void StopAttack()
        {
            CloseHitbox();
            _attackCooldownTimer = attackCooldown;

            // Đảm bảo huỷ các lịch trình dọn dẹp cũ và xếp lịch dọn dẹp mới để giải phóng _upperBodyActive = false
            CancelInvoke(nameof(FinalizeAttackCleanup));
            Invoke(nameof(FinalizeAttackCleanup), 0.2f);
            Debug.Log("[EdgeParty Attack Debug] StopAttack called! Scheduled FinalizeAttackCleanup in 0.2s.");
        }

        private void FinalizeAttackCleanup()
        {
            _upperBodyActive = false;

            /* --- PROCEDURAL CLEANUP (COMMENTED OUT) ---
            // KHÔI PHỤC GIỚI HẠN VÀ SPRING CHO CÁNH TAY
            var ragdollRoot = transform.root;
            var allJoints = ragdollRoot.GetComponentsInChildren<ConfigurableJoint>();
            foreach (var j in allJoints)
            {
                string n = j.name.ToLower();
                if (n == "upperarm_l" || n == "lowerarm_l")
                {
                    j.angularXMotion = ConfigurableJointMotion.Limited;
                    j.angularYMotion = ConfigurableJointMotion.Limited;
                    j.angularZMotion = ConfigurableJointMotion.Limited;
                }
                else if (n.Contains("pelvis"))
                {
                    // Khôi phục Pelvis về trạng thái vật lý bình thường
                    j.angularXMotion = ConfigurableJointMotion.Free;
                }
                else if (n.Contains("spine"))
                {
                    j.angularYMotion = ConfigurableJointMotion.Limited;
                }
            }

            // Trả spring về bình thường
            if (_cachedFollowers != null)
            {
                foreach (var f in _cachedFollowers) f.SetSpringMultiplier(1f);
            }
            */
        }

        private void ManageHitbox(float normalizedTime)
        {
            bool shouldBeOpen = normalizedTime >= hitboxStartTime && normalizedTime < hitboxEndTime;

            if (shouldBeOpen && !_hitboxOpen)
            {
                OpenHitbox();
            }
            else if (!shouldBeOpen && _hitboxOpen)
            {
                CloseHitbox();
            }
        }

        private void OpenHitbox()
        {
            _hitboxOpen = true;
            var pelvis = _motor != null ? _motor.pelvisRigidbody : null;

            // Right or left fist alternates per combo step
            bool useRight = (_comboStep % 2 != 0); // step 1,3 = right; step 2 = left
            PunchHitbox fist = useRight ? rightFistHitbox : leftFistHitbox;

            if (fist != null)
                fist.Activate(pelvis, _stats);
        }

        private void CloseHitbox()
        {
            if (!_hitboxOpen) return;
            _hitboxOpen = false;
            if (rightFistHitbox != null) rightFistHitbox.Deactivate();
            if (leftFistHitbox != null) leftFistHitbox.Deactivate();
        }

        // ─── Base locomotion state machine ────────────────────────────────

        private void DetermineBaseState()
        {
            if (IsPlayingOneShot)
            {
                bool landedFromJump = CurrentState == PlayerState.InAir
                                      && _motor != null && _motor.IsGrounded;

                bool oneShotDone = false;

                if (ghostAnimator != null)
                {
                    var info = ghostAnimator.GetCurrentAnimatorStateInfo(baseLayerIndex);

                    // NEW: Strict state checking. 
                    // Only consider the action "done" if we are actually IN the state and it's finished,
                    // OR if the safety timer has completely expired and we aren't even in the state yet (stuck).
                    bool inActionState = info.IsName(dashState) || info.IsName(jumpState) ||
                                        info.IsName(atk1State) || info.IsName(airAtkState);

                    if (inActionState)
                    {
                        oneShotDone = (info.normalizedTime >= 0.95f);
                    }

                    if (CurrentState == PlayerState.Attack)
                    {
                        Debug.Log($"[EdgeParty Attack Debug] DetermineBaseState: IsPlayingOneShot=true, CurrentState={CurrentState}, inActionState={inActionState}, stateName={info.fullPathHash}, normalizedTime={info.normalizedTime}, oneShotDone={oneShotDone}");
                    }
                }

                if (landedFromJump || oneShotDone)
                {
                    Debug.Log($"[EdgeParty Attack Debug] ONE-SHOT FINISHED! landedFromJump={landedFromJump}, oneShotDone={oneShotDone}. Resetting IsPlayingOneShot=false.");
                    IsPlayingOneShot = false;
                    if (_upperBodyActive) StopAttack();
                }
                else
                {
                    // Stay locked in one-shot mode
                    return;
                }
            }

            if (_motor != null && !_motor.IsGrounded)
            {
                CurrentState = PlayerState.InAir;
                return;
            }

            if (_moveDir.sqrMagnitude > 0.01f)
            {
                CurrentState = _isRunning ? PlayerState.Run : PlayerState.Walk;
            }
            else
            {
                CurrentState = PlayerState.Idle;
            }
        }

        private void UpdateBaseAnimator()
        {
            // Nếu là đòn Dash, Jump hoặc Attack, ta giữ nguyên animation của chúng
            bool isSpecialAction = IsPlayingOneShot && (CurrentState == PlayerState.Dash || CurrentState == PlayerState.InAir || CurrentState == PlayerState.Attack);
            if (isSpecialAction)
            {
                if (_cachedFollowers != null)
                {
                    foreach (var f in _cachedFollowers) f.SetNaturalPose(false);
                }
                return;
            }

            string target = CurrentState switch
            {
                PlayerState.Walk => GetDirectionalState(walkState, walkLState, walkRState),
                PlayerState.Run => GetDirectionalState(runState, runLState, runRState),
                PlayerState.InAir => inAirState,
                PlayerState.Idle => idleState,
                PlayerState.Attack => _currentAtkState, 
                _ => idleState
            };

            PlayBaseState(target);

            // LUÔN LUÔN THEO ANIMATOR (Full Idle) - Bỏ Natural Pose
            if (_cachedFollowers == null) _cachedFollowers = transform.root.GetComponentsInChildren<RagdollBoneFollower>();

            foreach (var f in _cachedFollowers)
            {
                f.SetNaturalPose(false);
                f.SetSpringMultiplier(1f);
            }
        }

        private RagdollBoneFollower[] _cachedFollowers;

        private string GetDirectionalState(string center, string left, string right)
        {
            if (ghostRoot == null) return center;
            float dot = Vector3.Dot(_moveDir, ghostRoot.right);
            if (dot > 0.65f) return right;
            if (dot < -0.65f) return left;
            return center;
        }

        // ─── Apply speed multiplier to animator ───────────────────────────
        private void ApplySpeedMultiplier()
        {
            if (ghostAnimator == null || _stats == null) return;
            ghostAnimator.speed = _stats.speedMultiplier;
        }

        // ─── Animator helpers ─────────────────────────────────────────────
        private void PlayBaseState(string stateName, bool restart = false)
        {
            if (ghostAnimator == null) return;
            if (!restart && _activeBaseState == stateName) return;
            ghostAnimator.Play(stateName, baseLayerIndex, restart ? 0f : -1f);
            _activeBaseState = stateName;
        }

        private void PlayUpperBodyState(string stateName)
        {
            if (ghostAnimator == null) return;

            // EMERGENCY FALLBACK (Verified via MCP Scan):
            // If the Inspector is still sending 'ATK1', we force it to 'RightATK'.
            if (stateName == "ATK1")
            {
                atk1State = "RightATK";
                stateName = "RightATK";
            }

            if (upperBodyLayerIndex == 1) upperBodyLayerIndex = 0;

            if (upperBodyLayerIndex < ghostAnimator.layerCount)
            {
                ghostAnimator.SetLayerWeight(upperBodyLayerIndex, 1f);
                ghostAnimator.Play(stateName, upperBodyLayerIndex, 0f);
            }
            else
            {
                // Fallback to base layer if upper body layer is missing
                ghostAnimator.Play(stateName, baseLayerIndex, 0f);
            }

            // Set AttackSpeed safely
            SetAnimatorFloat("AttackSpeed", attackAnimSpeed);
        }

        private void SetAnimatorFloat(string paramName, float value)
        {
            if (ghostAnimator == null) return;
            foreach (var p in ghostAnimator.parameters)
            {
                if (p.name == paramName)
                {
                    ghostAnimator.SetFloat(paramName, value);
                    return;
                }
            }
        }

        // LateUpdate and SwapGhostArmBones removed to handle mirroring cleanly on physical ragdoll bone followers without polluting ghost transforms.

        /// <summary>
        /// Áp dụng AnimationClip tùy biến được kéo thả từ Inspector vào để ghi đè đòn đánh mặc định lúc runtime.
        /// </summary>
        private void ApplyTestAttackClip()
        {
            if (ghostAnimator == null) return;

            if (testAttackClip == null)
            {
                // Nếu testAttackClip trống và ta đang có override controller, ta restore về mặc định
                if (_overrideController != null)
                {
                    var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
                    _overrideController.GetOverrides(overrides);
                    for (int i = 0; i < overrides.Count; i++)
                    {
                        if (overrides[i].Key.name.Contains("ATK1") && overrides[i].Value != null)
                        {
                            overrides[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, null);
                            _overrideController.ApplyOverrides(overrides);
                            Debug.Log("[EdgeParty Attack Test] Restored default attack clip.");
                            break;
                        }
                    }
                }
                return;
            }

            // Tạo mới AnimatorOverrideController nếu chưa có
            if (_overrideController == null)
            {
                _overrideController = new AnimatorOverrideController(ghostAnimator.runtimeAnimatorController);
                ghostAnimator.runtimeAnimatorController = _overrideController;
            }

            var overridesList = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
            _overrideController.GetOverrides(overridesList);

            for (int i = 0; i < overridesList.Count; i++)
            {
                var originalClip = overridesList[i].Key;
                if (originalClip.name.Contains("ATK1"))
                {
                    if (overridesList[i].Value != testAttackClip)
                    {
                        overridesList[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(originalClip, testAttackClip);
                        _overrideController.ApplyOverrides(overridesList);
                        Debug.Log($"[EdgeParty Attack Test] Successfully overrode default attack clip with: {testAttackClip.name}");
                    }
                    break;
                }
            }
        }

        // ─── Called by PlayerStats when dead ─────────────────────────────
        public void OnDeath()
        {
            _isDead = true;
            _upperBodyActive = false;
            CloseHitbox();
        }

        public void OnRespawn()
        {
            _isDead = false;
        }
    }
}