using UnityEngine;
using UnityEditor;
using System.IO;

public class GenerateRoundedRect
{
    [MenuItem("Tools/Tạo Nền Nameplate Bo Tròn")]
    public static void Generate()
    {
        int size = 64;
        int radius = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Tính khoảng cách từ tâm góc bo tròn
                float dx = Mathf.Max(0, radius - x, x - (size - radius - 1));
                float dy = Mathf.Max(0, radius - y, y - (size - radius - 1));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                
                // Khử răng cưa nhẹ (Anti-aliasing)
                float alpha = Mathf.Clamp01(radius - dist);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }
        
        tex.SetPixels(pixels);
        tex.Apply();

        string path = "Assets/NameplateBackground.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        // Cấu hình tự động thành Sprite Sliced
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteBorder = new Vector4(radius, radius, radius, radius);
            importer.SaveAndReimport();
        }

        Debug.Log("Đã tạo thành công khung nền bo tròn tại: " + path);
    }
}
