using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace EdgeParty.Editor
{
    public class StitchMenuSetup
    {
        [MenuItem("Tools/EdgeParty/Generate Stitch UI Menu Scene")]
        public static void GenerateStitchMenuScene()
        {
            // 1. Create a fresh empty scene
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Camera with dark background
            GameObject cameraObj = new GameObject("Main Camera");
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.09f); // Deep dark purple
            cam.clearFlags = CameraClearFlags.SolidColor;
            cameraObj.tag = "MainCamera";

            // 3. UIDocument GameObject
            GameObject uiDocObj = new GameObject("StitchUI_Document");
            UIDocument uiDocument = uiDocObj.AddComponent<UIDocument>();

            // 4. PanelSettings – reuse existing or create new
            string panelSettingsPath = "Assets/UI/StitchUI/StitchPanelSettings.asset";
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
                Debug.Log($"[StitchUI] Created PanelSettings at: {panelSettingsPath}");
            }
            uiDocument.panelSettings = panelSettings;

            // 5. Attach StitchUIController and load UXML VisualTreeAssets
            var controller = uiDocObj.AddComponent<EdgeParty.UI.StitchUIController>();

            // Use SerializedObject to assign private [SerializeField] references
            SerializedObject so = new SerializedObject(controller);

            AssignVisualTreeAsset(so, "loginVisualTree",          "Assets/UI/StitchUI/UXML/LoginMenu.uxml");
            AssignVisualTreeAsset(so, "registerVisualTree",       "Assets/UI/StitchUI/UXML/RegisterMenu.uxml");
            AssignVisualTreeAsset(so, "forgotPasswordVisualTree", "Assets/UI/StitchUI/UXML/ForgotPasswordMenu.uxml");
            AssignVisualTreeAsset(so, "homeVisualTree",           "Assets/UI/StitchUI/UXML/HomeMenu.uxml");
            AssignVisualTreeAsset(so, "shopVisualTree",           "Assets/UI/StitchUI/UXML/ShopMenu.uxml");
            AssignVisualTreeAsset(so, "matchmakingVisualTree",    "Assets/UI/StitchUI/UXML/MatchmakingMenu.uxml");

            so.ApplyModifiedPropertiesWithoutUndo();

            // 6. EventSystem for input handling (using New Input System)
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();

            // 7. Save Scene
            string scenePath = "Assets/Scenes/MainMenu.unity";

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            bool success = EditorSceneManager.SaveScene(newScene, scenePath);

            if (success)
            {
                Debug.Log($"[StitchUI] Successfully generated Stitch UI Menu Scene at: {scenePath}");
                Debug.Log("[StitchUI] Remember to add this scene to Build Settings (File > Build Settings > Add Open Scenes).");
            }
            else
            {
                Debug.LogError("[StitchUI] Failed to save scene.");
            }
        }

        private static void AssignVisualTreeAsset(SerializedObject so, string fieldName, string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[StitchUI] Could not find UXML asset at: {assetPath}");
                return;
            }

            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = asset;
            }
            else
            {
                Debug.LogWarning($"[StitchUI] Could not find serialized field: {fieldName}");
            }
        }
    }
}
