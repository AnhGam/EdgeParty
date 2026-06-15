using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class AssetCleaner : EditorWindow
{
    [MenuItem("Tools/Clean Unused VFX Assets")]
    public static void CleanUnusedAssets()
    {
        string[] prefabsToKeep = new string[]
        {
            "Assets/Kyeoms_FX/Prefabs/New Stylized Explosion Package/FX_Explosion_F_1.prefab",
            "Assets/PixPlays/ElementalBeams/WindBeam/Version_BuiltIn/WindBeam.prefab"
        };

        HashSet<string> usedPaths = new HashSet<string>();

        // Collect all dependencies
        foreach (string prefabPath in prefabsToKeep)
        {
            if (!System.IO.File.Exists(prefabPath))
            {
                Debug.LogWarning("Prefab not found: " + prefabPath);
                continue;
            }
            string[] deps = AssetDatabase.GetDependencies(prefabPath, true);
            foreach (string dep in deps)
            {
                usedPaths.Add(dep.Replace("\\", "/"));
            }
        }

        // Directories to clean
        string[] dirsToClean = new string[] { "Assets/Kyeoms_FX", "Assets/PixPlays" };

        int deletedCount = 0;
        foreach (string dir in dirsToClean)
        {
            if (!AssetDatabase.IsValidFolder(dir)) continue;

            string[] allAssets = AssetDatabase.FindAssets("", new string[] { dir });
            foreach (string guid in allAssets)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                
                // Skip folders and scripts
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".cs")) continue;

                if (!usedPaths.Contains(path))
                {
                    Debug.Log("Deleting unused asset: " + path);
                    AssetDatabase.DeleteAsset(path);
                    deletedCount++;
                }
            }
        }

        Debug.Log($"Cleaned {deletedCount} unused assets to save memory!");
        AssetDatabase.Refresh();
    }
}
