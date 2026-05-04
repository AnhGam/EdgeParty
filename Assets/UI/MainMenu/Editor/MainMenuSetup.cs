using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace EdgeParty.Editor
{
    public class MainMenuSetup
    {
        [MenuItem("Tools/EdgeParty/Generate Main Menu Scene")]
        public static void GenerateMainMenuScene()
        {
            // 1. Tạo một Scene trống mới
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            
            // 2. Tạo Camera (nếu cần thiết cho background/UI Toolkit fallback)
            GameObject cameraObj = new GameObject("Main Camera");
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.backgroundColor = new Color(248f/255f, 249f/255f, 250f/255f); // #f8f9fa
            cam.clearFlags = CameraClearFlags.SolidColor;
            
            // 3. Tạo UI Document
            GameObject uiDocObj = new GameObject("MainMenu_UIDocument");
            UIDocument uiDocument = uiDocObj.AddComponent<UIDocument>();
            
            // 4. Load file UXML
            string uxmlPath = "Assets/UI/MainMenu/MainMenu.uxml";
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            
            if (visualTree == null)
            {
                Debug.LogError($"[EdgeParty] Không tìm thấy file UXML tại: {uxmlPath}. Vui lòng kiểm tra lại đường dẫn.");
                return;
            }
            
            uiDocument.visualTreeAsset = visualTree;

            // Đảm bảo UIDocument có PanelSettings để render
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/MainMenu/MainMenuPanelSettings.asset");
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                // Thiết lập cơ bản cho PanelSettings nếu cần (ví dụ Scale Mode)
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                AssetDatabase.CreateAsset(panelSettings, "Assets/UI/MainMenu/MainMenuPanelSettings.asset");
            }
            uiDocument.panelSettings = panelSettings;

            // 6. Gắn script Logic
            uiDocObj.AddComponent<EdgeParty.UI.MainMenuController>();

            // 6.5 Thêm EventSystem để đảm bảo bắt click chuột tốt nhất
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // 7. Lưu Scene
            string scenePath = "Assets/Scenes/MainMenu.unity";
            
            // Đảm bảo thư mục Scenes tồn tại
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
            
            bool success = EditorSceneManager.SaveScene(newScene, scenePath);
            
            if (success)
            {
                Debug.Log($"[EdgeParty] Đã tạo thành công scene MainMenu tại: {scenePath}");
            }
            else
            {
                Debug.LogError("[EdgeParty] Không thể lưu scene MainMenu.");
            }
        }
    }
}
