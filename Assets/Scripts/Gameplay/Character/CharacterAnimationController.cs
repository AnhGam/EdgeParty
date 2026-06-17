using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    public enum PlayerState { None, Idle, Walk, Run, Attack, Dash, InAir, Grab }

    /// <summary>
    /// Drives the Ghost animator locomotion states and attack hitboxes.
    /// Simplified and cleaned version: no procedural blending or test overrides.
    ///
    /// Grab design:
    ///   _isGrabbing  = cờ PHYSICS/LOGIC (GrabHandler active hay không)
    ///   CurrentState = trạng thái ANIMATION (có thể là Walk/Run ngay cả khi _isGrabbing = true)
    ///   → Cho phép locomotion animation chạy bình thường khi đang grab + di chuyển.
    /// </summary>
    public class CharacterAnimationController : MonoBehaviour
    {
        [Header("References")]
        public Animator ghostAnimator;
        public Transform ghostRoot;

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
        public string grabState = "Grab";

        [Header("Attack State Names")]
        public string atk1State = "RightATK";
        public string airAtkState = "AirATK";

        [Header("Attack Settings")]
        public float totalAttackDuration = 1.5f;
        public float hitboxStartTime = 0.25f;
        public float hitboxEndTime = 0.65f;
        public float attackAnimSpeed = 1.1f;
        public float attackCooldown = 3.0f;

        [Header("Dash Settings")]
        public float dashCooldown = 1.5f;
        public float dashAnimSpeed = 1.2f;
        public string mirrorParam = "Mirror";
        public string dashSpeedParam = "DashSpeed";

        [Header("Punch Hitboxes")]
        public PunchHitbox rightFistHitbox;
        public PunchHitbox leftFistHitbox;

        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public bool IsPlayingOneShot { get; private set; }

        // _isGrabbing là cờ PHYSICS: true khi người chơi đang giữ chuột trái.
        // Tách biệt hoàn toàn với CurrentState để locomotion animation không bị block.
        private bool _isGrabbing;
        public bool IsGrabbing => _isGrabbing;

        public bool IsAttacking => _upperBodyActive;
        public bool AttackMirror => _attackMirror;
        public bool CanAttack() => _attackCooldownTimer <= 0f && !_isDead;
        public bool CanDash() => _dashTimer <= 0f && !_isDead;

        private CharacterMotor _motor;
        private PlayerStats _stats;

        private Vector3 _moveDir;
        private bool _isRunning;
        private string _activeBaseState;
        private bool _isDead;

        private bool _upperBodyActive;
        private string _currentAtkState;
        private bool _hitboxOpen;

        private float _dashTimer;
        private float _attackCooldownTimer;
        private bool _dashMirror;
        private bool _attackMirror;
        private bool _nextAttackIsLeft;
        private RagdollBoneFollower[] _cachedFollowers;

        private void Awake()
        {
            FindReferences();
            atk1State = "RightATK";
            airAtkState = "AirATK";
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

            if (rightFistHitbox == null || leftFistHitbox == null)
            {
                var hitboxes = transform.root.GetComponentsInChildren<PunchHitbox>();
                foreach (var h in hitboxes)
                {
                    string name = h.gameObject.name.ToLower();
                    if (name.Contains("_r") || name.Contains("right")) rightFistHitbox = h;
                    else if (name.Contains("_l") || name.Contains("left")) leftFistHitbox = h;
                }
            }
        }

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

            _dashMirror = !_dashMirror;
            if (ghostAnimator != null)
            {
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

        /// <summary>
        /// Kích hoạt hoạt ảnh tấn công trực tiếp (dùng cho vũ khí/bom) mà không cần kiểm tra stamina hay tiêu tốn stamina.
        /// </summary>
        public void ForceTriggerAttack()
        {
            if (_isDead) return;
            if (!_upperBodyActive)
            {
                StartAttack();
            }
        }

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

        private void StartAttack()
        {
            bool inAir = _motor != null && _motor.pelvisRigidbody != null && Mathf.Abs(_motor.pelvisRigidbody.linearVelocity.y) > 1.5f;

            if (!_nextAttackIsLeft)
            {
                // Right hand: normal attack
                _attackMirror = false;
                _currentAtkState = atk1State;
                _nextAttackIsLeft = true;
            }
            else
            {
                // Left hand: dash attack
                _attackMirror = false; // By default, the dash animation is already on the left, so we do not mirror it
                _currentAtkState = dashState;
                _nextAttackIsLeft = false;
            }

            if (ghostAnimator != null)
            {
                foreach (var param in ghostAnimator.parameters)
                {
                    if (param.name == "AttackSpeed")
                    {
                        ghostAnimator.SetFloat("AttackSpeed", attackAnimSpeed);
                    }
                    if (param.name == mirrorParam)
                    {
                        ghostAnimator.SetBool(mirrorParam, _attackMirror);
                    }
                }
            }

            PlayBaseState(_currentAtkState, restart: true);

            CurrentState = PlayerState.Attack;
            IsPlayingOneShot = true;

            _upperBodyActive = true;
            _hitboxOpen = false;
        }

        private void UpdateAttackHitbox()
        {
            if (!_upperBodyActive || ghostAnimator == null) return;

            var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(atk1State) || info.IsName(airAtkState) || info.IsName(dashState))
            {
                ManageHitbox(info.normalizedTime);
            }
        }

        private void StopAttack()
        {
            CloseHitbox();
            _attackCooldownTimer = attackCooldown;

            CancelInvoke(nameof(FinalizeAttackCleanup));
            Invoke(nameof(FinalizeAttackCleanup), 0.2f);
        }

        private void FinalizeAttackCleanup()
        {
            _upperBodyActive = false;
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
            PunchHitbox fist = (_currentAtkState == dashState) ? leftFistHitbox : rightFistHitbox;

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

        // ──────────────────────────────────────────────────────────────────────
        // GRAB API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gọi khi người chơi BẮT ĐẦU giữ chuột trái (quá ngưỡng hold).
        /// Chỉ bật cờ physics _isGrabbing. Animation sẽ chuyển về locomotion tự nhiên
        /// theo DetermineBaseState() — tránh "bị kéo lê" khi di chuyển.
        /// </summary>
        public void StartGrab()
        {
            if (_isGrabbing) return;
            _isGrabbing = true;

            // Chỉ play grab animation một lần khi bắt đầu grab (tư thế giơ tay).
            // Ngay frame sau, DetermineBaseState() sẽ tự chuyển về Walk/Run/Idle
            // nếu người chơi đang di chuyển — chân sẽ bước bình thường.
            PlayBaseState(grabState, restart: true);
            _activeBaseState = grabState; // force reset để DetermineBaseState có thể override
        }

        /// <summary>
        /// Gọi khi người chơi THẢ chuột trái.
        /// </summary>
        public void StopGrab()
        {
            if (!_isGrabbing) return;
            _isGrabbing = false;

            // Về idle ngay lập tức; DetermineBaseState sẽ override nếu đang di chuyển
            CurrentState = PlayerState.Idle;
            IsPlayingOneShot = false;
            PlayBaseState(idleState, restart: true);
        }

        // Giữ lại để tương thích nếu vẫn còn nơi nào gọi
        public void TriggerGrab()
        {
            if (_isGrabbing) StopGrab();
            else StartGrab();
        }

        // ──────────────────────────────────────────────────────────────────────
        // STATE MACHINE
        // ──────────────────────────────────────────────────────────────────────

        private void DetermineBaseState()
        {
            // KHÔNG block khi đang grab — locomotion animation (Walk/Run/Idle) vẫn chạy bình thường.
            // _isGrabbing chỉ ảnh hưởng đến GrabHandlers (physics), không lock animation.

            if (IsPlayingOneShot)
            {
                bool landedFromJump = CurrentState == PlayerState.InAir
                                      && _motor != null && _motor.pelvisRigidbody != null
                                      && Mathf.Abs(_motor.pelvisRigidbody.linearVelocity.y) < 0.1f;

                bool oneShotDone = false;

                if (ghostAnimator != null)
                {
                    var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                    bool inActionState = info.IsName(dashState) || info.IsName(jumpState) ||
                                         info.IsName(atk1State) || info.IsName(airAtkState);

                    if (inActionState)
                    {
                        oneShotDone = (info.normalizedTime >= 0.95f);
                    }
                }

                if (landedFromJump || oneShotDone)
                {
                    IsPlayingOneShot = false;
                    if (_upperBodyActive) StopAttack();
                }
                else
                {
                    return;
                }
            }

            bool inAir = _motor != null && _motor.pelvisRigidbody != null
                         && Mathf.Abs(_motor.pelvisRigidbody.linearVelocity.y) > 1.5f;
            if (inAir)
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
            // Khi đang Grab + IsPlayingOneShot: không override (tránh interrupt Dash/Attack)
            bool isSpecialAction = IsPlayingOneShot &&
                                   (CurrentState == PlayerState.Dash ||
                                    CurrentState == PlayerState.InAir ||
                                    CurrentState == PlayerState.Attack);
            if (isSpecialAction) return;

            // Locomotion animation thuần: Walk/Run/Idle/InAir — không còn Grab case ở đây
            string target = CurrentState switch
            {
                PlayerState.Walk   => walkState,
                PlayerState.Run    => runState,
                PlayerState.InAir  => inAirState,
                PlayerState.Idle   => idleState,
                PlayerState.Attack => _currentAtkState,
                _                  => idleState
            };

            PlayBaseState(target);
        }

        private void ApplySpeedMultiplier()
        {
            if (ghostAnimator == null || _stats == null) return;
            ghostAnimator.speed = _stats.speedMultiplier;
        }

        private void PlayBaseState(string stateName, bool restart = false)
        {
            if (ghostAnimator == null) return;
            if (!restart && _activeBaseState == stateName) return;
            ghostAnimator.Play(stateName, 0, restart ? 0f : -1f);
            _activeBaseState = stateName;
        }

        public void OnDeath()
        {
            _isDead = true;
            _isGrabbing = false;
            _upperBodyActive = false;
            CloseHitbox();
        }

        public void OnRespawn()
        {
            _isDead = false;
        }
    }
}