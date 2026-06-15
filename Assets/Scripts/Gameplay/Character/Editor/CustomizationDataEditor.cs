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

        private static List<Object> assetsToPreview = new List<Object>();
        private static CustomizationData dataToUpdate;

        private void ScanAssets(CustomizationData data)
        {
            Undo.RecordObject(data, "Auto Scan Assets");

            // Delete old generated sub-assets (previews and color icons)
            string dataPath = AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(dataPath))
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(dataPath);
                foreach (var sub in subAssets)
                {
                    if (sub != null && sub is Texture2D && (sub.name.EndsWith("_ColorIcon") || sub.name.EndsWith("_PreviewIcon")))
                    {
                        DestroyImmediate(sub, true);
                    }
                }
            }

            // 1. Scan Accessories
            data.hats = ScanAccessory("Hats", "Assets/Suriyun/Pspsps/Prefab/Accessory");
            data.glasses = ScanAccessory("Glasses", "Assets/Suriyun/Pspsps/Prefab/Accessory");
            data.necklaces = ScanAccessory("Neck", "Assets/Suriyun/Pspsps/Prefab/Accessory");

            // 2. Scan Emotions
            data.emotions = ScanEmotions("Assets/Suriyun/Pspsps/Textures/Emotions");

            // 3. Scan Colors
            data.colors = ScanColors(data, "Assets/Suriyun/Pspsps/Materials/Characters/Monkey");

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            // Queue preview updates since they are generated asynchronously
            dataToUpdate = data;
            assetsToPreview.Clear();
            if (data.hats != null) foreach (var item in data.hats) if (item.prefab != null) assetsToPreview.Add(item.prefab);
            if (data.glasses != null) foreach (var item in data.glasses) if (item.prefab != null) assetsToPreview.Add(item.prefab);
            if (data.necklaces != null) foreach (var item in data.necklaces) if (item.prefab != null) assetsToPreview.Add(item.prefab);

            // Trigger preview requests
            foreach (var asset in assetsToPreview)
            {
                AssetPreview.GetAssetPreview(asset);
            }

            EditorApplication.update -= UpdatePreviews;
            EditorApplication.update += UpdatePreviews;

            Debug.Log("Customization Data: Scan complete! Waiting for 3D previews to generate asynchronously...");
        }

        private static void UpdatePreviews()
        {
            if (dataToUpdate == null || assetsToPreview.Count == 0)
            {
                EditorApplication.update -= UpdatePreviews;
                return;
            }

            bool allLoaded = true;
            foreach (var asset in assetsToPreview)
            {
                if (asset != null && AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                {
                    allLoaded = false;
                }
            }

            bool modified = false;
            if (dataToUpdate.hats != null)
            {
                foreach (var item in dataToUpdate.hats)
                {
                    if (item.prefab != null && item.icon == null)
                    {
                        var preview = AssetPreview.GetAssetPreview(item.prefab);
                        if (preview != null && !AssetPreview.IsLoadingAssetPreview(item.prefab.GetInstanceID()))
                        {
                            item.icon = CreateReadableTextureCopy(preview, item.prefab.name + "_PreviewIcon", dataToUpdate);
                            modified = true;
                        }
                    }
                }
            }
            if (dataToUpdate.glasses != null)
            {
                foreach (var item in dataToUpdate.glasses)
                {
                    if (item.prefab != null && item.icon == null)
                    {
                        var preview = AssetPreview.GetAssetPreview(item.prefab);
                        if (preview != null && !AssetPreview.IsLoadingAssetPreview(item.prefab.GetInstanceID()))
                        {
                            item.icon = CreateReadableTextureCopy(preview, item.prefab.name + "_PreviewIcon", dataToUpdate);
                            modified = true;
                        }
                    }
                }
            }
            if (dataToUpdate.necklaces != null)
            {
                foreach (var item in dataToUpdate.necklaces)
                {
                    if (item.prefab != null && item.icon == null)
                    {
                        var preview = AssetPreview.GetAssetPreview(item.prefab);
                        if (preview != null && !AssetPreview.IsLoadingAssetPreview(item.prefab.GetInstanceID()))
                        {
                            item.icon = CreateReadableTextureCopy(preview, item.prefab.name + "_PreviewIcon", dataToUpdate);
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
            {
                EditorUtility.SetDirty(dataToUpdate);
                AssetDatabase.SaveAssets();
            }

            if (allLoaded)
            {
                Debug.Log("Customization Data: All 3D previews successfully generated and saved as persistent sub-assets!");
                EditorApplication.update -= UpdatePreviews;
                dataToUpdate = null;
                assetsToPreview.Clear();
            }
        }

        private static Texture2D CreateReadableTextureCopy(Texture2D source, string name, CustomizationData data)
        {
            if (source == null) return null;

            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, 
                source.height, 
                0, 
                RenderTextureFormat.Default, 
                RenderTextureReadWrite.Linear
            );

            Graphics.Blit(source, rt);

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readableTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readableTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readableTex.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);

            readableTex.name = name;

            string dataPath = AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(dataPath))
            {
                AssetDatabase.AddObjectToAsset(readableTex, data);
            }

            return readableTex;
        }

        private List<AccessoryItem> ScanAccessory(string filter, string folderPath)
        {
            List<AccessoryItem> items = new List<AccessoryItem>();
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path).ToLower();

                bool match = false;
                if (filter == "Hats" && (fileName.Contains("cap") || fileName.Contains("hat") || fileName.Contains("crown") || fileName.Contains("helmet"))) match = true;
                else if (filter == "Glasses" && (fileName.Contains("glass") || fileName.Contains("eye"))) match = true;
                else if (filter == "Neck" && (fileName.Contains("neck") || fileName.Contains("bell") || fileName.Contains("knot") || fileName.Contains("scarf"))) match = true;

                if (match)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    items.Add(new AccessoryItem
                    {
                        name = prefab.name,
                        prefab = prefab,
                        icon = null // Generated asynchronously
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

        private List<ColorItem> ScanColors(CustomizationData data, string folderPath)
        {
            // First, delete old generated icons from the ScriptableObject
            string dataPath = AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(dataPath))
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(dataPath);
                foreach (var sub in subAssets)
                {
                    if (sub != null && sub is Texture2D && sub.name.EndsWith("_ColorIcon"))
                    {
                        DestroyImmediate(sub, true);
                    }
                }
            }

            List<ColorItem> items = new List<ColorItem>();
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Texture2D tex = mat.mainTexture as Texture2D;
                Texture2D iconTex = null;

                if (tex != null)
                {
                    string texPath = AssetDatabase.GetAssetPath(tex);
                    TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    bool wasReadable = false;
                    if (importer != null)
                    {
                        wasReadable = importer.isReadable;
                        if (!wasReadable)
                        {
                            importer.isReadable = true;
                            importer.SaveAndReimport();
                        }
                    }

                    // Read pixels and calculate average color of body
                    try
                    {
                        Color[] pixels = tex.GetPixels();
                        float totalR = 0f, totalG = 0f, totalB = 0f;
                        int count = 0;
                        foreach (var pixel in pixels)
                        {
                            // Filter out transparent pixels
                            if (pixel.a < 0.1f) continue;
                            
                            // Filter out black/dark outlines (R, G, B < 0.2)
                            if (pixel.r < 0.2f && pixel.g < 0.2f && pixel.b < 0.2f) continue;
                            
                            // Filter out light cream face pixels (R > 0.8 && G > 0.8 && B > 0.65)
                            if (pixel.r > 0.8f && pixel.g > 0.8f && pixel.b > 0.65f) continue;

                            totalR += pixel.r;
                            totalG += pixel.g;
                            totalB += pixel.b;
                            count++;
                        }

                        Color avgColor = Color.white;
                        if (count > 0)
                        {
                            avgColor = new Color(totalR / count, totalG / count, totalB / count, 1f);
                        }
                        else if (pixels.Length > 0)
                        {
                            // Fallback to average of all pixels if everything got filtered out
                            float r = 0, g = 0, b = 0;
                            foreach (var p in pixels) { r += p.r; g += p.g; b += p.b; }
                            avgColor = new Color(r / pixels.Length, g / pixels.Length, b / pixels.Length, 1f);
                        }

                        // Create 32x32 solid texture
                        iconTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                        Color[] solidPixels = new Color[32 * 32];
                        for (int i = 0; i < solidPixels.Length; i++) solidPixels[i] = avgColor;
                        iconTex.SetPixels(solidPixels);
                        iconTex.Apply();
                        iconTex.name = mat.name + "_ColorIcon";

                        if (!string.IsNullOrEmpty(dataPath))
                        {
                            AssetDatabase.AddObjectToAsset(iconTex, data);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Error processing texture {tex.name}: {ex.Message}");
                    }

                    if (importer != null && !wasReadable)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }

                if (iconTex == null)
                {
                    // Fallback to normal preview
                    iconTex = mat.mainTexture as Texture2D;
                    if (iconTex == null) iconTex = GetAssetTexturePreview(mat);
                }

                items.Add(new ColorItem
                {
                    name = mat.name,
                    material = mat,
                    icon = iconTex
                });
            }
            return items;
        }

        private Texture2D GetAssetTexturePreview(Object asset)
        {
            if (asset == null) return null;

            // If it is a GameObject (Prefab) or Material, we want to render it in 3D using Unity's AssetPreview.
            // Since AssetPreview.GetAssetPreview loads asynchronously and might return null initially,
            // we run a quick loop to wait/force the rendering.
            if (asset is GameObject || asset is Material)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(asset);
                int count = 0;
                while (preview == null && count < 50)
                {
                    System.Threading.Thread.Sleep(5);
                    preview = AssetPreview.GetAssetPreview(asset);
                    count++;
                }
                if (preview != null) return preview;
            }

            // Try to find a texture with a matching name first (e.g. T_Cap_A for Cap_A)
            string assetName = asset.name;
            
            // Try matching with "T_" prefix or exact name
            string[] guids = AssetDatabase.FindAssets("T_" + assetName + " t:Texture2D");
            if (guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets(assetName + " t:Texture2D");
            }

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            // Fallback to built-in asset preview
            return AssetPreview.GetAssetPreview(asset);
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
