using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EdgeParty.ConnectionManagement
{
    public struct BoneState : INetworkSerializable
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
        }
    }

    public struct RagdollFrameState : INetworkSerializable
    {
        public int AnimatorStateHash;
        public float AnimatorNormalizedTime;
        public BoneState[] Bones;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref AnimatorStateHash);
            serializer.SerializeValue(ref AnimatorNormalizedTime);
            
            int length = 0;
            if (!serializer.IsReader)
            {
                length = Bones != null ? Bones.Length : 0;
            }
            serializer.SerializeValue(ref length);

            if (serializer.IsReader)
            {
                if (Bones == null || Bones.Length != length)
                {
                    Bones = new BoneState[length];
                }
            }

            for (int i = 0; i < length; i++)
            {
                serializer.SerializeValue(ref Bones[i].Position);
                serializer.SerializeValue(ref Bones[i].Rotation);
            }
        }
    }

    public class ActiveRagdollSyncer : NetworkBehaviour
    {
        [Header("Interpolation")]
        public float syncRate = 20f;
        public float smoothSpeed = 15f;

        private Rigidbody[] _rigidbodies;
        private Transform[] _bones;
        
        // Target state for interpolation on remote clients
        private RagdollFrameState _targetState;
        private RagdollFrameState _prevState;
        
        private float _lastSendTime;
        private float _interpolationParam;

        private Animator _ghostAnimator;
        private EdgeParty.Gameplay.Character.PlayerController _playerController;

        private void Awake()
        {
            _rigidbodies = GetComponentsInChildren<Rigidbody>();
            _bones = new Transform[_rigidbodies.Length];
            for (int i = 0; i < _rigidbodies.Length; i++)
            {
                _bones[i] = _rigidbodies[i].transform;
            }

            // Self-healing: Search for controllers in parents or siblings
            _playerController = GetComponent<EdgeParty.Gameplay.Character.PlayerController>();
            if (_playerController == null) _playerController = GetComponentInParent<EdgeParty.Gameplay.Character.PlayerController>();
            if (_playerController == null) _playerController = GetComponentInChildren<EdgeParty.Gameplay.Character.PlayerController>();

            var animCtrl = GetComponent<EdgeParty.Gameplay.Character.CharacterAnimationController>();
            if (animCtrl == null) animCtrl = GetComponentInParent<EdgeParty.Gameplay.Character.CharacterAnimationController>();
            if (animCtrl == null) animCtrl = GetComponentInChildren<EdgeParty.Gameplay.Character.CharacterAnimationController>();
            
            // If still null, we might need to search the root hierarchy
            if (animCtrl == null && transform.root != null)
                animCtrl = transform.root.GetComponentInChildren<EdgeParty.Gameplay.Character.CharacterAnimationController>();

            if (animCtrl != null)
            {
                _ghostAnimator = animCtrl.ghostAnimator;
            }
            
            if (_ghostAnimator == null)
            {
                // Fallback to searching the whole hierarchy for ANY animator if the controller didn't have one
                _ghostAnimator = GetComponentInChildren<Animator>();
                if (_ghostAnimator == null && transform.root != null)
                    _ghostAnimator = transform.root.GetComponentInChildren<Animator>();
            }
        }

        public override void OnNetworkSpawn()
        {
            // NẾU LÀ CLIENT (Không phải Server): BIẾN TOÀN TẬP THÀNH BÙ NHÌN KINEMATIC
            // Vì Server-Authoritative, Player không được tự mô phỏng Physics cục bộ
            bool isOffline = NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer;
            bool isPureClient = IsClient && !IsServer && !isOffline;

            if (isPureClient)
            {
                // Diệt gọn cấu trúc Physics để cấm đụng độ nội bộ
                var scripts = GetComponentsInChildren<EdgeParty.Gameplay.Character.RagdollBoneFollower>();
                foreach (var script in scripts) Destroy(script);

                var joints = GetComponentsInChildren<Joint>();
                foreach (var joint in joints) Destroy(joint);

                foreach (var rb in _rigidbodies)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }

            _targetState.Bones = new BoneState[_bones.Length];
            _prevState.Bones = new BoneState[_bones.Length];
            
            CaptureState(ref _targetState);
            CaptureState(ref _prevState);
        }

        private void Update()
        {
            if (!IsSpawned || NetworkManager.Singleton == null) return;

            // CHỈ CÓ SERVER mới được quyền đo tọa độ xương phát cho trần gian
            if (IsServer)
            {
                if (Time.time - _lastSendTime >= (1f / syncRate))
                {
                    _lastSendTime = Time.time;
                    RagdollFrameState state = new RagdollFrameState();
                    state.Bones = new BoneState[_bones.Length];
                    CaptureState(ref state);
                    
                    UpdateStateClientRpc(state);
                }
            }
            
            bool isPureClient = IsClient && !IsServer;
            if (isPureClient)
            {
                // Ở Client, nội hàm interpolation kéo bù nhìn chạy theo Server
                _interpolationParam += Time.deltaTime * smoothSpeed;
                _interpolationParam = Mathf.Clamp01(_interpolationParam);

                for (int i = 0; i < _bones.Length && i < _targetState.Bones.Length && i < _prevState.Bones.Length; i++)
                {
                    Vector3 pos = Vector3.Lerp(_prevState.Bones[i].Position, _targetState.Bones[i].Position, _interpolationParam);
                    Quaternion rot = Quaternion.Slerp(_prevState.Bones[i].Rotation, _targetState.Bones[i].Rotation, _interpolationParam);
                    
                    _bones[i].position = pos;
                    _bones[i].rotation = rot;
                }

                if (_ghostAnimator != null && _targetState.AnimatorStateHash != 0)
                {
                    var info = _ghostAnimator.GetCurrentAnimatorStateInfo(0);
                    if (info.shortNameHash != _targetState.AnimatorStateHash)
                    {
                        _ghostAnimator.Play(_targetState.AnimatorStateHash, 0, _targetState.AnimatorNormalizedTime);
                    }
                }
            }
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void UpdateStateClientRpc(RagdollFrameState state)
        {
            // Máy chủ Server từ chối nhận lệnh RPC vì nó là cội nguồn của dữ liệu
            if (IsServer) return;

            _prevState = _targetState;
            _targetState = state;
            _interpolationParam = 0f;
        }

        private void CaptureState(ref RagdollFrameState state)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                state.Bones[i].Position = _bones[i].position;
                state.Bones[i].Rotation = _bones[i].rotation;
            }

            if (_ghostAnimator != null && _ghostAnimator.layerCount > 0)
            {
                var info = _ghostAnimator.GetCurrentAnimatorStateInfo(0);
                state.AnimatorStateHash = info.shortNameHash;
                state.AnimatorNormalizedTime = info.normalizedTime % 1f;
            }
        }
    }
}
