using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using EdgeParty.Auth;
using EdgeParty.UI;

namespace EdgeParty.Gameplay.Character
{
    public class CustomizationController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private CustomizationData customizationData;
        
        private VisualElement root;
        private VisualElement itemGrid;
        private NetworkPlayerAppearance targetAppearance;

        private enum Category { Hats, Glasses, Neck, Faces, Colors }
        private Category currentCategory = Category.Hats;

        void OnEnable()
        {
            // If StitchUIController is missing, we are in a gameplay scene, so disable and deactivate this popup.
            if (Object.FindAnyObjectByType<StitchUIController>() == null)
            {
                if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
                if (uiDocument != null) uiDocument.enabled = false;
                gameObject.SetActive(false);
                return;
            }
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null)
            {
                uiDocument.enabled = true; // Ensure the UI Document is active and visible
                InitializeWithRoot(uiDocument.rootVisualElement);
            }
        }

        public void InitializeWithRoot(VisualElement rootElement)
        {
            root = rootElement;
            if (root == null) return;

            itemGrid = root.Q<VisualElement>("ItemGrid");
            if (itemGrid == null) return;

            // Always re-discover target to avoid stale references
            targetAppearance = null;

            // Prioritize finding NetworkPlayerAppearance in our own children (the local preview model)
            if (targetAppearance == null)
            {
                targetAppearance = GetComponentInChildren<NetworkPlayerAppearance>(true);
                if (targetAppearance == null)
                {
                    // Check if we have any child at all to attach to (e.g. Chibi_Monkey_00 Variant)
                    var childTransform = transform.Find("Chibi_Monkey_00 Variant");
                    if (childTransform == null && transform.childCount > 0)
                    {
                        for (int i = 0; i < transform.childCount; i++)
                        {
                            var child = transform.GetChild(i);
                            if (child.GetComponent<UnityEngine.Camera>() == null && child.name != "Cube")
                            {
                                childTransform = child;
                                break;
                            }
                        }
                    }

                    if (childTransform != null)
                    {
                        targetAppearance = childTransform.gameObject.AddComponent<NetworkPlayerAppearance>();
                        targetAppearance.data = customizationData;
                        Debug.Log("[CustomizationController] Dynamically attached NetworkPlayerAppearance to local preview root: " + childTransform.gameObject.name);
                    }
                }
            }

            root.Q<Button>("BackButton")?.RegisterCallback<ClickEvent>(evt => {
                gameObject.SetActive(false);
                var mainUI = Object.FindAnyObjectByType<StitchUIController>();
                if (mainUI != null)
                {
                    mainUI.HideLocker();
                }
            });
            
            SetupCategoryButton("BtnHats", Category.Hats);
            SetupCategoryButton("BtnGlasses", Category.Glasses);
            SetupCategoryButton("BtnNeck", Category.Neck);
            SetupCategoryButton("BtnFaces", Category.Faces);
            SetupCategoryButton("BtnColors", Category.Colors);

            currentCategory = Category.Hats;
            UpdateCategoryVisuals();
            RefreshGrid();

            if (targetAppearance != null)
            {
                var csm = CloudSaveManager.Instance;
                if (csm != null && csm.IsLoaded)
                {
                    var outfit = csm.CachedEquipped;
                    targetAppearance.SetHat(outfit.hat);
                    targetAppearance.SetGlasses(outfit.glasses);
                    targetAppearance.SetNecklace(outfit.necklace);
                    targetAppearance.SetEmotion(outfit.emotion);
                    targetAppearance.SetColor(outfit.color);
                    Debug.Log("[CustomizationController] Loaded cloud outfit into preview.");
                }
                else
                {
                    targetAppearance.SetHat(-1);
                    targetAppearance.SetGlasses(-1);
                    targetAppearance.SetNecklace(-1);
                    targetAppearance.SetEmotion(0);
                    targetAppearance.SetColor(0);
                }
            }
        }

        public void SetTarget(NetworkPlayerAppearance appearance)
        {
            targetAppearance = appearance;
        }

        private void SetupCategoryButton(string name, Category cat)
        {
            var btn = root.Q<Button>(name);
            btn?.RegisterCallback<ClickEvent>(evt => {
                currentCategory = cat;
                UpdateCategoryVisuals();
                RefreshGrid();
            });
        }

        private void UpdateCategoryVisuals()
        {
            var activeClass = "btn-primary-3d";
            var inactiveClass = "btn-surface-3d";

            root.Query<Button>(className: "category-button").ForEach(btn => {
                btn.RemoveFromClassList("selected");
                
                if (btn.ClassListContains(activeClass) || btn.ClassListContains(inactiveClass))
                {
                    btn.RemoveFromClassList(activeClass);
                    btn.RemoveFromClassList(inactiveClass);
                    btn.AddToClassList(inactiveClass);
                }
            });

            string activeName = "Btn" + currentCategory.ToString();
            var activeBtn = root.Q<Button>(activeName);
            if (activeBtn != null)
            {
                activeBtn.AddToClassList("selected");
                
                if (activeBtn.ClassListContains(inactiveClass))
                {
                    activeBtn.RemoveFromClassList(inactiveClass);
                    activeBtn.AddToClassList(activeClass);
                }
            }
        }

        private void RefreshGrid()
        {
            itemGrid.Clear();
            if (customizationData == null) return;

            switch (currentCategory)
            {
                case Category.Hats:
                    CreateItemCard(-1, null, "None"); // Add None option
                    if (customizationData.hats != null)
                        for (int i = 0; i < customizationData.hats.Count; i++) 
                            CreateItemCard(i, customizationData.hats[i].icon, customizationData.hats[i].name);
                    break;
                case Category.Glasses:
                    CreateItemCard(-1, null, "None"); // Add None option
                    if (customizationData.glasses != null)
                        for (int i = 0; i < customizationData.glasses.Count; i++) 
                            CreateItemCard(i, customizationData.glasses[i].icon, customizationData.glasses[i].name);
                    break;
                case Category.Neck:
                    CreateItemCard(-1, null, "None"); // Add None option
                    if (customizationData.necklaces != null)
                        for (int i = 0; i < customizationData.necklaces.Count; i++) 
                            CreateItemCard(i, customizationData.necklaces[i].icon, customizationData.necklaces[i].name);
                    break;
                case Category.Faces:
                    if (customizationData.emotions != null)
                        for (int i = 0; i < customizationData.emotions.Count; i++) 
                            CreateItemCard(i, customizationData.emotions[i].texture, customizationData.emotions[i].name);
                    break;
                case Category.Colors:
                    if (customizationData.colors != null)
                        for (int i = 0; i < customizationData.colors.Count; i++) 
                            CreateItemCard(i, customizationData.colors[i].icon, customizationData.colors[i].name);
                    break;
            }
        }

        private void CreateItemCard(int index, Texture2D icon, string itemName)
        {
            var card = new VisualElement();
            card.AddToClassList("item-card");

            bool isLocked = false;
            if (index != -1 && CloudSaveManager.Instance != null && CloudSaveManager.Instance.IsLoaded)
            {
                if (currentCategory == Category.Hats)
                {
                    isLocked = !CloudSaveManager.Instance.OwnsItem("hat_" + index);
                }
                else if (currentCategory == Category.Glasses)
                {
                    isLocked = !CloudSaveManager.Instance.OwnsItem("glasses_" + index);
                }
                else if (currentCategory == Category.Neck)
                {
                    isLocked = !CloudSaveManager.Instance.OwnsItem("neck_" + index);
                }
            }

            if (currentCategory == Category.Colors && index >= 0)
            {
                var mat = customizationData.colors[index].material;
                Texture2D colTex = customizationData.colors[index].icon;
                if (colTex == null && mat != null) colTex = mat.mainTexture as Texture2D;

                if (colTex != null)
                {
                    card.style.backgroundImage = new StyleBackground(colTex);
                    card.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Contain));
                }
                else
                {
                    Color col = Color.white;
                    if (mat != null)
                    {
                        if (mat.HasProperty("_BaseColor")) col = mat.GetColor("_BaseColor");
                        else if (mat.HasProperty("_Color")) col = mat.GetColor("_Color");
                    }
                    card.style.backgroundColor = new StyleColor(col);
                }
                card.style.borderTopLeftRadius = 60;
                card.style.borderTopRightRadius = 60;
                card.style.borderBottomLeftRadius = 60;
                card.style.borderBottomRightRadius = 60;
                
                card.style.borderTopWidth = 4;
                card.style.borderBottomWidth = 4;
                card.style.borderLeftWidth = 4;
                card.style.borderRightWidth = 4;
                card.style.borderTopColor = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
                card.style.borderBottomColor = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
                card.style.borderLeftColor = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
                card.style.borderRightColor = new StyleColor(new Color(0.9f, 0.9f, 0.9f));
            }
            else if (icon != null)
            {
                var iconView = new VisualElement();
                iconView.AddToClassList("item-icon");
                iconView.style.backgroundImage = new StyleBackground(icon);
                card.Add(iconView);
            }
            else
            {
                string emoji = "";
                if (currentCategory == Category.Hats) emoji = "🧢";
                else if (currentCategory == Category.Glasses) emoji = "👓";
                else if (currentCategory == Category.Neck) emoji = "🧣";
                else if (currentCategory == Category.Faces) emoji = "😊";
                
                var container = new VisualElement();
                container.style.alignItems = Align.Center;
                container.style.justifyContent = Justify.Center;
                container.style.flexGrow = 1;
                container.style.paddingTop = 15;
                
                if (!string.IsNullOrEmpty(emoji))
                {
                    var emojiLabel = new Label(emoji);
                    emojiLabel.style.fontSize = 28;
                    emojiLabel.style.marginBottom = 2;
                    emojiLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    container.Add(emojiLabel);
                }

                var label = new Label(itemName);
                label.AddToClassList("item-label");
                label.style.fontSize = 11;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.paddingTop = 0;
                label.style.paddingBottom = 0;
                label.style.whiteSpace = WhiteSpace.Normal;
                container.Add(label);
                
                card.Add(container);
            }

            if (isLocked)
            {
                card.style.opacity = 0.4f;
                
                var lockIcon = new Label("🔒");
                lockIcon.style.position = Position.Absolute;
                lockIcon.style.top = 4;
                lockIcon.style.right = 4;
                lockIcon.style.fontSize = 18;
                lockIcon.style.color = new StyleColor(new Color(0.4f, 0.3f, 0.2f));
                card.Add(lockIcon);

                card.RegisterCallback<ClickEvent>(evt => {
                    Debug.Log($"[CustomizationController] {itemName} is locked. Purchase it in the Shop!");
                });
            }
            else
            {
                card.RegisterCallback<ClickEvent>(evt => ApplySelection(index));
            }
            
            itemGrid.Add(card);
        }

        private void ApplySelection(int index)
        {
            if (targetAppearance != null)
            {
                switch (currentCategory)
                {
                    case Category.Hats: targetAppearance.SetHat(index); break;
                    case Category.Glasses: targetAppearance.SetGlasses(index); break;
                    case Category.Neck: targetAppearance.SetNecklace(index); break;
                    case Category.Faces: targetAppearance.SetEmotion(index); break;
                    case Category.Colors: targetAppearance.SetColor(index); break;
                }
            }

            var scenePlayers = Object.FindObjectsByType<NetworkPlayerAppearance>(FindObjectsSortMode.None);
            foreach (var player in scenePlayers)
            {
                if (player != targetAppearance)
                {
                    switch (currentCategory)
                    {
                        case Category.Hats: player.SetHat(index); break;
                        case Category.Glasses: player.SetGlasses(index); break;
                        case Category.Neck: player.SetNecklace(index); break;
                        case Category.Faces: player.SetEmotion(index); break;
                        case Category.Colors: player.SetColor(index); break;
                    }
                }
            }
        }
    }
}
