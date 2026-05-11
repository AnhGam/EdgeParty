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

        [Header("Attack Settings")]
        [Tooltip("Tổng thời gian đòn đánh (Tự động tính = Swing + Hold + Recovery)")]
        public float totalAttackDuration = 1.5f;

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
        private float _oneShotSafetyTimer;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            FindReferences();

            // PROACTIVE CORRECTION (Verified via MCP Scan):
            // The project's Animator uses Layer 0 and state name 'RightATK'.
            // We force những giá trị này để đè lên các giá trị sai trong Inspector (nếu có).
            if (atk1State == "ATK1") atk1State = "RightATK";
            if (upperBodyLayerIndex == 1) upperBodyLayerIndex = 0;
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
            _oneShotSafetyTimer = 2.0f; // 2 second safety fallback
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

            if (IsPlayingOneShot)
            {
                float oldTimer = _oneShotSafetyTimer;
                _oneShotSafetyTimer -= dt;

                // Khi kết thúc đòn đấm
                if (oldTimer > 0 && _oneShotSafetyTimer <= 0)
                {
                    IsPlayingOneShot = false;
                    // Trì hoãn việc giải phóng cơ thể 0.2s để triệt tiêu phản lực
                    Invoke(nameof(FinalizeAttackCleanup), 0.2f);
                }
            }

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
            /* --- THAY THẾ BẰNG PROCEDURAL ANIMATION ---
            bool inAir = _motor != null && !_motor.IsGrounded;
            _currentAtkState = inAir ? airAtkState : atk1State;

            _attackMirror = !_attackMirror;
            if (ghostAnimator != null)
            {
                foreach (var param in ghostAnimator.parameters)
                {
                    if (param.name == mirrorParam) 
                        ghostAnimator.SetBool(mirrorParam, _attackMirror);
                }
            }

            PlayBaseState(_currentAtkState, restart: true);
            */

            CurrentState = PlayerState.Attack;

            IsPlayingOneShot = true;

            // Tự động tính tổng thời gian dựa trên các phase để đồng bộ hóa việc khóa khớp
            totalAttackDuration = swingTime + holdTime + recoveryTime;
            _oneShotSafetyTimer = totalAttackDuration;

            _upperBodyActive = true;
            _hitboxOpen = false;

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

            /*
            SetAnimatorFloat("AttackSpeed", attackAnimSpeed);
            */
        }

        /// <summary>
        /// Simple hitbox management during attack — runs every frame in Update.
        /// </summary>
        private void UpdateAttackHitbox()
        {
            if (!_upperBodyActive || ghostAnimator == null) return;

            /*
            float duration = 0.5f;
            float elapsed = duration - _oneShotSafetyTimer;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            ManageHitbox(normalizedTime);
            */
        }

        private void StopAttack()
        {
            CloseHitbox();
            // _upperBodyActive sẽ được set false trong FinalizeAttackCleanup
            _attackCooldownTimer = attackCooldown;
        }

        private void FinalizeAttackCleanup()
        {
            _upperBodyActive = false;

            // KHÔI PHỤC GIỚI HẠN VÀ SPRING CHO CÁNH TAY
            var ragdollRoot = transform.root;
            var allJoints = ragdollRoot.GetComponentsInChildren<ConfigurableJoint>();
            foreach (var j in allJoints)
            {
                string n = j.name.ToLower();
                if (n == "upperarm_l")
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
                    else if (_oneShotSafetyTimer <= 0f)
                    {
                        // Fallback: If we waited 0.2s and still didn't enter the state, something is wrong, release lock.
                        oneShotDone = true;
                    }
                }

                if (landedFromJump || oneShotDone)
                {
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
            // Nếu là đòn Dash hoặc Jump, ta giữ nguyên animation của chúng
            bool isSpecialAction = IsPlayingOneShot && (CurrentState == PlayerState.Dash || CurrentState == PlayerState.InAir);
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
                PlayerState.Attack => idleState, // Khi đấm procedural, giữ Animator ở Idle
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

        // ─── Procedural Attack Animation ─────────────────────────────────
        private void LateUpdate()
        {
            if (!_upperBodyActive || ghostAnimator == null) return;

            Transform leftUpperArm = ghostAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            if (leftUpperArm != null)
            {
                // TỔNG THỜI GIAN: Được tính động từ các phase
                float elapsed = totalAttackDuration - _oneShotSafetyTimer;

                float progress = 0f;

                if (elapsed <= swingTime)
                {
                    // GIAI ĐOẠN 1: VUNG ĐẤM
                    progress = Mathf.Clamp01(elapsed / swingTime);
                }
                else if (elapsed <= swingTime + holdTime)
                {
                    // GIAI ĐOẠN 2: GIỮ THẾ (STAY AT MAX)
                    progress = 1.0f;
                }
                else
                {
                    // GIAI ĐOẠN 3: THU TAY VỀ (RECOVERY)
                    float recoveryElapsed = elapsed - (swingTime + holdTime);
                    float recoveryT = Mathf.Clamp01(recoveryElapsed / recoveryTime);
                    // Thu về từ 1.0 về 0.0
                    progress = 1.0f - Mathf.SmoothStep(0f, 1f, recoveryT);

                    // Trong lúc thu tay, ta cũng giảm dần lực Spring về bình thường
                    if (_cachedFollowers != null)
                    {
                        foreach (var f in _cachedFollowers)
                        {
                            if (f.name.ToLower() == "upperarm_l")
                                f.SetSpringMultiplier(Mathf.Lerp(10f, 1f, recoveryT));
                        }
                    }
                }

                float t = Mathf.SmoothStep(0f, 1f, progress);

                float zRotVal = Mathf.Lerp(0f, targetPunchZ, t);

                // Trục X (Quật ngang) chỉ bắt đầu chạy khi tiến trình vượt qua hookStartThreshold
                float xProgress = Mathf.Clamp01((progress - hookStartThreshold) / (1f - hookStartThreshold));
                float xT = Mathf.SmoothStep(0f, 1f, xProgress);
                float xRotVal = Mathf.Lerp(0f, targetPunchX, xT);

                float yRotVal = Mathf.Lerp(0f, targetPunchY, t);

                Quaternion yRot = Quaternion.AngleAxis(yRotVal, Vector3.up);
                Quaternion xRot = Quaternion.AngleAxis(xRotVal, Vector3.right);
                Quaternion zRot = Quaternion.AngleAxis(zRotVal, Vector3.forward);

                // CƠ CHẾ CỘNG DỒN (ADDITIVE): 
                // Ta lấy rotation HIỆN TẠI của Idle (đã có breathing/motion) 
                // rồi nhân thêm (offset) góc đấm vào.
                // Khi progress về 0, nó sẽ tự động là Idle thuần túy.
                leftUpperArm.localRotation *= Quaternion.Slerp(Quaternion.identity, yRot * xRot * zRot, t);
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