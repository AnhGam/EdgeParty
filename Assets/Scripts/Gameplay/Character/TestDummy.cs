using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Attached to spawned player prefabs to turn them into test dummies.
    /// Runs primarily on the server to lock position and trigger behaviors.
    /// </summary>
    public class TestDummy : MonoBehaviour
    {
        public enum DummyBehavior { StandStill, AttackAndLock }
        public DummyBehavior behavior = DummyBehavior.StandStill;
        
        public float attackInterval = 1.6f;

        private PlayerController _controller;
        private Vector3 _lockPosition;
        private float _lastAttackTime = 0f;

        private void Start()
        {
            _controller = GetComponent<PlayerController>();
            _lockPosition = transform.position;

            // Disable player input handler so it does not respond to local keyboard/mouse input
            var inputHandler = GetComponentInChildren<PlayerInputHandler>();
            if (inputHandler != null)
            {
                inputHandler.enabled = false;
            }

            // Sync display names on server
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && _controller != null)
            {
                _controller.playerNameSync.Value = (behavior == DummyBehavior.StandStill) 
                    ? "Dummy_Target" 
                    : "Dummy_Attacker";
            }
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            if (behavior == DummyBehavior.AttackAndLock)
            {
                // Constantly lock position to spawn point to keep it stable
                if (_controller != null && _controller.pelvisRigidbody != null)
                {
                    _controller.pelvisRigidbody.transform.position = _lockPosition + Vector3.up * 0.8f;
                    _controller.pelvisRigidbody.linearVelocity = Vector3.zero;
                    _controller.pelvisRigidbody.angularVelocity = Vector3.zero;
                }
                transform.position = _lockPosition;

                var rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // Attack at intervals slightly longer than weapon/punch cooldown
                if (Time.time - _lastAttackTime >= attackInterval)
                {
                    _lastAttackTime = Time.time;
                    if (_controller != null)
                    {
                        _controller.OnAttackTriggered_Server();
                    }
                }
            }
        }
    }
}
