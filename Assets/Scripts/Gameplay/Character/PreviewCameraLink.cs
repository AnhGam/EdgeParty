using UnityEngine;
using UnityEngine.UIElements;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Attach this to a Camera that views the preview character.
    /// It automatically creates a RenderTexture and links it to the UI PreviewContainer.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class PreviewCameraLink : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private string containerName = "PreviewContainer";
        [SerializeField] private int textureSize = 512;

        private RenderTexture renderTexture;
        private UnityEngine.Camera cam;

        void Start()
        {
            cam = GetComponent<UnityEngine.Camera>();
            
            // 1. Create a dynamic RenderTexture
            renderTexture = new RenderTexture(textureSize, textureSize, 24);
            cam.targetTexture = renderTexture;

            // 2. Link to UI
            if (uiDocument == null) uiDocument = Object.FindAnyObjectByType<UIDocument>();
            if (uiDocument != null)
            {
                var root = uiDocument.rootVisualElement;
                if (root == null) return; // Wait until UI is ready

                var container = root.Q<VisualElement>(containerName);
                if (container != null)
                {
                    container.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(renderTexture));
                    Debug.Log("Preview Camera: Linked to " + containerName);
                }
            }
        }

        void OnDestroy()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }
    }
}
