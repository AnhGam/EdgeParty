using UnityEngine;
using Unity.Netcode;
using System;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Networked player stats: HP, Stamina, Speed modifier.
    /// Attach to the same GameObject as PlayerController.
    /// </summary>
    public class PlayerStats : NetworkBehaviour
    {
        [Header("Health")]
        public float maxHealth = 100f;
        [Tooltip("Seconds before HP starts recovering after last hit")]
        public float healthRegenDelay = 5f;
        [Tooltip("HP/sec recovered while not recently hit")]
        public float healthRegenRate = 0f;   // 0 = no regen by default (party game)

        [Header("Stamina")]
        public float maxStamina = 100f;
        [Tooltip("Stamina drained per second while sprinting")]
        public float sprintDrainRate = 20f;
        [Tooltip("Stamina drained per punch")]
        public float attackStaminaCost = 15f;
        [Tooltip("Stamina drained per dash")]
        public float dashStaminaCost = 25f;
        [Tooltip("Stamina/sec recovered while not sprinting")]
        public float staminaRegenRate = 12f;
        [Tooltip("Seconds after using stamina before regen starts")]
        public float staminaRegenDelay = 1.2f;

        [Header("Speed")]
        [Tooltip("Multiplier applied to CharacterMotor walkForce / runForce")]
        public float speedMultiplier = 1f;

        [Header("Fall Death")]
        [Tooltip("Y position below which the player is considered to have fallen off the map")]
        public float fallDeathY = -20f;

        [Header("Knockback")]
        [Tooltip("Base upward+backward impulse when taking a hit")]
        public float knockbackForce = 18f;
        [Tooltip("Extra upward ratio of knockback")]
        public float knockbackUpRatio = 0.4f;

        public NetworkVariable<float> CurrentHealth  = new NetworkVariable<float>(100f);
        public NetworkVariable<float> CurrentStamina = new NetworkVariable<float>(100f);
        public NetworkVariable<bool>  IsDead         = new NetworkVariable<bool>(false);
        [HideInInspector] public bool fellOffMap = false;

        public event Action<float, float> OnHealthChanged;
        public event Action<float, float> OnStaminaChanged;
        public event Action<float, Vector3> OnHitReceived;
        public event Action OnDied;
        public event Action OnRespawned;

        private CharacterMotor _motor;
        private float _regenHealthTimer;
        private float _regenStaminaTimer;

        public bool  IsAlive    => !IsDead.Value;
        public float HealthPct  => CurrentHealth.Value  / maxHealth;
        public float StaminaPct => CurrentStamina.Value / maxStamina;

        public bool HasStaminaForAttack => CurrentStamina.Value >= attackStaminaCost;
        public bool HasStaminaForDash   => CurrentStamina.Value >= dashStaminaCost;
        public bool HasStaminaToSprint  => CurrentStamina.Value > 0f;

        private void Awake()
        {
            _motor = transform.root.GetComponentInChildren<CharacterMotor>();
            if (_motor == null) _motor = GetComponentInParent<CharacterMotor>();
            if (_motor == null) _motor = GetComponentInChildren<CharacterMotor>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentHealth.Value  = maxHealth;
                CurrentStamina.Value = maxStamina;
                IsDead.Value         = false;
            }

            CurrentHealth.OnValueChanged  += (_, v) => OnHealthChanged?.Invoke(v, maxHealth);
            CurrentStamina.OnValueChanged += (_, v) => OnStaminaChanged?.Invoke(v, maxStamina);
            IsDead.OnValueChanged         += (prev, now) =>
            {
                if (now && !prev) OnDied?.Invoke();
                if (!now && prev) OnRespawned?.Invoke();
            };
        }

        private void Update()
        {
            if (!IsServer) return;
            if (IsDead.Value) return;

            float dt = Time.deltaTime;

            if (_motor == null)
            {
                _motor = GetComponentInParent<CharacterMotor>();
                if (_motor == null) _motor = GetComponentInChildren<CharacterMotor>();
            }

            Rigidbody pelvisRb = (_motor != null) ? _motor.pelvisRigidbody : null;
            if (pelvisRb == null)
            {
                foreach (var rb in transform.root.GetComponentsInChildren<Rigidbody>())
                {
                    if (rb.name.ToLower().Contains("pelvis"))
                    {
                        pelvisRb = rb;
                        break;
                    }
                }
            }

            float currentY = (pelvisRb != null) ? pelvisRb.position.y : transform.position.y;
            if (currentY < fallDeathY)
            {
                FallOffMap();
                return;
            }

            if (healthRegenRate > 0f)
            {
                _regenHealthTimer -= dt;
                if (_regenHealthTimer <= 0f && CurrentHealth.Value < maxHealth)
                {
                    CurrentHealth.Value = Mathf.Min(maxHealth, CurrentHealth.Value + healthRegenRate * dt);
                }
            }

            _regenStaminaTimer -= dt;
            if (_regenStaminaTimer <= 0f && CurrentStamina.Value < maxStamina)
            {
                CurrentStamina.Value = Mathf.Min(maxStamina, CurrentStamina.Value + staminaRegenRate * dt);
            }
        }

        public void TakeDamage(float damage, Vector3 hitDirection, Rigidbody pelvisRb = null, float customKnockbackForce = -1f)
        {
            if (!IsServer || IsDead.Value) return;

            CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - damage);
            _regenHealthTimer   = healthRegenDelay;

            if (pelvisRb != null)
            {
                float force = customKnockbackForce > 0f ? customKnockbackForce : knockbackForce;
                if (CurrentHealth.Value <= 0f)
                {
                    force *= 0.5f;
                }
                Vector3 kb = hitDirection.normalized + Vector3.up * knockbackUpRatio;
                pelvisRb.AddForce(kb.normalized * force, ForceMode.Impulse);
            }

            NotifyHitClientRpc(damage, hitDirection);

            if (CurrentHealth.Value <= 0f)
                Die();
        }

        /// <summary>Drain stamina for sprinting (call every frame while sprinting).</summary>
        public void DrainSprintStamina(float dt)
        {
            if (!IsServer) return;
            UseStamina(sprintDrainRate * dt);
        }

        /// <summary>Drain stamina for a punch.</summary>
        public bool SpendAttackStamina()
        {
            if (!IsServer || !HasStaminaForAttack) return false;
            UseStamina(attackStaminaCost);
            return true;
        }

        /// <summary>Drain stamina for a dash.</summary>
        public bool SpendDashStamina()
        {
            if (!IsServer || !HasStaminaForDash) return false;
            UseStamina(dashStaminaCost);
            return true;
        }

        private void UseStamina(float amount)
        {
            CurrentStamina.Value  = Mathf.Max(0f, CurrentStamina.Value - amount);
            _regenStaminaTimer    = staminaRegenDelay;
        }

        public void Respawn(Vector3 position)
        {
            if (!IsServer) return;
            CurrentHealth.Value  = maxHealth;
            CurrentStamina.Value = maxStamina;
            IsDead.Value         = false;
            fellOffMap           = false;
        }

        private void Die()
        {
            IsDead.Value = true;
        }

        /// <summary>Instantly kills the player and marks them as fallen off the map.</summary>
        public void FallOffMap()
        {
            if (!IsServer || IsDead.Value) return;
            fellOffMap = true;
            IsDead.Value = true;
            if (_motor != null && _motor.pelvisRigidbody != null)
            {
                var rb = _motor.pelvisRigidbody;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        [ClientRpc]
        private void NotifyHitClientRpc(float damage, Vector3 hitDir)
        {
            OnHitReceived?.Invoke(damage, hitDir);
        }
    }
}
