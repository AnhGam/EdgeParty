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
        [SerializeField] private float fov = 60f;
        [SerializeField] private Vector3 cameraPosition = new Vector3(0f, 0.75f, 2.4f);

        private RenderTexture renderTexture;
        public RenderTexture previewTexture => renderTexture;
        private UnityEngine.Camera cam;

        public float Fov
        {
            get => fov;
            set
            {
                fov = value;
                if (cam == null) cam = GetComponent<UnityEngine.Camera>();
                if (cam != null)
                {
                    cam.fieldOfView = fov;
                }
            }
        }

        public Vector3 CameraPosition
        {
            get => cameraPosition;
            set
            {
                cameraPosition = value;
                if (cam == null) cam = GetComponent<UnityEngine.Camera>();
                if (cam != null)
                {
                    cam.transform.localPosition = cameraPosition;
                }
            }
        }

        public void ConfigureCamera(float targetFov, Vector3 targetPosition)
        {
            fov = targetFov;
            cameraPosition = targetPosition;
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.fieldOfView = fov;
                cam.transform.localPosition = cameraPosition;
            }
        }

        public void InitializeIfNeeded()
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.fieldOfView = fov;
                cam.transform.localPosition = cameraPosition;
            }

            if (renderTexture == null)
            {
                // Use ARGB32 for alpha/transparency support
                renderTexture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = renderTexture;

                // Configure camera to clear to a fully transparent color
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            DisablePhysicsAndResetPose();
        }

        private void DisablePhysicsAndResetPose()
        {
            var parent = transform.parent;
            if (parent != null)
            {
                var bgCube = parent.Find("Cube");
                if (bgCube != null)
                {
                    bgCube.gameObject.SetActive(false);
                }
                else
                {
                    // Fallback search in all direct children
                    foreach (Transform child in parent)
                    {
                        if (child.name == "Cube")
                        {
                            child.gameObject.SetActive(false);
                        }
                    }
                }

                // Disable gameplay and ragdoll scripts in EdgeParty namespace
                var monoBehaviours = parent.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var mb in monoBehaviours)
                {
                    if (mb == null) continue;
                    string ns = mb.GetType().Namespace;
                    if (ns != null && ns.StartsWith("EdgeParty"))
                    {
                        if (mb is PreviewCameraLink || mb is NetworkPlayerAppearance || mb is CustomizationController)
                        {
                            continue;
                        }
                        mb.enabled = false;
                    }
                }

                // Make all rigidbodies kinematic to disable gravity/physics on preview models
                var rigidbodies = parent.GetComponentsInChildren<Rigidbody>(true);
                foreach (var rb in rigidbodies)
                {
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                    }
                    rb.useGravity = false;
                }

                // Disable/Destroy all joints to prevent physics solving
                var joints = parent.GetComponentsInChildren<Joint>(true);
                foreach (var joint in joints)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(joint);
                    }
                    else
                    {
                        DestroyImmediate(joint);
                    }
                }

                // Disable all colliders
                var colliders = parent.GetComponentsInChildren<Collider>(true);
                foreach (var col in colliders)
                {
                    col.enabled = false;
                }

                // Rebind animator to reset character bones to their default poses
                var animator = parent.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
        }

        void OnEnable()
        {
            InitializeIfNeeded();
        }

        void Start()
        {
            InitializeIfNeeded();

            // 2. Link to UI
            if (uiDocument == null) uiDocument = GetComponentInParent<UIDocument>();
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
