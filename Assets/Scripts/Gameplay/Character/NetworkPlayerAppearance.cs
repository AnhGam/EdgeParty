using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace EdgeParty.Gameplay.Character
{
    public class NetworkPlayerAppearance : NetworkBehaviour
    {
        [Header("Data Reference")]
        public CustomizationData data;

        [Header("Bone References")]
        public Transform headBone;
        public Transform neckBone;
        public Renderer characterRenderer;

        // Sync Variables
        public NetworkVariable<int> hatIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> glassesIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> necklaceIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> emotionIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> colorIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private GameObject currentHat;
        private GameObject currentGlasses;
        private GameObject currentNecklace;

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Auto-discover components if missing
            if (characterRenderer == null)
            {
                characterRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (characterRenderer == null)
                    characterRenderer = GetComponentInChildren<Renderer>(true);
            }

            if (neckBone == null)
            {
                neckBone = GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.ToLower().Contains("neck"));
            }

            if (headBone == null)
            {
                headBone = GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.ToLower().Contains("head"));
            }

            // Cleanup any existing objects attached to bones in the prefab
            ClearBone(headBone);
            ClearBone(neckBone);
        }

        private void ClearBone(Transform bone)
        {
            if (bone == null) return;
            // Only destroy children that have a Renderer (likely accessories)
            // and are not parts of the skeleton rig
            foreach (Transform child in bone.Cast<Transform>().ToList())
            {
                // If it has a Renderer but is not a bone, it's an accessory
                if (child.GetComponent<Renderer>() != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            InitializeComponents();

            // Update appearance when variables change
            hatIndex.OnValueChanged += (oldVal, newVal) => UpdateHat(newVal);
            glassesIndex.OnValueChanged += (oldVal, newVal) => UpdateGlasses(newVal);
            necklaceIndex.OnValueChanged += (oldVal, newVal) => UpdateNecklace(newVal);
            emotionIndex.OnValueChanged += (oldVal, newVal) => UpdateEmotion(newVal);
            colorIndex.OnValueChanged += (oldVal, newVal) => UpdateColor(newVal);

            // Initial Update
            UpdateAll();
        }

        private void UpdateAll()
        {
            UpdateHat(hatIndex.Value);
            UpdateGlasses(glassesIndex.Value);
            UpdateNecklace(necklaceIndex.Value);
            UpdateEmotion(emotionIndex.Value);
            UpdateColor(colorIndex.Value);
        }

        private void UpdateHat(int index)
        {
            if (currentHat != null) Destroy(currentHat);
            if (index >= 0 && index < data.hats.Count && headBone != null)
            {
                var prefab = data.hats[index].prefab;
                currentHat = Instantiate(prefab, headBone);
                // Optimized values from user reference
                currentHat.transform.SetLocalPositionAndRotation(
                    new Vector3(-0.15f, 0f, 0f), 
                    Quaternion.Euler(-90f, 90f, 0f)
                );
                currentHat.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            }
        }

        private void UpdateGlasses(int index)
        {
            if (currentGlasses != null) Destroy(currentGlasses);
            // Changed to neckBone as requested
            if (index >= 0 && index < data.glasses.Count && neckBone != null)
            {
                var prefab = data.glasses[index].prefab;
                currentGlasses = Instantiate(prefab, neckBone);
                // Optimized values from user reference
                currentGlasses.transform.SetLocalPositionAndRotation(
                    new Vector3(-0.2f, -0.17f, 0f), 
                    Quaternion.Euler(-90f, 90f, 0f)
                );
                currentGlasses.transform.localScale = Vector3.one;
            }
        }

        private void UpdateNecklace(int index)
        {
            if (currentNecklace != null) Destroy(currentNecklace);
            if (index >= 0 && index < data.necklaces.Count && neckBone != null)
            {
                var prefab = data.necklaces[index].prefab;
                currentNecklace = Instantiate(prefab, neckBone);
                // Optimized values from user reference
                currentNecklace.transform.SetLocalPositionAndRotation(
                    Vector3.zero, 
                    Quaternion.Euler(-90f, 90f, 0f)
                );
                currentNecklace.transform.localScale = Vector3.one;
            }
        }

        private void UpdateEmotion(int index)
        {
            if (characterRenderer != null && index >= 0 && index < data.emotions.Count)
            {
                var mats = characterRenderer.materials;
                if (mats.Length > 1)
                {
                    // Slot 1 is the Face
                    mats[1].shader = Shader.Find("Unlit/Transparent");
                    mats[1].mainTexture = data.emotions[index].texture;
                    mats[1].doubleSidedGI = true; // Enable Double Sided GI
                    characterRenderer.materials = mats;
                }
            }
        }

        private void UpdateColor(int index)
        {
            if (characterRenderer != null && index >= 0 && index < data.colors.Count)
            {
                var mats = characterRenderer.materials;
                if (mats.Length > 0)
                {
                    // Slot 0 is the Body
                    mats[0] = data.colors[index].material;
                    characterRenderer.materials = mats;
                }
            }
        }

        // Setters for UI
        public void SetHat(int index) { if (IsSpawned && IsOwner) hatIndex.Value = index; else UpdateHat(index); }
        public void SetGlasses(int index) { if (IsSpawned && IsOwner) glassesIndex.Value = index; else UpdateGlasses(index); }
        public void SetNecklace(int index) { if (IsSpawned && IsOwner) necklaceIndex.Value = index; else UpdateNecklace(index); }
        public void SetEmotion(int index) { if (IsSpawned && IsOwner) emotionIndex.Value = index; else UpdateEmotion(index); }
        public void SetColor(int index) { if (IsSpawned && IsOwner) colorIndex.Value = index; else UpdateColor(index); }
    }
}
