using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class ExtractAndFixMaterials
{
    static ExtractAndFixMaterials()
    {
        EditorApplication.delayCall += RunFix;
    }

    static void RunFix()
    {
        if (SessionState.GetBool("FixMaterialsRun", false)) return;
        SessionState.SetBool("FixMaterialsRun", true);

        Debug.Log("ExtractAndFixMaterials: Starting...");
        int count = 0;

        string[] guids = AssetDatabase.FindAssets("t:Model");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLower().EndsWith(".fbx")) continue;

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && importer.materialLocation == ModelImporterMaterialLocation.InPrefab)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
                importer.SaveAndReimport();
                count++;
            }
        }
        
        Debug.Log($"ExtractAndFixMaterials: Extracted materials from {count} models.");
        
        // Wait a frame for reimport to finish, then upgrade
        EditorApplication.delayCall += UpgradeMaterials;
    }

    static void UpgradeMaterials()
    {
        Debug.Log("ExtractAndFixMaterials: Upgrading Standard materials...");
        string[] matGuids = AssetDatabase.FindAssets("t:Material");
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");

        int count = 0;
        foreach (string matGuid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(matGuid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && mat.shader != null && mat.shader.name == "Standard")
            {
                mat.shader = urpLit;
                EditorUtility.SetDirty(mat);
                count++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"ExtractAndFixMaterials: Successfully upgraded {count} materials to URP!");
    }
}
