using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

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
            InitializeUI();
        }

        private void InitializeUI()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            if (root == null) return;

            itemGrid = root.Q<VisualElement>("ItemGrid");
            if (itemGrid == null) return;

            // Auto-find a preview character in the scene if target is null
            if (targetAppearance == null)
            {
                targetAppearance = Object.FindAnyObjectByType<NetworkPlayerAppearance>();
                if (targetAppearance != null) Debug.Log("Customization: Auto-linked to " + targetAppearance.name);
            }

            // Buttons
            root.Q<Button>("BackButton")?.RegisterCallback<ClickEvent>(evt => gameObject.SetActive(false));
            
            SetupCategoryButton("BtnHats", Category.Hats);
            SetupCategoryButton("BtnGlasses", Category.Glasses);
            SetupCategoryButton("BtnNeck", Category.Neck);
            SetupCategoryButton("BtnFaces", Category.Faces);
            SetupCategoryButton("BtnColors", Category.Colors);

            currentCategory = Category.Hats;
            UpdateCategoryVisuals();
            RefreshGrid();

            // Apply a default look if we have a target (No accessories by default)
            if (targetAppearance != null)
            {
                targetAppearance.SetHat(-1);
                targetAppearance.SetGlasses(-1);
                targetAppearance.SetNecklace(-1);
                targetAppearance.SetEmotion(0);
                targetAppearance.SetColor(0);
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
            root.Query<Button>(className: "category-button").ForEach(btn => {
                btn.RemoveFromClassList("selected");
            });

            string activeName = "Btn" + currentCategory.ToString();
            root.Q<Button>(activeName)?.AddToClassList("selected");
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
                            CreateItemCard(i, customizationData.emotions[i].icon, customizationData.emotions[i].name);
                    break;
                case Category.Colors:
                    if (customizationData.colors != null)
                        for (int i = 0; i < customizationData.colors.Count; i++) 
                            CreateItemCard(i, customizationData.colors[i].icon, customizationData.colors[i].name);
                    break;
            }
        }

        private void CreateItemCard(int index, Sprite icon, string itemName)
        {
            var card = new VisualElement();
            card.AddToClassList("item-card");

            if (icon != null)
            {
                var iconView = new VisualElement();
                iconView.AddToClassList("item-icon");
                iconView.style.backgroundImage = new StyleBackground(icon);
                card.Add(iconView);
            }
            else
            {
                // Show text label if no icon
                var label = new Label(itemName);
                label.AddToClassList("item-label");
                card.Add(label);
            }
            
            card.RegisterCallback<ClickEvent>(evt => ApplySelection(index));
            itemGrid.Add(card);
        }

        private void ApplySelection(int index)
        {
            if (targetAppearance == null) return;

            switch (currentCategory)
            {
                case Category.Hats: targetAppearance.SetHat(index); break;
                case Category.Glasses: targetAppearance.SetGlasses(index); break;
                case Category.Neck: targetAppearance.SetNecklace(index); break;
                case Category.Faces: targetAppearance.SetEmotion(index); break;
                case Category.Colors: targetAppearance.SetColor(index); break;
            }
        }
    }
}
