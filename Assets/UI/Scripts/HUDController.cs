using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class HUDController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Texture2D customCursor;
    
    private VisualElement root;
    private VisualElement dynamicBar;
    
    private Label redScoreLabel;
    private Label blueScoreLabel;
    
    private Button soundButton;
    private Button settingsButton;
    private Button exitButton;

    private bool isSoundOn = true;

    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        root = uiDocument.rootVisualElement;
        if (root == null) return;
        
        // Score labels
        redScoreLabel = root.Q<Label>("RedScore");
        blueScoreLabel = root.Q<Label>("BlueScore");
        
        // Buttons
        soundButton = root.Q<Button>("SoundButton");
        settingsButton = root.Q<Button>("SettingsButton");
        exitButton = root.Q<Button>("ExitButton");
        
        dynamicBar = root.Q<VisualElement>("DynamicBar");
        
        // Hook up events
        soundButton?.RegisterCallback<ClickEvent>(evt => ToggleSound());
        settingsButton?.RegisterCallback<ClickEvent>(evt => OpenSettings());
        exitButton?.RegisterCallback<ClickEvent>(evt => QuitGame());
        
        // Initial setup
        UpdateInstructionBar("BOOST", new string[] { "CTRL", "J", "F" });
        SetCursorState(false);
    }

    void Update()
    {
        // Update score from ForestGameManager
        if (ForestGameManager.Instance != null)
        {
            if (redScoreLabel != null)
                redScoreLabel.text = ForestGameManager.Instance.Team1Score.Value.ToString();
            if (blueScoreLabel != null)
                blueScoreLabel.text = ForestGameManager.Instance.Team2Score.Value.ToString();
        }

        // Enforce cursor state every frame while Alt is held
        if (Keyboard.current.leftAltKey.isPressed)
        {
            // Only call if state isn't already correct to avoid overhead, 
            // but we want to be firm about visibility.
            if (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                SetCursorState(true);
            }
        }
        else if (Keyboard.current.leftAltKey.wasReleasedThisFrame)
        {
            SetCursorState(false);
        }
    }

    private void SetCursorState(bool visible)
    {
        UnityEngine.Cursor.visible = visible;
        UnityEngine.Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        
        if (visible && customCursor != null)
        {
            // Set custom cursor (hotspot at top-left)
            UnityEngine.Cursor.SetCursor(customCursor, Vector2.zero, CursorMode.Auto);
        }
        else
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }

    public void UpdateInstructionBar(string actionName, string[] keys)
    {
        if (dynamicBar == null) return;
        
        dynamicBar.Clear();
        
        foreach (var key in keys)
        {
            var keyCap = new Label(key);
            keyCap.AddToClassList("key-cap");
            dynamicBar.Add(keyCap);
        }
        
        var actionLabel = new Label(actionName.ToUpper());
        actionLabel.AddToClassList("instruction-label");
        dynamicBar.Add(actionLabel);
    }

    private void ToggleSound()
    {
        isSoundOn = !isSoundOn;
        var icon = soundButton.Q<VisualElement>("SoundIcon");
        if (icon != null)
        {
            if (isSoundOn)
            {
                icon.RemoveFromClassList("icon-sound-off");
                icon.AddToClassList("icon-sound");
            }
            else
            {
                icon.RemoveFromClassList("icon-sound");
                icon.AddToClassList("icon-sound-off");
            }
        }
        Debug.Log("Toggle Sound: " + (isSoundOn ? "ON" : "OFF"));
    }

    private void OpenSettings()
    {
        Debug.Log("Open Settings clicked");
    }

    private void QuitGame()
    {
        Debug.Log("Quit Game clicked");
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
}
