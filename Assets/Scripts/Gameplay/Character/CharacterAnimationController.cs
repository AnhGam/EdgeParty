using UnityEngine;
using System.Collections;

namespace EdgeParty.Gameplay.Character
{
    public enum PlayerState { Idle, Walk, Run, Attack, Dash, InAir }

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
        public int upperBodyLayerIndex = 1;   // Layer 1 must exist in the controller

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
        public string atk1State = "ATK1";
        public string atk2State = "ATK2";
        public string atk3State = "ATK3";
        public string airAtkState = "AirATK";

        [Header("Attack Settings")]
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

        [Header("Dash Cooldown")]
        public float dashCooldown = 1.5f;

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

        // Timers
        private float _attackCooldownTimer;
        private float _dashTimer;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            _motor = GetComponentInParent<CharacterMotor>() ?? GetComponent<CharacterMotor>();
            if (_motor == null)
            {
                var ctrl = GetComponentInParent<PlayerController>();
                if (ctrl != null) _motor = ctrl.motor;
            }

            _stats = GetComponentInParent<PlayerStats>() ?? GetComponentInChildren<PlayerStats>();
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
            if (!CanAttack()) return false;
            if (_stats != null && !_stats.HasStaminaForAttack) return false;

            _stats?.SpendAttackStamina();

            if (_upperBodyActive)
            {
                // Queue next combo hit during input window
                var info = ghostAnimator != null ? ghostAnimator.GetCurrentAnimatorStateInfo(upperBodyLayerIndex) : default;
                if (info.normalizedTime >= comboInputWindow)
                    _comboQueued = true;
            }
            else
            {
                StartAttack();
            }
            return true;
        }

        // ─── Main update ──────────────────────────────────────────────────
        private void Update()
        {
            float dt = Time.deltaTime;

            _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - dt);
            _dashTimer = Mathf.Max(0f, _dashTimer - dt);

            if (!IsServerActive()) return;

            UpdateUpperBody(dt);
            DetermineBaseState();
            UpdateBaseAnimator();
            ApplySpeedMultiplier();
        }

        private bool IsServerActive()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || nm.IsServer;
        }

        // ─── Upper body layer ─────────────────────────────────────────────

        private void StartAttack()
        {
            bool inAir = _motor != null && !_motor.IsGrounded;
            if (inAir)
            {
                _currentAtkState = airAtkState;
                _comboStep = 0;
            }
            else
            {
                _comboStep = 1;
                _currentAtkState = atk1State;
            }

            PlayUpperBodyState(_currentAtkState);
            _upperBodyActive = true;
            _comboQueued = false;
            _hitboxOpen = false;
        }

        private void UpdateUpperBody(float dt)
        {
            // Blend layer weight
            float targetWeight = _upperBodyActive ? 1f : 0f;
            _upperBodyWeight = Mathf.MoveTowards(_upperBodyWeight, targetWeight, upperBodyBlendSpeed * dt);
            if (ghostAnimator != null)
                ghostAnimator.SetLayerWeight(upperBodyLayerIndex, _upperBodyWeight);

            if (!_upperBodyActive) return;
            if (ghostAnimator == null) return;

            var info = ghostAnimator.GetCurrentAnimatorStateInfo(upperBodyLayerIndex);
            float t = info.normalizedTime;

            // Hitbox window
            ManageHitbox(t);

            // Check if current attack clip finished
            if (t >= 0.95f)
            {
                CloseHitbox();

                if (_comboQueued && _comboStep < 3)
                {
                    _comboQueued = false;
                    _comboStep++;
                    _currentAtkState = _comboStep switch { 2 => atk2State, 3 => atk3State, _ => atk1State };
                    PlayUpperBodyState(_currentAtkState);
                }
                else
                {
                    // End attack
                    _upperBodyActive = false;
                    _comboStep = 0;
                    _comboQueued = false;
                    _attackCooldownTimer = attackCooldown;
                    ghostAnimator.SetLayerWeight(upperBodyLayerIndex, 0f);
                    _upperBodyWeight = 0f;
                }
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
            rightFistHitbox?.Deactivate();
            leftFistHitbox?.Deactivate();
        }

        // ─── Base locomotion state machine ────────────────────────────────

        private void DetermineBaseState()
        {
            // One-shot states (jump / dash) — wait until clip finishes or grounded
            if (IsPlayingOneShot)
            {
                bool landedFromJump = CurrentState == PlayerState.InAir
                                      && _motor != null && _motor.IsGrounded;
                bool oneShotDone = false;

                if (ghostAnimator != null)
                {
                    var info = ghostAnimator.GetCurrentAnimatorStateInfo(baseLayerIndex);
                    oneShotDone = info.IsName(_activeBaseState) && info.normalizedTime >= 0.95f;
                }

                if (landedFromJump || oneShotDone)
                    IsPlayingOneShot = false;
                else
                    return;
            }

            if (_motor != null && !_motor.IsGrounded)
            {
                CurrentState = PlayerState.InAir;
                return;
            }

            if (_moveDir.sqrMagnitude > 0.01f)
                CurrentState = _isRunning ? PlayerState.Run : PlayerState.Walk;
            else
                CurrentState = PlayerState.Idle;
        }

        private void UpdateBaseAnimator()
        {
            if (IsPlayingOneShot) return;

            string target = CurrentState switch
            {
                PlayerState.Walk => GetDirectionalState(walkState, walkLState, walkRState),
                PlayerState.Run => GetDirectionalState(runState, runLState, runRState),
                PlayerState.InAir => inAirState,
                _ => idleState
            };

            PlayBaseState(target);
        }

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
            ghostAnimator.SetLayerWeight(upperBodyLayerIndex, 1f);
            ghostAnimator.Play(stateName, upperBodyLayerIndex, 0f);
            ghostAnimator.SetFloat("AttackSpeed", attackAnimSpeed);
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