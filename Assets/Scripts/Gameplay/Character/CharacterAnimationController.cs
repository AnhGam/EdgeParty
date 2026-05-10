using UnityEngine;
using System.Collections.Generic;

namespace EdgeParty.Gameplay.Character
{
    public enum PlayerState { Idle, Walk, Run, Attack, Dash, InAir, Grab }

    public class CharacterAnimationController : MonoBehaviour
    {
        [Header("References")]
        public Animator ghostAnimator;
        public Transform ghostRoot;

        [Header("State Names")]
        public string idleState = "IdleA";
        public string walkState = "Walk";
        public string walkLState = "WalkL";
        public string walkRState = "WalkR";
        public string runState = "Run";
        public string runLState = "RunL";
        public string runRState = "RunR";
        public string jumpState = "Jump";
        public string dashState = "Dash";
        public string attackState = "RightATK";
        public string airAttackState = "AirATK";
        public string grabState = "Grab";

        [Header("Settings")]
        public float attackCooldown = 0.5f;
        public float dashCooldown = 5.0f;

        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public bool IsPlayingOneShot { get; private set; }
        public bool IsGrabbing => CurrentState == PlayerState.Grab;
        public bool IsAttacking => CurrentState == PlayerState.Attack && IsPlayingOneShot;

        private CharacterMotor _motor;
        private float _attackTimer;
        private float _dashTimer;
        private bool _isNextAttackLeft;
        private Vector3 _moveDir;
        private bool _isRunning;
        private string _activeStateName;

        private void Awake()
        {
            FindMotor();
        }

        private void FindMotor()
        {
            if (_motor != null) return;
            
            _motor = GetComponent<CharacterMotor>();
            if (_motor == null) _motor = GetComponentInParent<CharacterMotor>();
            if (_motor == null) _motor = GetComponentInChildren<CharacterMotor>();
            
            // If still null, search within siblings of the common root (PlayerController)
            if (_motor == null)
            {
                var controller = GetComponentInParent<PlayerController>();
                if (controller != null) _motor = controller.motor;
            }
        }

        private void Update()
        {
            UpdateTimers();
            if (IsServerActive())
            {
                DetermineState();
                UpdateAnimator();
            }
        }

        private bool IsServerActive()
        {
            if (Unity.Netcode.NetworkManager.Singleton == null) return true;
            return Unity.Netcode.NetworkManager.Singleton.IsServer;
        }

        private void UpdateTimers()
        {
            if (_attackTimer > 0) _attackTimer -= Time.deltaTime;
            if (_dashTimer > 0) _dashTimer -= Time.deltaTime;
        }

        public void SetMovementInput(Vector3 moveDir, bool isRunning)
        {
            _moveDir = moveDir;
            _isRunning = isRunning;
        }

        public bool CanAttack() => _attackTimer <= 0;
        public bool CanDash() => _dashTimer <= 0;

        public void TriggerAttack()
        {
            if (!CanAttack()) return;

            _attackTimer = attackCooldown;
            
            if (_motor != null && !_motor.IsGrounded)
            {
                PlayState(airAttackState, true);
                CurrentState = PlayerState.Attack;
            }
            else
            {
                // Toggle mirroring for combo
                if (ghostAnimator != null) ghostAnimator.SetBool("isMirrored", _isNextAttackLeft);
                PlayState(attackState, true);
                _isNextAttackLeft = !_isNextAttackLeft;
                CurrentState = PlayerState.Attack;
            }

            IsPlayingOneShot = true;
        }

        public void TriggerDash()
        {
            if (!CanDash()) return;
            _dashTimer = dashCooldown;
            PlayState(dashState, true);
            CurrentState = PlayerState.Dash;
            IsPlayingOneShot = true;
        }

        public void TriggerJump()
        {
            PlayState(jumpState, true);
            CurrentState = PlayerState.InAir;
            IsPlayingOneShot = true;
        }

        public void TriggerGrab()
        {
            if (CurrentState == PlayerState.Grab)
            {
                CurrentState = PlayerState.Idle;
                IsPlayingOneShot = false;
                PlayState(idleState, true);
            }
            else
            {
                PlayState(grabState, true);
                CurrentState = PlayerState.Grab;
                IsPlayingOneShot = true;
            }
        }

        private void DetermineState()
        {
            if (IsPlayingOneShot)
            {
                if (CurrentState == PlayerState.Grab) return; // Keep grab active

                var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(_activeStateName) && info.normalizedTime >= 0.95f)
                {
                    IsPlayingOneShot = false;
                }
                else return;
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

        private void UpdateAnimator()
        {
            if (IsPlayingOneShot) return;

            string targetState = idleState;

            switch (CurrentState)
            {
                case PlayerState.Idle:
                    targetState = idleState;
                    break;
                case PlayerState.Walk:
                    targetState = GetDirectionalState(walkState, walkLState, walkRState);
                    break;
                case PlayerState.Run:
                    targetState = GetDirectionalState(runState, runLState, runRState);
                    break;
                case PlayerState.InAir:
                    targetState = "None"; // Or falling state if you have one
                    break;
            }

            PlayState(targetState);
        }

        private string GetDirectionalState(string center, string left, string right)
        {
            if (ghostRoot == null) return center;

            float dot = Vector3.Dot(_moveDir, ghostRoot.right);
            if (dot > 0.5f) return right;
            if (dot < -0.5f) return left;
            return center;
        }

        private void PlayState(string stateName, bool restart = false)
        {
            if (ghostAnimator == null) return;
            if (!restart && _activeStateName == stateName) return;

            ghostAnimator.Play(stateName, 0, restart ? 0f : -1f);
            _activeStateName = stateName;
        }
    }
}
