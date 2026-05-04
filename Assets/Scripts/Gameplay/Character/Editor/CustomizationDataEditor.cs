using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace EdgeParty.Gameplay.Character
{
    [CustomEditor(typeof(CustomizationData))]
    public class CustomizationDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CustomizationData data = (CustomizationData)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("AUTO-SCAN ALL ASSETS", GUILayout.Height(40)))
            {
                ScanAssets(data);
            }
        }

        private void ScanAssets(CustomizationData data)
        {
            Undo.RecordObject(data, "Auto Scan Assets");

            // 1. Scan Accessories
            data.hats = ScanAccessory("Hats", "Assets/Suriyun/Pspsps/Prefab/Accessory");
            data.glasses = ScanAccessory("Glasses", "Assets/Suriyun/Pspsps/Prefab/Accessory");
            data.necklaces = ScanAccessory("Neck", "Assets/Suriyun/Pspsps/Prefab/Accessory");

            // 2. Scan Emotions
            data.emotions = ScanEmotions("Assets/Suriyun/Pspsps/Textures/Emotions");

            // 3. Scan Colors
            data.colors = ScanColors("Assets/Suriyun/Pspsps/Materials/Characters/Monkey");

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("Customization Data: Scan complete!");
        }

        private List<AccessoryItem> ScanAccessory(string filter, string folderPath)
        {
            List<AccessoryItem> items = new List<AccessoryItem>();
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

                // Simple filtering based on naming conventions
                bool match = false;
                if (filter == "Hats" && (fileName.Contains("cap") || fileName.Contains("hat") || fileName.Contains("crown") || fileName.Contains("helmet"))) match = true;
                else if (filter == "Glasses" && (fileName.Contains("glass") || fileName.Contains("eye"))) match = true;
                else if (filter == "Neck" && (fileName.Contains("neck") || fileName.Contains("bell"))) match = true;

                if (match)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    items.Add(new AccessoryItem
                    {
                        name = prefab.name,
                        prefab = prefab,
                        icon = GetAssetPreview(prefab)
                    });
                }
            }
            return items;
        }

        private List<EmotionItem> ScanEmotions(string folderPath)
        {
            List<EmotionItem> items = new List<EmotionItem>();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                items.Add(new EmotionItem
                {
                    name = tex.name,
                    texture = tex,
                    icon = GetAssetPreview(tex)
                });
            }
            return items;
        }

        private List<ColorItem> ScanColors(string folderPath)
        {
            List<ColorItem> items = new List<ColorItem>();
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                items.Add(new ColorItem
                {
                    name = mat.name,
                    material = mat,
                    icon = GetAssetPreview(mat)
                });
            }
            return items;
        }

        private Sprite GetAssetPreview(Object asset)
        {
            if (asset == null) return null;

            // Instead of creating a sprite from a texture (which isn't serializable as an asset),
            // we search for a Sprite asset with a matching name in the project.
            string assetName = asset.name;
            string[] guids = AssetDatabase.FindAssets(assetName + " t:Sprite");
            
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return null;
        }
    }
}
