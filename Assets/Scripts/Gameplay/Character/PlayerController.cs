using UnityEngine;
using UnityEngine.InputSystem;

namespace EdgeParty.Gameplay.Character
{
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        public Rigidbody pelvisRigidbody;
        public Transform ghostRoot;
        public Transform ghostPelvis;
        public Animator ghostAnimator;

        [Header("Movement")]
        public float walkForce = 75f;
        public float runForce = 125f;
        public float jumpImpulse = 50f;
        public float dashImpulse = 100f;
        public float rotationSpeed = 5f;
        [Range(0f, 1f)] public float airControlFactor = 0.15f;
        public float idleDelay = 12f;
        [SerializeField] private float yawOffset = 0f;



        [Header("Bone Category Strength")]
        [Range(1f, 100f)] public float legMultiplier = 1f;  // Set default to 1 so it preserves user's Inspector tuning
        [Range(1f, 100f)] public float armMultiplier = 1f;
        [Range(1f, 100f)] public float torsoMultiplier = 1f;
        [Range(1f, 100f)] public float headMultiplier = 1f;

        [Header("Animation States")]
        public string noneState = "None";
        public string idleState = "IdleA";
        public string walkState = "Walk";
        public string runState = "Run";
        public string jumpState = "Jump";
        public string dashState = "Dash";

        private Transform _camTransform;
        private Vector3 _moveDir;
        private Vector3 _lastFacingDirection = Vector3.forward;
        private bool _isRunning;
        private bool _isPlayingOneShot;
        private float _oneShotTimer;
        private string _currentState = "";
        private RagdollBoneFollower[] _followers;
        private float _prevLegMul, _prevArmMul, _prevTorsoMul, _prevHeadMul;

        private void Awake()
        {
            if (UnityEngine.Camera.main != null)
                _camTransform = UnityEngine.Camera.main.transform;
                
            _followers = GetComponentsInChildren<RagdollBoneFollower>();

            if (ghostAnimator != null)
                ghostAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (ghostRoot != null)
                _lastFacingDirection = ghostRoot.forward;
        }

        private void Start()
        {
            ApplyBoneMultipliers();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || ghostAnimator == null) return;

            if (_prevLegMul != legMultiplier || _prevArmMul != armMultiplier ||
                _prevTorsoMul != torsoMultiplier || _prevHeadMul != headMultiplier)
            {
                ApplyBoneMultipliers();
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1;
            if (keyboard.sKey.isPressed) input.y -= 1;
            if (keyboard.aKey.isPressed) input.x -= 1;
            if (keyboard.dKey.isPressed) input.x += 1;

            bool hasMovement = input.sqrMagnitude > 0.01f;
            _isRunning = keyboard.leftShiftKey.isPressed;

            if (_isPlayingOneShot)
            {
                _oneShotTimer += Time.deltaTime;
                var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                
                if ((info.IsName(_currentState) && info.normalizedTime >= 0.95f) || _oneShotTimer > 1.5f) 
                {
                    _isPlayingOneShot = false;
                }
                else 
                {
                    if (hasMovement)
                    {
                        _moveDir = GetCameraRelativeDirection(input);
                        if (_moveDir.sqrMagnitude > 0.01f)
                            _lastFacingDirection = _moveDir;
                    }
                    return; 
                }
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                PlayState(jumpState, true);
                _isPlayingOneShot = true;
                _oneShotTimer = 0f;
                _moveDir = Vector3.zero;

                if (pelvisRigidbody != null)
                {
                    var vel = pelvisRigidbody.linearVelocity;
                    pelvisRigidbody.linearVelocity = new Vector3(vel.x * 0.3f, vel.y, vel.z * 0.3f);
                    pelvisRigidbody.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
                }
                return;
            }

            if (keyboard.leftShiftKey.wasPressedThisFrame && !hasMovement)
            {
                PlayState(dashState, true);
                _isPlayingOneShot = true;
                _oneShotTimer = 0f;
                _moveDir = Vector3.zero;

                if (pelvisRigidbody != null)
                {
                    Vector3 dashDir = ghostRoot.forward;
                    pelvisRigidbody.AddForce(dashDir * dashImpulse, ForceMode.Impulse);
                }
                return;
            }

            if (hasMovement)
            {
                ApplyBoneMultipliers(); 
                
                _moveDir = GetCameraRelativeDirection(input);
                if (_moveDir.sqrMagnitude > 0.01f)
                    _lastFacingDirection = _moveDir;

                PlayState(_isRunning ? runState : walkState);

                var info = ghostAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(_currentState) && info.normalizedTime >= 1f)
                {
                    PlayState(_currentState, true);
                }
            }
            else
            {
                _moveDir = Vector3.zero;
                ApplyBoneMultipliers();
                PlayState(noneState); 
            }
        }

        private void FixedUpdate()
        {
            if (pelvisRigidbody != null && ghostPelvis != null)
            {
                if (ghostRoot != null && _lastFacingDirection.sqrMagnitude > 0.001f)
                {
                    ghostRoot.rotation = Quaternion.LookRotation(_lastFacingDirection, Vector3.up);
                }

                Quaternion targetRot = ghostPelvis.rotation;
                Quaternion deltaRot = targetRot * Quaternion.Inverse(pelvisRigidbody.rotation);

                deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;

                if (Mathf.Abs(angle) > 0.01f)
                {
                    Vector3 torque = axis.normalized * (angle * rotationSpeed);
                    pelvisRigidbody.AddTorque(torque, ForceMode.Acceleration);
                }
            }

            if (pelvisRigidbody == null || _moveDir.sqrMagnitude < 0.01f) return;

            float force = _isRunning ? runForce : walkForce;

            if (_isPlayingOneShot)
                force *= airControlFactor;

            pelvisRigidbody.AddForce(_moveDir * force, ForceMode.Acceleration);
        }

        private void PlayState(string stateName, bool restart = false)
        {
            if (!restart && _currentState == stateName) return;
            ghostAnimator.Play(stateName, 0, restart ? 0f : -1f);
            _currentState = stateName;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (_camTransform == null) return Vector3.zero;
            Vector3 camForward = Vector3.ProjectOnPlane(_camTransform.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(_camTransform.right, Vector3.up).normalized;
            return (camForward * input.y + camRight * input.x).normalized;
        }

        private void ApplyBoneMultipliers()
        {
            if (_followers == null) return;
            foreach (var f in _followers)
            {
                switch (f.category)
                {
                    case BoneCategory.Leg: f.SetSpringMultiplier(legMultiplier); break;
                    case BoneCategory.Arm: f.SetSpringMultiplier(armMultiplier); break;
                    case BoneCategory.Torso: f.SetSpringMultiplier(torsoMultiplier); break;
                    case BoneCategory.Head: f.SetSpringMultiplier(headMultiplier); break;
                }
            }
            _prevLegMul = legMultiplier;
            _prevArmMul = armMultiplier;
            _prevTorsoMul = torsoMultiplier;
            _prevHeadMul = headMultiplier;
        }

    }
}

