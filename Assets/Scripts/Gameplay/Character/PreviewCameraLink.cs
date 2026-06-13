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
                cam.aspect = 1.0f; // Force 1:1 aspect ratio
            }
        }

        public void InitializeIfNeeded()
        {
            if (cam == null) cam = GetComponent<UnityEngine.Camera>();
            if (cam != null)
            {
                cam.fieldOfView = fov;
                cam.transform.localPosition = cameraPosition;
                cam.aspect = 1.0f; // Force 1:1 aspect ratio to match square render texture!
            }

            if (renderTexture == null)
            {
                renderTexture = new RenderTexture(textureSize, textureSize, 24);
                cam.targetTexture = renderTexture;

                // Configure camera to clear to a fully transparent color
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            DisablePhysicsAndResetPose();

            // Refresh environment lighting + material state to fix dark tint after scene reload
            RefreshLightingAndMaterials();
        }


        /// <summary>
        /// Restores Main Menu scene lighting settings and forces URP to re-evaluate
        /// ambient/GI for the preview character.
        ///
        /// ROOT CAUSE: DemoScene_Forest uses AmbientMode=Custom with ambientIntensity=2.03
        /// and a full white ambient. MainMenu uses AmbientMode=Skybox with intensity=1.0
        /// and a darker sky color (0.21, 0.23, 0.26). The UTS2/Toon Shader reads ambient
        /// data to compute character lighting. After visiting Forest and returning to Menu,
        /// the ambient is correctly restored by Unity, BUT URP needs a frame or two to
        /// re-bake the SH ambient probes that the Toon Shader reads per-renderer.
        /// The fix: unconditionally override ambient to a neutral bright value for the
        /// preview rendering pass, then restore the scene's original settings after.
        /// </summary>
        private void RefreshLightingAndMaterials()
        {
            // 1. Disable light probes on all preview renderers to eliminate stale
            //    per-renderer SH ambient data from the previous (gameplay) scene.
            DisableLightProbesOnRenderers();

            // 2. ROOT FIX: The MainMenu ambient (Skybox mode, intensity=1.0, sky=(0.21,0.23,0.26))
            //    is significantly dimmer than DemoScene_Forest (Custom mode, intensity=2.03, white).
            //    UTS2 Toon Shader uses the ambient SH to determine the "shadow color" threshold,
            //    so a dimmer ambient = darker character after returning from gameplay.
            //    We unconditionally boost ambient to Flat/White while preview is active.
            //    This mirrors what the Forest scene sets, so character always looks consistent.
            _savedAmbientMode = RenderSettings.ambientMode;
            _savedAmbientLight = RenderSettings.ambientLight;
            _savedAmbientIntensity = RenderSettings.ambientIntensity;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 1.0f;

            // 3. Re-bake light probe tetrahedralization for current scene.
            LightProbes.TetrahedralizeAsync();

            // 4. Flush the GI environment immediately.
            DynamicGI.UpdateEnvironment();

            // 5. Multi-frame refresh so URP re-evaluates per-renderer ambient SH probes.
            StartCoroutine(DelayedGIRefresh());
        }

        // Saved ambient settings to restore if needed
        private UnityEngine.Rendering.AmbientMode _savedAmbientMode;
        private Color _savedAmbientLight;
        private float _savedAmbientIntensity;

        private void DisableLightProbesOnRenderers()
        {
            // KEY FIX: URP uploads per-renderer ambient SH probe data (unity_SHAr/g/b)
            // into the GPU's per-renderer CBUFFER every frame. SetPropertyBlock(null) does NOT
            // clear these internal values — they're set by URP's internal rendering pipeline.
            //
            // After returning from DemoScene_Forest (which has baked light probes with
            // AmbientMode=Custom, intensity=2.03), URP re-evaluates the SH probes from
            // MainMenu's dimmer Skybox ambient (sky=(0.21,0.23,0.26)), making the UTS2
            // Toon Shader render the character darker.
            //
            // Setting lightProbeUsage=Off tells URP to SKIP the SH probe lookup entirely.
            // Our dedicated PreviewDirectionalLight provides illumination instead.
            var root = transform.root; // Use root to catch all cases regardless of hierarchy depth
            if (root == null) root = transform;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                // Disable light probe sampling — eliminates stale SH data from previous scene
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                // Also clear any user-set property blocks
                r.SetPropertyBlock(null);
            }
        }

        private System.Collections.IEnumerator DelayedGIRefresh()
        {
            // Re-apply lightProbeUsage=Off over several frames because URP may
            // re-enable probe sampling when it re-processes the renderer list.
            yield return null;
            DisableLightProbesOnRenderers();
            DynamicGI.UpdateEnvironment();

            yield return null;
            DisableLightProbesOnRenderers();
            DynamicGI.UpdateEnvironment();

            yield return new UnityEngine.WaitForSeconds(0.1f);
            DisableLightProbesOnRenderers();
            DynamicGI.UpdateEnvironment();
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
