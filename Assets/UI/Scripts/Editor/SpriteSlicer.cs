using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class SpriteSlicer : Editor
{
    [MenuItem("Tools/Slice Soft Icons")]
    public static void SliceIcons()
    {
        string path = "Assets/UI/Textures/SoftIcons.png";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        
        if (importer == null) {
            Debug.LogError("Could not find SoftIcons.png at " + path);
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Bilinear;

        var sheet = new List<SpriteMetaData>();
        int size = 256;

        sheet.Add(new SpriteMetaData { name = "icon_sound", rect = new Rect(0, 256, size, size), alignment = 0 });
        sheet.Add(new SpriteMetaData { name = "icon_settings", rect = new Rect(256, 256, size, size), alignment = 0 });
        sheet.Add(new SpriteMetaData { name = "icon_exit", rect = new Rect(0, 0, size, size), alignment = 0 });
        sheet.Add(new SpriteMetaData { name = "icon_mouse", rect = new Rect(256, 0, size, size), alignment = 0 });

        // We use #pragma to hide the warning because the modern replacement requires the 2D Sprite package
        // which might not be installed in every project.
#pragma warning disable 0618
        importer.spritesheet = sheet.ToArray();
#pragma warning restore 0618
        
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        
        Debug.Log("SoftIcons.png sliced successfully!");
    }
}
