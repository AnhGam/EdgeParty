using UnityEngine;
using System.Collections.Generic;

namespace EdgeParty.Gameplay.Character
{
    [CreateAssetMenu(fileName = "CustomizationData", menuName = "EdgeParty/Customization Data")]
    public class CustomizationData : ScriptableObject
    {
        public List<AccessoryItem> hats;
        public List<AccessoryItem> glasses;
        public List<AccessoryItem> necklaces;
        public List<EmotionItem> emotions;
        public List<ColorItem> colors;
    }

    [System.Serializable]
    public class AccessoryItem
    {
        public string name;
        public GameObject prefab;
        public Texture2D icon;
    }

    [System.Serializable]
    public class EmotionItem
    {
        public string name;
        public Texture2D texture;
        public Sprite icon;
    }

    [System.Serializable]
    public class ColorItem
    {
        public string name;
        public Material material;
        public Texture2D icon;
    }
}
