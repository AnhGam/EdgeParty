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

        [MenuItem("Tools/EdgeParty/Integrate Stitch UI into SampleScene")]
        public static void IntegrateStitchUIIntoSampleScene()
        {
            // 1. Open the SampleScene
            string scenePath = "Assets/Scenes/SampleScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 2. Find and disable/delete the old ClientConnectionUI
            var oldUI = Object.FindAnyObjectByType<EdgeParty.ConnectionManagement.ClientConnectionUI>();
            if (oldUI != null)
            {
                var oldGo = oldUI.gameObject;
                Debug.Log($"[StitchUI] Found old ClientConnectionUI on GameObject '{oldGo.name}'. Deleting it...");
                Undo.DestroyObjectImmediate(oldGo);
            }

            // 3. Check if StitchUI already exists in scene
            var existingUI = Object.FindAnyObjectByType<EdgeParty.UI.StitchUIController>();
            if (existingUI != null)
            {
                Debug.Log("[StitchUI] StitchUIController already exists in SampleScene.");
                EditorSceneManager.SaveScene(scene);
                return;
            }

            // 4. Create StitchUI_Document GameObject
            GameObject uiDocObj = new GameObject("StitchUI_Document");
            Undo.RegisterCreatedObjectUndo(uiDocObj, "Create StitchUI_Document");
            
            UIDocument uiDocument = uiDocObj.AddComponent<UIDocument>();

            // 5. Load PanelSettings
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

            // 6. Attach StitchUIController and load Visual Tree Assets
            var controller = uiDocObj.AddComponent<EdgeParty.UI.StitchUIController>();
            SerializedObject so = new SerializedObject(controller);

            AssignVisualTreeAsset(so, "loginVisualTree",          "Assets/UI/StitchUI/UXML/LoginMenu.uxml");
            AssignVisualTreeAsset(so, "registerVisualTree",       "Assets/UI/StitchUI/UXML/RegisterMenu.uxml");
            AssignVisualTreeAsset(so, "forgotPasswordVisualTree", "Assets/UI/StitchUI/UXML/ForgotPasswordMenu.uxml");
            AssignVisualTreeAsset(so, "homeVisualTree",           "Assets/UI/StitchUI/UXML/HomeMenu.uxml");
            AssignVisualTreeAsset(so, "shopVisualTree",           "Assets/UI/StitchUI/UXML/ShopMenu.uxml");
            AssignVisualTreeAsset(so, "matchmakingVisualTree",    "Assets/UI/StitchUI/UXML/MatchmakingMenu.uxml");

            so.ApplyModifiedPropertiesWithoutUndo();

            // 7. EventSystem check
            var existingEventSystem = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (existingEventSystem == null)
            {
                GameObject eventSystemObj = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
                eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemObj.AddComponent<InputSystemUIInputModule>();
            }

            // 8. Save Scene
            EditorSceneManager.MarkSceneDirty(scene);
            bool success = EditorSceneManager.SaveScene(scene);
            if (success)
            {
                Debug.Log($"[StitchUI] Successfully integrated Stitch UI into SampleScene at: {scenePath}");
            }
            else
            {
                Debug.LogError("[StitchUI] Failed to save SampleScene.");
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
