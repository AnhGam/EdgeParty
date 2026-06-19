using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Texture2D customCursor;
    
    private VisualElement root;
    private VisualElement dynamicBar;
    
    private Label redScoreLabel;
    private Label blueScoreLabel;
    private Label matchTimerLabel;
    
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

    private SettingsMenu settingsMenu;
    private VisualElement pingFpsCounter;
    private Label pingFpsLabel;
    private float fpsTimer = 0f;
    private int fpsCount = 0;
    private float lastFps = 0f;

    public bool IsSettingsOpen => (settingsMenu != null && settingsMenu.IsOpen) || (settingsPanel != null && settingsPanel.style.display == DisplayStyle.Flex);

    private void Start()
    {
        float musicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfxVol = PlayerPrefs.GetFloat("SFXVolume", 1f);

        InitSliderVisuals(musicFill, musicThumb, musicVol);
        InitSliderVisuals(sfxFill, sfxThumb, sfxVol);

        ApplyVolumes(musicVol, sfxVol);
    }

    void OnEnable()
    {
        Instance = this;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        root = uiDocument.rootVisualElement;
        if (root == null) return;
        
        redScoreLabel = root.Q<Label>("RedScore");
        blueScoreLabel = root.Q<Label>("BlueScore");
        matchTimerLabel = root.Q<Label>("MatchTimer");
        
        soundButton = root.Q<Button>("SoundButton");
        settingsButton = root.Q<Button>("SettingsButton");
        exitButton = root.Q<Button>("ExitButton");
        
        dynamicBar = root.Q<VisualElement>("DynamicBar");
        
        // Hook up main bar events
        soundButton?.RegisterCallback<ClickEvent>(evt => ToggleSound());
        settingsButton?.RegisterCallback<ClickEvent>(evt => OpenSettings());
        exitButton?.RegisterCallback<ClickEvent>(evt => QuitGame());

        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        if (settingsPanel != null)
        {
            settingsMenu = GetComponent<SettingsMenu>();
            if (settingsMenu == null)
            {
                settingsMenu = gameObject.AddComponent<SettingsMenu>();
            }

            settingsMenu.InitializeWithRoot(settingsPanel);
            
            // Subscribe to settings menu open/close events to keep cursor synced
            settingsMenu.OnOpenSettingsEvent -= SyncCursorOnOpen;
            settingsMenu.OnOpenSettingsEvent += SyncCursorOnOpen;

            settingsMenu.OnCloseSettingsEvent -= SyncCursorOnClose;
            settingsMenu.OnCloseSettingsEvent += SyncCursorOnClose;
        }
        
        UpdateInstructionBar("BOOST", new string[] { "CTRL", "J", "F" });
        SetCursorState(false);
    }

    void Update()
    {
        if (ForestGameManager.Instance != null)
        {
            if (redScoreLabel != null)
                redScoreLabel.text = ForestGameManager.Instance.Team1Score.Value.ToString("00");
            if (blueScoreLabel != null)
                blueScoreLabel.text = ForestGameManager.Instance.Team2Score.Value.ToString("00");
            if (matchTimerLabel != null)
            {
                float timeRemaining = ForestGameManager.Instance.MatchTimeRemaining.Value;
                int minutes = Mathf.FloorToInt(timeRemaining / 60f);
                int seconds = Mathf.FloorToInt(timeRemaining % 60f);
                matchTimerLabel.text = $"{minutes:00}:{seconds:00}";
            }
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (settingsMenu != null)
            {
                if (settingsMenu.IsOpen)
                    settingsMenu.HandleBackButton();
                else
                    settingsMenu.OpenSettings();
            }
            else if (settingsPanel != null)
            {
                if (settingsPanel.style.display == DisplayStyle.Flex)
                    CloseSettings();
                else
                    OpenSettings();
            }
        }

        // Enforce cursor state every frame while Alt is held or settings are open
        bool isSettingsOpen = (settingsMenu != null && settingsMenu.IsOpen) || (settingsPanel != null && settingsPanel.style.display == DisplayStyle.Flex);
        bool shouldShowCursor = (Keyboard.current != null && Keyboard.current.leftAltKey.isPressed) || isSettingsOpen;

        if (shouldShowCursor)
        {
            if (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                SetCursorState(true);
            }
        }
        else
        {
            if (UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorState(false);
            }
        }

        // Update Ping and FPS counter if enabled
        bool showPingFps = PlayerPrefs.GetInt("ShowPingFPS", 0) == 1;
        if (showPingFps)
        {
            if (pingFpsCounter == null && root != null)
            {
                pingFpsCounter = new VisualElement();
                pingFpsCounter.AddToClassList("ping-fps-counter");
                
                pingFpsLabel = new Label("FPS: -- | Ping: -- ms");
                pingFpsLabel.AddToClassList("ping-fps-text");
                pingFpsCounter.Add(pingFpsLabel);
                
                root.Add(pingFpsCounter);
            }

            if (pingFpsCounter != null)
            {
                pingFpsCounter.style.display = DisplayStyle.Flex;

                fpsTimer += Time.unscaledDeltaTime;
                fpsCount++;
                if (fpsTimer >= 0.5f)
                {
                    lastFps = fpsCount / fpsTimer;
                    fpsTimer = 0f;
                    fpsCount = 0;
                }

                int pingVal = 0;
                if (Unity.Netcode.NetworkManager.Singleton != null && 
                    Unity.Netcode.NetworkManager.Singleton.IsClient && 
                    Unity.Netcode.NetworkManager.Singleton.IsConnectedClient)
                {
                    var transport = Unity.Netcode.NetworkManager.Singleton.NetworkConfig.NetworkTransport;
                    if (transport is Unity.Netcode.Transports.UTP.UnityTransport utp)
                    {
                        pingVal = (int)utp.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId);
                    }
                }

                if (pingFpsLabel != null)
                {
                    string pingText = pingVal > 0 ? $"{pingVal} ms" : "-- ms";
                    pingFpsLabel.text = $"FPS: {Mathf.RoundToInt(lastFps)} | Ping: {pingText}";
                }
            }
        }
        else
        {
            if (pingFpsCounter != null)
            {
                pingFpsCounter.style.display = DisplayStyle.None;
            }
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

    private void SyncCursorOnOpen() => SetCursorState(true);
    private void SyncCursorOnClose() => SetCursorState(false);

    private void OpenSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsMenu != null)
        {
            settingsMenu.OpenSettings();
        }
        else if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.Flex;
            SetCursorState(true);
        }
    }

    private void CloseSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsMenu != null)
        {
            settingsMenu.CloseSettings();
        }
        else if (settingsPanel != null)
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

        EdgeParty.UI.StitchUIController.ReturnedFromGame = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

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

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }
}
