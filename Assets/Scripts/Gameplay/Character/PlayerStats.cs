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
        // ─── Tuneable defaults ───────────────────────────────────────────
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

        [Header("Knockback")]
        [Tooltip("Base upward+backward impulse when taking a hit")]
        public float knockbackForce = 18f;
        [Tooltip("Extra upward ratio of knockback")]
        public float knockbackUpRatio = 0.4f;

        // ─── Networked values ─────────────────────────────────────────────
        public NetworkVariable<float> CurrentHealth  = new NetworkVariable<float>(100f);
        public NetworkVariable<float> CurrentStamina = new NetworkVariable<float>(100f);
        public NetworkVariable<bool>  IsDead         = new NetworkVariable<bool>(false);

        // ─── Local events (UI, effects, sounds) ──────────────────────────
        public event Action<float, float> OnHealthChanged;    // (current, max)
        public event Action<float, float> OnStaminaChanged;   // (current, max)
        public event Action<float, Vector3> OnHitReceived;    // (damage, hitDir)
        public event Action OnDied;
        public event Action OnRespawned;

        // ─── Private ──────────────────────────────────────────────────────
        private CharacterMotor _motor;
        private float _regenHealthTimer;
        private float _regenStaminaTimer;

        // ─── Properties ───────────────────────────────────────────────────
        public bool  IsAlive    => !IsDead.Value;
        public float HealthPct  => CurrentHealth.Value  / maxHealth;
        public float StaminaPct => CurrentStamina.Value / maxStamina;

        // ─── Stamina helpers (called by other systems) ────────────────────
        public bool HasStaminaForAttack => CurrentStamina.Value >= attackStaminaCost;
        public bool HasStaminaForDash   => CurrentStamina.Value >= dashStaminaCost;
        public bool HasStaminaToSprint  => CurrentStamina.Value > 0f;

        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            _motor = GetComponentInParent<CharacterMotor>();
            if (_motor == null) _motor = GetComponentInChildren<CharacterMotor>();
        }

        public override void OnNetworkSpawn()
        {
            // Initialise on server
            if (IsServer)
            {
                CurrentHealth.Value  = maxHealth;
                CurrentStamina.Value = maxStamina;
                IsDead.Value         = false;
            }

            // Subscribe on everyone so UI/effects work on clients too
            CurrentHealth.OnValueChanged  += (_, v) => OnHealthChanged?.Invoke(v, maxHealth);
            CurrentStamina.OnValueChanged += (_, v) => OnStaminaChanged?.Invoke(v, maxStamina);
            IsDead.OnValueChanged         += (prev, now) =>
            {
                if (now && !prev) OnDied?.Invoke();
                if (!now && prev) OnRespawned?.Invoke();
            };
        }

        // ─── Update (server-side logic only) ─────────────────────────────
        private void Update()
        {
            if (!IsServer) return;
            if (IsDead.Value) return;

            float dt = Time.deltaTime;

            // Apply speed multiplier to motor
            if (_motor != null)
            {
                // We store the base values once; scale is applied live
                // (PlayerController still controls actual force, we expose the multiplier)
            }

            // Health regen
            if (healthRegenRate > 0f)
            {
                _regenHealthTimer -= dt;
                if (_regenHealthTimer <= 0f && CurrentHealth.Value < maxHealth)
                {
                    CurrentHealth.Value = Mathf.Min(maxHealth, CurrentHealth.Value + healthRegenRate * dt);
                }
            }

            // Stamina regen
            _regenStaminaTimer -= dt;
            if (_regenStaminaTimer <= 0f && CurrentStamina.Value < maxStamina)
            {
                CurrentStamina.Value = Mathf.Min(maxStamina, CurrentStamina.Value + staminaRegenRate * dt);
            }
        }

        // ─── Public API (Server-only writes) ─────────────────────────────

        /// <summary>Called by PunchHitbox on the server when this player is struck.</summary>
        public void TakeDamage(float damage, Vector3 hitDirection, Rigidbody pelvisRb = null)
        {
            if (!IsServer || IsDead.Value) return;

            CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - damage);
            _regenHealthTimer   = healthRegenDelay;

            // Knockback
            if (pelvisRb != null)
            {
                Vector3 kb = hitDirection.normalized + Vector3.up * knockbackUpRatio;
                pelvisRb.AddForce(kb.normalized * knockbackForce, ForceMode.Impulse);
            }

            // Raise client-side event RPC so VFX/sound can play everywhere
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

            // Teleport pelvis if available
            if (_motor != null && _motor.pelvisRigidbody != null)
            {
                _motor.pelvisRigidbody.position = position;
                _motor.pelvisRigidbody.linearVelocity = Vector3.zero;
            }
        }

        private void Die()
        {
            IsDead.Value = true;
        }

        // ─── Client RPCs ──────────────────────────────────────────────────
        [ClientRpc]
        private void NotifyHitClientRpc(float damage, Vector3 hitDir)
        {
            OnHitReceived?.Invoke(damage, hitDir);
        }
    }
}
