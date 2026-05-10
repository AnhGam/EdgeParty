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
        public float attackDuration = 1.5f; 
        
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
        private float _idleTimer; // Timer cho trạng thái Idle sau 10s
        private float _attackCooldownTimer;
        private bool _dashMirror;
        private bool _attackMirror;
        private float _oneShotSafetyTimer;
        private Quaternion _baseLeftArmRot; // Cached at start of attack

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
                    _upperBodyActive = false;
                    
                    // KHÔI PHỤC GIỚI HẠN VÀ SPRING
                    var ragdollRoot = transform.root;
                    var allJoints = ragdollRoot.GetComponentsInChildren<ConfigurableJoint>();
                    foreach(var j in allJoints) {
                        string n = j.name.ToLower();
                        if(n.Contains("upperarm_l")) {
                            j.angularXMotion = ConfigurableJointMotion.Limited;
                            j.angularYMotion = ConfigurableJointMotion.Limited;
                            j.angularZMotion = ConfigurableJointMotion.Limited;
                        }
                        else if (n.Contains("pelvis")) {
                            // Khôi phục Pelvis về Free (hoặc trạng thái mặc định của bạn)
                            j.angularXMotion = ConfigurableJointMotion.Free;
                        }
                        else if (n.Contains("spine")) {
                            // Khôi phục Spine về Limited
                            j.angularYMotion = ConfigurableJointMotion.Limited;
                        }
                    }

                    // Trả spring về bình thường
                    if (_cachedFollowers != null)
                    {
                        foreach(var f in _cachedFollowers) f.SetSpringMultiplier(1f);
                    }
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
            _idleTimer = 0f; 
            
            // Dùng attackDuration thay vì hằng số 0.5s
            _oneShotSafetyTimer = attackDuration; 
            _upperBodyActive = true;
            _hitboxOpen = false;

            // LẤY BASE TỪ VẬT LÝ VÀ BOOST SPRING
            if (ghostAnimator != null)
            {
                var ragdollRoot = transform.root;
                
                // Boost Spring tay trái lên gấp 10 lần để vung cho mạnh
                if (_cachedFollowers == null) _cachedFollowers = ragdollRoot.GetComponentsInChildren<RagdollBoneFollower>();
                foreach(var f in _cachedFollowers) {
                    if (f.name.ToLower().Contains("upperarm_l")) f.SetSpringMultiplier(10f);
                }

                // Tìm xương tay trái vật lý
                var leftArmPhys = ragdollRoot.GetComponentsInChildren<Rigidbody>()
                    .FirstOrDefault(r => r.name.ToLower().Contains("upperarm_l"));
                
                if (leftArmPhys != null) {
                    _baseLeftArmRot = leftArmPhys.transform.localRotation;
                } else {
                    Transform leftGhost = ghostAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    if (leftGhost != null) _baseLeftArmRot = leftGhost.localRotation;
                }
                
                // KHÓA CỨNG CƠ THỂ VÀ MỞ TUNG CÁNH TAY
                var allJoints = ragdollRoot.GetComponentsInChildren<ConfigurableJoint>();
                foreach(var j in allJoints) {
                    string n = j.name.ToLower();
                    if(n.Contains("upperarm_l")) {
                        j.angularXMotion = ConfigurableJointMotion.Free;
                        j.angularYMotion = ConfigurableJointMotion.Free;
                        j.angularZMotion = ConfigurableJointMotion.Free;
                    }
                    else if (n.Contains("pelvis")) {
                        // Giới hạn Pelvis X để không bị ngửa ra sau (-5 đến 5 độ)
                        j.angularXMotion = ConfigurableJointMotion.Limited;
                        var limit = j.highAngularXLimit; limit.limit = 5f; j.highAngularXLimit = limit;
                        limit = j.lowAngularXLimit; limit.limit = -5f; j.lowAngularXLimit = limit;
                    }
                    else if (n.Contains("spine")) {
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
            _upperBodyActive = false;
            _attackCooldownTimer = attackCooldown;
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
                _idleTimer = 0f; // Reset timer khi di chuyển
            }
            else
            {
                _idleTimer += Time.deltaTime;
                // Nếu đứng yên quá 10s thì mới vào Idle, còn không thì ở None (mặc định)
                if (_idleTimer >= 10f)
                    CurrentState = PlayerState.Idle;
                else
                    CurrentState = PlayerState.None; // PlayerState.None needs to be added to enum
            }
        }

        private void UpdateBaseAnimator()
        {
            if (IsPlayingOneShot)
            {
                // Khi đang chơi đòn đặc biệt (đấm/nhảy/dash), ta PHẢI kết nối lại với Ghost
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
                _ => "None"
            };

            PlayBaseState(target);

            // XỬ LÝ TƯ THẾ TỰ NHIÊN CHO TRẠNG THÁI NONE
            bool isNone = target == "None";
            if (_cachedFollowers == null) _cachedFollowers = transform.root.GetComponentsInChildren<RagdollBoneFollower>();
            
            foreach(var f in _cachedFollowers)
            {
                // Nếu là None, ép xương về tư thế mặc định của Prefab, bỏ qua Animator
                f.SetNaturalPose(isNone);
                
                // Giảm nhẹ lực lò xo ở trạng thái None để tay rũ xuống tự nhiên hơn (0.6x thay vì 1x)
                if (isNone) f.SetSpringMultiplier(0.6f);
                else f.SetSpringMultiplier(1f);
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
                // TỔNG THỜI GIAN: attackDuration (tổng 3 phase phải khớp với attackDuration)
                float elapsed = attackDuration - _oneShotSafetyTimer;
                
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
                    if (_cachedFollowers != null) {
                        foreach(var f in _cachedFollowers) {
                            if (f.name.ToLower().Contains("upperarm_l")) 
                                f.SetSpringMultiplier(Mathf.Lerp(10f, 1f, recoveryT));
                        }
                    }
                }
                
                float t = Mathf.SmoothStep(0f, 1f, progress);

                // Dùng các thông số từ Inspector
                float zRotVal = Mathf.Lerp(0f, targetPunchZ, t);
                
                // Trục X (Quật ngang) chỉ bắt đầu chạy khi tiến trình vượt qua hookStartThreshold
                float xProgress = Mathf.Clamp01((progress - hookStartThreshold) / (1f - hookStartThreshold));
                float xT = Mathf.SmoothStep(0f, 1f, xProgress);
                float xRotVal = Mathf.Lerp(0f, targetPunchX, xT);

                float yRotVal = Mathf.Lerp(0f, targetPunchY, t);

                Quaternion zRot = Quaternion.AngleAxis(zRotVal, Vector3.forward);
                Quaternion xRot = Quaternion.AngleAxis(xRotVal, Vector3.right);
                Quaternion yRot = Quaternion.AngleAxis(yRotVal, Vector3.up);

                // Ghi đè rotation của xương ghost
                leftUpperArm.localRotation = _baseLeftArmRot * zRot * xRot * yRot;
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