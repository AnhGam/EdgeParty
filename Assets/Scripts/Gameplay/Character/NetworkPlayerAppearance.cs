using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;
using EdgeParty.Auth;

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
        public List<Renderer> characterRenderers = new List<Renderer>();

        // Sync Variables
        public NetworkVariable<int> hatIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> glassesIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> necklaceIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> emotionIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> colorIndex = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private int localHat = -1;
        private int localGlasses = -1;
        private int localNecklace = -1;
        private int localEmotion = 0;
        private int localColor = 0;

        private GameObject currentHat;
        private GameObject currentGlasses;
        private GameObject currentNecklace;

        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (data == null)
            {
                data = Resources.Load<CustomizationData>("CustomizationData");
            }

            // Auto-discover components if missing
            if (characterRenderers == null) characterRenderers = new List<Renderer>();
            characterRenderers.Clear();

            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in skinned)
            {
                if (!characterRenderers.Contains(r)) characterRenderers.Add(r);
            }

            var other = GetComponentsInChildren<Renderer>(true);
            foreach (var r in other)
            {
                if (!characterRenderers.Contains(r))
                {
                    if (headBone != null && r.transform.IsChildOf(headBone)) continue;
                    if (neckBone != null && r.transform.IsChildOf(neckBone)) continue;
                    characterRenderers.Add(r);
                }
            }

            if (characterRenderer != null && !characterRenderers.Contains(characterRenderer))
            {
                characterRenderers.Add(characterRenderer);
            }
            else if (characterRenderer == null && characterRenderers.Count > 0)
            {
                characterRenderer = characterRenderers[0];
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

            // Owner loads & broadcasts their saved outfit
            if (IsOwner)
            {
                LoadAndApplyCloudOutfit();
            }
        }

        private void UpdateAll()
        {
            UpdateHat(hatIndex.Value);
            UpdateGlasses(glassesIndex.Value);
            UpdateNecklace(necklaceIndex.Value);
            UpdateEmotion(emotionIndex.Value);
            UpdateColor(colorIndex.Value);
        }

        /// <summary>
        /// Called once for the owning player: pull the saved outfit from
        /// CloudSaveManager cache and broadcast it via NetworkVariables.
        /// </summary>
        private void LoadAndApplyCloudOutfit()
        {
            var csm = CloudSaveManager.Instance;
            if (csm == null || !csm.IsLoaded) return;

            var outfit = csm.CachedEquipped;
            localHat = outfit.hat;
            localGlasses = outfit.glasses;
            localNecklace = outfit.necklace;
            localEmotion = outfit.emotion;
            localColor = outfit.color;

            hatIndex.Value      = outfit.hat;
            glassesIndex.Value  = outfit.glasses;
            necklaceIndex.Value = outfit.necklace;
            emotionIndex.Value  = outfit.emotion;
            colorIndex.Value    = outfit.color;

            Debug.Log($"[NetworkPlayerAppearance] Cloud outfit loaded: Hat={outfit.hat} Glasses={outfit.glasses}");
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
            if (index < 0 || data == null || data.emotions == null || index >= data.emotions.Count) return;

            foreach (var renderer in characterRenderers)
            {
                if (renderer == null) continue;
                var mats = renderer.materials;
                if (mats.Length > 1)
                {
                    // Slot 1 is the Face
                    mats[1].shader = Shader.Find("Unlit/Transparent");
                    mats[1].mainTexture = data.emotions[index].texture;
                    mats[1].doubleSidedGI = true; // Enable Double Sided GI
                    renderer.materials = mats;
                }
            }
        }

        private void UpdateColor(int index)
        {
            if (index < 0 || data == null || data.colors == null || index >= data.colors.Count) return;

            foreach (var renderer in characterRenderers)
            {
                if (renderer == null) continue;
                var mats = renderer.materials;
                if (mats.Length > 0)
                {
                    // Slot 0 is the Body
                    mats[0] = data.colors[index].material;
                    renderer.materials = mats;

                    // Clear any stale per-renderer shader properties (ambient probes, GI data)
                    // that URP may have baked from the gameplay scene's lighting environment.
                    // This forces URP to re-evaluate lighting from the current scene.
                    renderer.SetPropertyBlock(null);
                }
            }
        }


        public void PreviewItem(string category, int index)
        {
            if (data == null) return;
            switch (category.ToLower())
            {
                case "hat": UpdateHat(index); break;
                case "glasses": UpdateGlasses(index); break;
                case "necklace": UpdateNecklace(index); break;
                case "emotion": UpdateEmotion(index); break;
                case "color": UpdateColor(index); break;
            }
        }

        // Setters for UI
        public void SetHat(int index)
        {
            localHat = index;
            if (IsSpawned) hatIndex.Value = index;
            else UpdateHat(index);
            SaveOutfitToCloud();
        }
        public void SetGlasses(int index)
        {
            localGlasses = index;
            if (IsSpawned) glassesIndex.Value = index;
            else UpdateGlasses(index);
            SaveOutfitToCloud();
        }
        public void SetNecklace(int index)
        {
            localNecklace = index;
            if (IsSpawned) necklaceIndex.Value = index;
            else UpdateNecklace(index);
            SaveOutfitToCloud();
        }
        public void SetEmotion(int index)
        {
            localEmotion = index;
            if (IsSpawned) emotionIndex.Value = index;
            else UpdateEmotion(index);
            SaveOutfitToCloud();
        }
        public void SetColor(int index)
        {
            localColor = index;
            if (IsSpawned) colorIndex.Value = index;
            else UpdateColor(index);
            SaveOutfitToCloud();
        }

        private void SaveOutfitToCloud()
        {
            int hat = IsSpawned ? hatIndex.Value : localHat;
            int glasses = IsSpawned ? glassesIndex.Value : localGlasses;
            int necklace = IsSpawned ? necklaceIndex.Value : localNecklace;
            int emotion = IsSpawned ? emotionIndex.Value : localEmotion;
            int color = IsSpawned ? colorIndex.Value : localColor;

            _ = CloudSaveManager.Instance?.SaveEquippedOutfitAsync(
                hat, glasses, necklace, emotion, color);
        }
    }
}
