using UnityEngine;
using TMPro;

public class ForestHUD : MonoBehaviour
{
    public TMP_Text scoreT1Text;
    public TMP_Text scoreT2Text; 

    [Header("Premium HUD")]
    public GameObject premiumHudPrefab;

    private GameObject _premiumHudInstance;

    private void Start()
    {
        // Enforce 16:9 aspect ratio on the main camera
        EnforceAspectRatio();

        // Dynamically restore all forest map effects (water, rotating coins, particles)
        gameObject.AddComponent<ForestEffectsRestorer>();

        // Hide legacy UI elements
        if (scoreT1Text != null) scoreT1Text.gameObject.SetActive(false);
        if (scoreT2Text != null) scoreT2Text.gameObject.SetActive(false);

        // Try to hide the parent canvas if possible
        if (scoreT1Text != null)
        {
            Canvas parentCanvas = scoreT1Text.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.gameObject != gameObject)
            {
                parentCanvas.gameObject.SetActive(false);
            }
        }

        // Spawn premium HUD
        if (premiumHudPrefab != null)
        {
            _premiumHudInstance = Instantiate(premiumHudPrefab);
            Debug.Log("[ForestHUD] Premium HUD instantiated successfully.");
        }
        else
        {
            // Fallback load from Resources or AssetDatabase if running in Editor,
            // or we will let the user assign it.
            // Let's attempt to find the HUD_Preview prefab in the project at runtime or load it.
            premiumHudPrefab = Resources.Load<GameObject>("HUD_Preview");
            if (premiumHudPrefab != null)
            {
                _premiumHudInstance = Instantiate(premiumHudPrefab);
                Debug.Log("[ForestHUD] Premium HUD loaded from Resources and instantiated.");
            }
            else
            {
                Debug.LogWarning("[ForestHUD] Premium HUD Prefab not assigned and not found in Resources. Please assign HUD_Preview prefab in ForestHUD component.");
            }
        }
    }

    private void EnforceAspectRatio()
    {
        float targetAspect = 16f / 9f;
        Camera mainCam = Camera.main;
        if (mainCam == null) mainCam = Object.FindFirstObjectByType<Camera>();

        if (mainCam != null)
        {
            float windowAspect = (float)Screen.width / (float)Screen.height;
            float scaleHeight = windowAspect / targetAspect;

            if (scaleHeight < 1.0f)
            {
                Rect rect = mainCam.rect;
                rect.width = 1.0f;
                rect.height = scaleHeight;
                rect.x = 0;
                rect.y = (1.0f - scaleHeight) / 2.0f;
                mainCam.rect = rect;
            }
            else
            {
                float scaleWidth = 1.0f / scaleHeight;
                Rect rect = mainCam.rect;
                rect.width = scaleWidth;
                rect.height = 1.0f;
                rect.x = (1.0f - scaleWidth) / 2.0f;
                rect.y = 0;
                mainCam.rect = rect;
            }
            Debug.Log($"[ForestHUD] Locked Main Camera aspect ratio to 16:9 (scaleHeight={scaleHeight})");
        }
    }

    void Update()
    {
        if (ForestGameManager.Instance != null)
        {
            // Fallback update legacy text if it's active
            if (scoreT1Text != null && scoreT1Text.gameObject.activeSelf)
            {
                scoreT1Text.text = $"Đội Đỏ: {ForestGameManager.Instance.Team1Score.Value}";
            }
            if (scoreT2Text != null && scoreT2Text.gameObject.activeSelf)
            {
                scoreT2Text.text = $"Đội Xanh: {ForestGameManager.Instance.Team2Score.Value}";
            }
        }
    }
}