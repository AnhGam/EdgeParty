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

    // Detailed settings panel references
    private VisualElement settingsPanel;
    private VisualElement musicFill;
    private VisualElement musicThumb;
    private VisualElement sfxFill;
    private VisualElement sfxThumb;

    private bool isSoundOn = true;

    private void Start()
    {
        // Load settings and initialize volumes
        float musicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfxVol = PlayerPrefs.GetFloat("SFXVolume", 1f);

        InitSliderVisuals(musicFill, musicThumb, musicVol);
        InitSliderVisuals(sfxFill, sfxThumb, sfxVol);

        ApplyVolumes(musicVol, sfxVol);
    }

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
        
        // Hook up main bar events
        soundButton?.RegisterCallback<ClickEvent>(evt => ToggleSound());
        settingsButton?.RegisterCallback<ClickEvent>(evt => OpenSettings());
        exitButton?.RegisterCallback<ClickEvent>(evt => QuitGame());

        // Settings panel setup
        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        if (settingsPanel != null)
        {
            musicFill = settingsPanel.Q<VisualElement>(className: "slider-fill-music");
            musicThumb = settingsPanel.Q<VisualElement>(className: "slider-thumb-music");
            sfxFill = settingsPanel.Q<VisualElement>(className: "slider-fill-sfx");
            sfxThumb = settingsPanel.Q<VisualElement>(className: "slider-thumb-sfx");

            var returnBtn = settingsPanel.Q<Button>("ReturnBtn");
            returnBtn?.RegisterCallback<ClickEvent>(evt => CloseSettings());

            // Wire up custom sliders
            var musicTrack = musicFill?.parent;
            var sfxTrack = sfxFill?.parent;

            RegisterSliderEvents(musicTrack, musicFill, musicThumb, "MusicVolume", val => {
                ApplyVolumes(val, PlayerPrefs.GetFloat("SFXVolume", 1f));
            });

            RegisterSliderEvents(sfxTrack, sfxFill, sfxThumb, "SFXVolume", val => {
                ApplyVolumes(PlayerPrefs.GetFloat("MusicVolume", 1f), val);
            });

            // Wire up quality options
            var graphicsButtons = settingsPanel.Query<Button>(className: "graphics-btn").ToList();
            if (graphicsButtons != null && graphicsButtons.Count >= 2)
            {
                var qualityBtn = graphicsButtons[0];
                var fpsBtn = graphicsButtons[1];

                qualityBtn.text = QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpper() + " QUALITY";
                fpsBtn.text = Application.targetFrameRate == 60 ? "60 FPS LIMIT" : "UNLIMITED FPS";
                if (Application.targetFrameRate != 60)
                    fpsBtn.AddToClassList("graphics-btn-inactive");
                
                qualityBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    int currentLevel = QualitySettings.GetQualityLevel();
                    int nextLevel = (currentLevel + 1) % QualitySettings.names.Length;
                    QualitySettings.SetQualityLevel(nextLevel);
                    qualityBtn.text = QualitySettings.names[nextLevel].ToUpper() + " QUALITY";
                    EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
                });

                fpsBtn.RegisterCallback<ClickEvent>(evt =>
                {
                    if (Application.targetFrameRate == 60)
                    {
                        Application.targetFrameRate = -1;
                        fpsBtn.text = "UNLIMITED FPS";
                        fpsBtn.AddToClassList("graphics-btn-inactive");
                    }
                    else
                    {
                        Application.targetFrameRate = 60;
                        fpsBtn.text = "60 FPS LIMIT";
                        fpsBtn.RemoveFromClassList("graphics-btn-inactive");
                    }
                    EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
                });
            }
        }
        
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
                redScoreLabel.text = ForestGameManager.Instance.Team1Score.Value.ToString("00");
            if (blueScoreLabel != null)
                blueScoreLabel.text = ForestGameManager.Instance.Team2Score.Value.ToString("00");
        }

        // Toggle settings on Escape key
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (settingsPanel != null)
            {
                if (settingsPanel.style.display == DisplayStyle.Flex)
                    CloseSettings();
                else
                    OpenSettings();
            }
        }

        // Enforce cursor state every frame while Alt is held or settings are open
        bool shouldShowCursor = false;
        if (Keyboard.current != null && Keyboard.current.leftAltKey.isPressed)
        {
            shouldShowCursor = true;
        }
        else if (settingsPanel != null && settingsPanel.style.display == DisplayStyle.Flex)
        {
            shouldShowCursor = true;
        }

        if (shouldShowCursor)
        {
            if (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                SetCursorState(true);
            }
        }
        else if (Keyboard.current != null && Keyboard.current.leftAltKey.wasReleasedThisFrame)
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
        AudioListener.volume = isSoundOn ? 1f : 0f;

        var icon = soundButton?.Q<VisualElement>("SoundIcon");
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
        
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        Debug.Log("Toggle Sound: " + (isSoundOn ? "ON" : "OFF"));
    }

    private void OpenSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.Flex;

            float musicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);
            float sfxVol = PlayerPrefs.GetFloat("SFXVolume", 1f);
            InitSliderVisuals(musicFill, musicThumb, musicVol);
            InitSliderVisuals(sfxFill, sfxThumb, sfxVol);

            SetCursorState(true);
        }
    }

    private void CloseSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
            SetCursorState(false);
        }
    }

    private void QuitGame()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        Debug.Log("Quit Game clicked - Returning to MainMenu");

        // Unlock and show cursor
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    // ─── Custom Slider Interaction Helpers ─────────────────────────────────

    private void RegisterSliderEvents(VisualElement track, VisualElement fill, VisualElement thumb, string prefKey, System.Action<float> onVolumeChanged)
    {
        if (track == null || fill == null || thumb == null) return;

        System.Action<float> updateSlider = (localX) =>
        {
            float width = track.resolvedStyle.width;
            if (width <= 0f) width = 300f; // fallback width
            float ratio = Mathf.Clamp01(localX / width);
            
            thumb.style.left = Length.Percent(ratio * 100f);
            fill.style.width = Length.Percent(ratio * 100f);
            
            onVolumeChanged?.Invoke(ratio);
            PlayerPrefs.SetFloat(prefKey, ratio);
            PlayerPrefs.Save();
        };

        track.RegisterCallback<PointerDownEvent>(evt =>
        {
            track.CapturePointer(evt.pointerId);
            updateSlider(evt.localPosition.x);
            evt.StopPropagation();
        });

        track.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (track.HasPointerCapture(evt.pointerId))
            {
                updateSlider(evt.localPosition.x);
                evt.StopPropagation();
            }
        });

        track.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (track.HasPointerCapture(evt.pointerId))
            {
                track.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        });
    }

    private void InitSliderVisuals(VisualElement fill, VisualElement thumb, float volume)
    {
        if (fill == null || thumb == null) return;
        thumb.style.left = Length.Percent(volume * 100f);
        fill.style.width = Length.Percent(volume * 100f);
    }

    private void ApplyVolumes(float musicVol, float sfxVol)
    {
        if (EdgeParty.Core.AudioManager.Instance != null)
        {
            EdgeParty.Core.AudioManager.Instance.SetMusicVolume(musicVol);
            EdgeParty.Core.AudioManager.Instance.SetSFXVolume(sfxVol);
        }
        if (AudioManager.Instance != null)
        {
            if (AudioManager.Instance.bgmSource != null)
                AudioManager.Instance.bgmSource.volume = musicVol;
            if (AudioManager.Instance.sfxSource != null)
                AudioManager.Instance.sfxSource.volume = sfxVol;
        }
    }
}
