using System.Collections.Generic;
using EdgeParty.Infrastructure.VoiceChat;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;



public class SettingsMenu : MonoBehaviour
{
    [Header("Audio Mixer")]
    public AudioMixer mixer;

    [Header("UI Document Reference")]
    public UIDocument uiDocument;

    [Header("Settings Panel GameObject (Optional)")]
    public GameObject settingsPanel;

    // Callback when closing settings (e.g., to notify PauseMenu or StitchUIController)
    public System.Action OnCloseSettingsEvent;
    public System.Action OnOpenSettingsEvent;

    public bool IsOpen
    {
        get
        {
            if (_root != null) return _root.style.display == DisplayStyle.Flex;
            if (uiDocument != null && uiDocument.gameObject != gameObject) return uiDocument.enabled;
            if (settingsPanel != null && settingsPanel != gameObject) return settingsPanel.activeSelf;
            return false;
        }
    }
    private bool _isInitialized;
    private VisualElement _root;
    private VisualElement _settingsOverlay;
    private string _activeTab = "audio";

    private float _lastClickTime = -1f;
    private float _lastHoverTime = -1f;

    private string _rebindingActionName = null;
    private Button _rebindingButton = null;

    // Track state of custom toggles (VisualElements acting as toggle switches)
    private Dictionary<VisualElement, bool> _toggleStates = new Dictionary<VisualElement, bool>();

    // Initial values for tracking unsaved changes
    private Dictionary<string, object> _initialValues = new Dictionary<string, object>();

    private Resolution[] _resolutions;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (settingsPanel == null)
            settingsPanel = gameObject;
    }

    private void Start()
    {
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            InitializeWithRoot(uiDocument.rootVisualElement);
        }

        // Apply settings on start
        LoadAndApplyAllSettings();
    }

    private void OnEnable()
    {
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            InitializeWithRoot(uiDocument.rootVisualElement);
        }

        if (VoiceChatManager.Instance != null)
        {
            VoiceChatManager.Instance.OnVoiceReady += PopulateVoiceDevicesAsync;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // Actually apply "Mute when Unfocused" setting
        bool muteUnfocused = PlayerPrefs.GetInt("MuteUnfocused", 0) == 1;
        if (muteUnfocused)
        {
            AudioListener.pause = !hasFocus;
        }
        else
        {
            AudioListener.pause = false;
        }
    }

    public void InitializeWithRoot(VisualElement root)
    {
        if (root == null) return;
        VisualElement overlay = root.Q<VisualElement>("SettingsOverlay");
        if (overlay == null) return;
        if (_settingsOverlay == overlay)
            return;
        _settingsOverlay = overlay;
        _root = root;
        _toggleStates.Clear();

        BindTabButton("TabAudio", "audio"); 
        BindTabButton("TabGraphics", "graphics");
        BindTabButton("TabControls", "controls");
        BindTabButton("TabNetwork", "network");

        var btnBack = _root.Q<Button>("BtnBack");
        if (btnBack != null)
        {
            RegisterHoverAndClick(btnBack, HandleBackButton);
        }

        var btnResetDefaults = _root.Q<Button>("BtnResetDefaults");
        if (btnResetDefaults != null)
        {
            RegisterHoverAndClick(btnResetDefaults, ResetToDefaults);
        }

        var btnSaveChanges = _root.Q<Button>("BtnSaveChanges");
        if (btnSaveChanges != null)
        {
            RegisterHoverAndClick(btnSaveChanges, SaveChanges);
        }

        BindSlider("SliderMaster", "LabelMasterValue", "MasterVolume", 80, "%");
        BindSlider("SliderMusic", "LabelMusicValue", "MusicVolume", 65, "%");
        BindSlider("SliderSFX", "LabelSFXValue", "SFXVolume", 100, "%");
        BindSlider("SliderFPSLimit", "LabelFPSLimitValue", "FPSLimit", 144, " FPS");
        BindSlider("SliderSensitivityX", "LabelSensitivityXValue", "CameraSensitivityX", 50, "");
        BindSlider("SliderSensitivityY", "LabelSensitivityYValue", "CameraSensitivityY", 50, "");
        BindCustomToggle("ToggleVoiceChat", "VoiceChatEnabled", true);
        BindCustomToggle("ToggleMuteUnfocused", "MuteUnfocused", false);
        BindCustomToggle("ToggleVSync", "VSyncEnabled", true);
        BindCustomToggle("ToggleInvertX", "InvertCameraX", false);
        BindCustomToggle("ToggleInvertY", "InvertCameraY", false);
        BindCustomToggle("ToggleShowPing", "ShowPingFPS", false);
        BindCustomToggle("ToggleShowNames", "ShowPlayerNames", true);

        // Bind Transmission Mode Buttons
        var btnModePTT = _root.Q<Button>("BtnModePTT");
        var btnModeOpenMic = _root.Q<Button>("BtnModeOpenMic");
        if (btnModePTT != null && btnModeOpenMic != null)
        {
            RegisterHoverAndClick(btnModePTT, () => SetTransmissionMode(true));
            RegisterHoverAndClick(btnModeOpenMic, () => SetTransmissionMode(false));
        }

        SetupDropdowns();

        // Bind Keybinding buttons
        BindKeybindButton("BtnPTTKey", "KeybindPTT", "V");
        BindKeybindButton("BtnKeybindForward", "KeybindForward", "W");
        BindKeybindButton("BtnKeybindBackward", "KeybindBackward", "S");
        BindKeybindButton("BtnKeybindLeft", "KeybindLeft", "A");
        BindKeybindButton("BtnKeybindRight", "KeybindRight", "D");
        BindKeybindButton("BtnKeybindJump", "KeybindJump", "Space");

        // Load visual states from saved settings
        LoadUISettingsFromPrefs();

        // Show default tab
        SwitchTab(_activeTab);

        // Capture initial values for unsaved changes popup detection
        CaptureInitialValues();
    }

    private void RegisterHoverAndClick(Button btn, System.Action onClickAction)
    {
        if (btn == null) return;

        btn.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (Time.unscaledTime - _lastClickTime < 0.2f) return;
            if (Time.unscaledTime - _lastHoverTime < 0.15f) return;
            _lastHoverTime = Time.unscaledTime;
            EdgeParty.Core.AudioManager.Instance?.PlaySFX("Hover");
        });

        if (onClickAction != null)
        {
            btn.clicked += () =>
            {
                _lastClickTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
                onClickAction.Invoke();
            };
        }
        else
        {
            btn.clicked += () =>
            {
                _lastClickTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
            };
        }
    }

    private void BindTabButton(string buttonName, string tabName)
    {
        var btn = _root.Q<Button>(buttonName);
        if (btn != null)
        {
            RegisterHoverAndClick(btn, () => SwitchTab(tabName));
        }
    }

    private void BindSlider(string sliderName, string labelName, string prefKey, float defaultValue, string suffix)
    {
        var slider = _root.Q<Slider>(sliderName);
        var label = _root.Q<Label>(labelName);
        if (slider != null)
        {
            float savedVal = PlayerPrefs.GetFloat(prefKey, defaultValue);
            slider.value = savedVal;
            if (label != null) label.text = $"{Mathf.RoundToInt(savedVal)}{suffix}";

            slider.RegisterValueChangedCallback(evt =>
            {
                if (label != null) label.text = $"{Mathf.RoundToInt(evt.newValue)}{suffix}";
                
                // Real-time audio adjustment
                if (prefKey == "MasterVolume") ApplyMasterVolume(evt.newValue / 100f);
                else if (prefKey == "MusicVolume") ApplyMusicVolume(evt.newValue / 100f);
                else if (prefKey == "SFXVolume") ApplySFXVolume(evt.newValue / 100f);
            });
        }
    }

    private void BindCustomToggle(string toggleName, string prefKey, bool defaultValue)
    {
        var toggle = _root.Q<VisualElement>(toggleName);
        if (toggle != null)
        {
            bool savedVal = PlayerPrefs.GetInt(prefKey, defaultValue ? 1 : 0) == 1;
            SetToggleVisualState(toggle, savedVal);

            toggle.RegisterCallback<PointerDownEvent>(evt =>
            {
                bool currentVal = _toggleStates.ContainsKey(toggle) ? _toggleStates[toggle] : false;
                bool newVal = !currentVal;
                SetToggleVisualState(toggle, newVal);
                _lastClickTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
            });

            toggle.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (Time.unscaledTime - _lastClickTime < 0.2f) return;
                if (Time.unscaledTime - _lastHoverTime < 0.15f) return;
                _lastHoverTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX("Hover");
            });
        }
    }

    private void SetToggleVisualState(VisualElement toggle, bool value)
    {
        _toggleStates[toggle] = value;
        if (value)
        {
            toggle.AddToClassList("gummy-toggle--checked");
        }
        else
        {
            toggle.RemoveFromClassList("gummy-toggle--checked");
        }
    }

    private void SetupDropdowns()
    {
        // 1. Display Mode
        var dropdownDisplay = _root.Q<DropdownField>("DropdownDisplayMode");
        if (dropdownDisplay != null)
        {
            dropdownDisplay.choices = new List<string> { "Fullscreen", "Windowed", "Borderless Window" };
            int savedIndex = PlayerPrefs.GetInt("DisplayModeIndex", 0);
            dropdownDisplay.value = dropdownDisplay.choices[savedIndex];
        }

        // 2. Resolution
        var dropdownRes = _root.Q<DropdownField>("DropdownResolution");
        if (dropdownRes != null)
        {
            _resolutions = Screen.resolutions;
            var options = new List<string>();
            int currentIndex = 0;

            for (int i = 0; i < _resolutions.Length; i++)
            {
                string rText = $"{_resolutions[i].width}x{_resolutions[i].height} @ {_resolutions[i].refreshRateRatio.numerator}Hz";
                options.Add(rText);

                if (_resolutions[i].width == Screen.currentResolution.width &&
                    _resolutions[i].height == Screen.currentResolution.height)
                {
                    currentIndex = i;
                }
            }

            dropdownRes.choices = options;
            int savedIndex = PlayerPrefs.GetInt("ResolutionIndex", currentIndex);
            if (savedIndex < dropdownRes.choices.Count)
                dropdownRes.value = dropdownRes.choices[savedIndex];
        }

        // 3. Matchmaking Region
        var dropdownRegion = _root.Q<DropdownField>("DropdownRegion");
        if (dropdownRegion != null)
        {
            dropdownRegion.choices = new List<string> { "Auto (Best Ping)", "North America", "Europe", "Asia" };
            int savedIndex = PlayerPrefs.GetInt("RegionIndex", 0);
            dropdownRegion.value = dropdownRegion.choices[savedIndex];
        }

        // 4. Voice Chat Devices (Microphones)
        var dropdownInput = _root.Q<DropdownField>("DropdownInputDevice");
        if (dropdownInput != null)
        {
            var micChoices = new List<string> { "Default System Microphone" };
            foreach (var device in Microphone.devices)
            {
                micChoices.Add(device);
            }
            dropdownInput.choices = micChoices;
            int savedIndex = PlayerPrefs.GetInt("VoiceInputDeviceIndex", 0);
            if (savedIndex < dropdownInput.choices.Count)
                dropdownInput.value = dropdownInput.choices[savedIndex];
        }

        // 5. Voice Chat Playback Devices
        var dropdownOutput = _root.Q<DropdownField>("DropdownOutputDevice");
        if (dropdownOutput != null)
        {
            dropdownOutput.choices = new List<string> { "Default System Speakers", "Headphones (Rumble Pro)", "HDMI Audio Device" };
            int savedIndex = PlayerPrefs.GetInt("VoiceOutputDeviceIndex", 0);
            dropdownOutput.value = dropdownOutput.choices[savedIndex];
        }

        // Dynamically fetch and populate voice devices from Vivox if possible
        PopulateVoiceDevicesAsync();
    }

    private void PopulateVoiceDevicesAsync()
    {
        if (EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance == null || 
            !EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance.IsReady) return;

        try
        {
            // 1. Input Devices (Microphones)
            var dropdownInput = _root?.Q<DropdownField>("DropdownInputDevice");
            if (dropdownInput != null)
            {
                var inputDevices = VivoxService.Instance.AvailableInputDevices;
                var micChoices = new List<string>();
                int selectedIndex = 0;
                string savedName = PlayerPrefs.GetString("VoiceInputDeviceName", "Default System Microphone");

                micChoices.Add("Default System Microphone");
                if (inputDevices != null)
                {
                    for (int i = 0; i < inputDevices.Count; i++)
                    {
                        string devName = inputDevices[i].DeviceName;
                        micChoices.Add(devName);
                        if (devName == savedName)
                        {
                            selectedIndex = i + 1; // 1-based index because of Default
                        }
                    }
                }
                dropdownInput.choices = micChoices;
                if (selectedIndex < micChoices.Count)
                    dropdownInput.value = micChoices[selectedIndex];
                else
                    dropdownInput.value = micChoices[0];
            }

            // 2. Output Devices (Playback)
            var dropdownOutput = _root?.Q<DropdownField>("DropdownOutputDevice");
            if (dropdownOutput != null)
            {
                var outputDevices = VivoxService.Instance.AvailableOutputDevices;
                var speakerChoices = new List<string>();
                int selectedIndex = 0;
                string savedName = PlayerPrefs.GetString("VoiceOutputDeviceName", "Default System Speakers");

                speakerChoices.Add("Default System Speakers");
                if (outputDevices != null)
                {
                    for (int i = 0; i < outputDevices.Count; i++)
                    {
                        string devName = outputDevices[i].DeviceName;
                        speakerChoices.Add(devName);
                        if (devName == savedName)
                        {
                            selectedIndex = i + 1; // 1-based index
                        }
                    }
                }
                dropdownOutput.choices = speakerChoices;
                if (selectedIndex < speakerChoices.Count)
                    dropdownOutput.value = speakerChoices[selectedIndex];
                else
                    dropdownOutput.value = speakerChoices[0];
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[SettingsMenu] Failed to query dynamic Vivox devices: {ex.Message}");
        }
    }

    private void SetTransmissionMode(bool pushToTalk)
    {
        var btnModePTT = _root.Q<Button>("BtnModePTT");
        var btnModeOpenMic = _root.Q<Button>("BtnModeOpenMic");

        if (pushToTalk)
        {
            btnModePTT?.AddToClassList("active-mode");
            btnModeOpenMic?.RemoveFromClassList("active-mode");
            PlayerPrefs.SetInt("TransmissionModePTT", 1);
        }
        else
        {
            btnModePTT?.RemoveFromClassList("active-mode");
            btnModeOpenMic?.AddToClassList("active-mode");
            PlayerPrefs.SetInt("TransmissionModePTT", 0);
        }
    }

    private void BindKeybindButton(string buttonName, string prefKey, string defaultKey)
    {
        var btn = _root.Q<Button>(buttonName);
        if (btn != null)
        {
            string savedKey = PlayerPrefs.GetString(prefKey, defaultKey);
            btn.text = savedKey;

            RegisterHoverAndClick(btn, () => StartRebinding(prefKey, btn));
        }
    }

    private void StartRebinding(string actionName, Button btn)
    {
        if (_rebindingActionName != null) return; // Already rebinding

        _rebindingActionName = actionName;
        _rebindingButton = btn;
        btn.text = "<Press Key>";
    }

    private void OnGUI()
    {
        if (_rebindingActionName == null || _rebindingButton == null) return;

        Event e = Event.current;
        if (e != null && e.isKey && e.type == EventType.KeyDown)
        {
            // Stop rebind on Escape, otherwise assign
            string pressedKey = e.keyCode.ToString();
            if (e.keyCode == KeyCode.Escape)
            {
                // Revert
                pressedKey = PlayerPrefs.GetString(_rebindingActionName, "Space");
            }

            _rebindingButton.text = pressedKey;
            PlayerPrefs.SetString(_rebindingActionName, pressedKey);
            
            Debug.Log($"[SettingsMenu] Rebound action {_rebindingActionName} to: {pressedKey}");

            // Clear state
            _rebindingActionName = null;
            _rebindingButton = null;

            e.Use();
        }
    }

    public void SwitchTab(string tabId)
    {
        _activeTab = tabId;
        if (_root == null) return;

        // Toggle visibility of panels
        TogglePanel("ContentAudio", tabId == "audio");
        TogglePanel("ContentGraphics", tabId == "graphics");
        TogglePanel("ContentControls", tabId == "controls");
        TogglePanel("ContentNetwork", tabId == "network");

        // Reset tab button states
        ResetTabButtonState("TabAudio", tabId == "audio");
        ResetTabButtonState("TabGraphics", tabId == "graphics");
        ResetTabButtonState("TabControls", tabId == "controls");
        ResetTabButtonState("TabNetwork", tabId == "network");

        // Update header label text and icon class
        var headerLabel = _root.Q<Label>("MainHeaderTitle");
        var headerIcon = _root.Q<VisualElement>("MainHeaderIcon");
        if (headerLabel != null)
        {
            headerLabel.text = tabId switch
            {
                "audio" => "Audio Settings",
                "graphics" => "Graphics Settings",
                "controls" => "Controls Settings",
                "network" => "Network Settings",
                _ => "Settings"
            };
        }
        if (headerIcon != null)
        {
            // Remove previous icon classes
            headerIcon.RemoveFromClassList("icon-volume-up");
            headerIcon.RemoveFromClassList("icon-monitor");
            headerIcon.RemoveFromClassList("icon-videogame-asset");
            headerIcon.RemoveFromClassList("icon-language");

            // Add the new icon class
            string iconClass = tabId switch
            {
                "audio" => "icon-volume-up",
                "graphics" => "icon-monitor",
                "controls" => "icon-videogame-asset",
                "network" => "icon-language",
                _ => "icon-settings"
            };
            headerIcon.AddToClassList(iconClass);
        }
    }

    private void TogglePanel(string panelName, bool show)
    {
        var panel = _root.Q<VisualElement>(panelName);
        if (panel != null)
        {
            panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void ResetTabButtonState(string btnName, bool isActive)
    {
        var btn = _root.Q<Button>(btnName);
        if (btn != null)
        {
            if (isActive)
            {
                btn.AddToClassList("active-tab");
            }
            else
            {
                btn.RemoveFromClassList("active-tab");
            }

            // Update icon tint color dynamically
            var icon = btn.Q<VisualElement>(className: "settings-nav-tab-icon");
            if (icon != null)
            {
                icon.style.unityBackgroundImageTintColor = isActive 
                    ? new StyleColor(new Color(0.44f, 0.37f, 0f)) // #705e00
                    : new StyleColor(new Color(0.30f, 0.28f, 0.20f)); // #4d4732
            }
        }
    }

    private void LoadUISettingsFromPrefs()
    {
        // 1. Audio Transmission Mode
        bool ptt = PlayerPrefs.GetInt("TransmissionModePTT", 1) == 1;
        SetTransmissionMode(ptt);
    }

    public void SaveChanges()
    {
        if (_root == null) return;

        // Save slider states
        SaveSliderValue("SliderMaster", "MasterVolume");
        SaveSliderValue("SliderMusic", "MusicVolume");
        SaveSliderValue("SliderSFX", "SFXVolume");
        SaveSliderValue("SliderFPSLimit", "FPSLimit");
        SaveSliderValue("SliderSensitivityX", "CameraSensitivityX");
        SaveSliderValue("SliderSensitivityY", "CameraSensitivityY");

        // Save toggle states
        SaveToggleValue("ToggleVoiceChat", "VoiceChatEnabled");
        SaveToggleValue("ToggleMuteUnfocused", "MuteUnfocused");
        SaveToggleValue("ToggleVSync", "VSyncEnabled");
        SaveToggleValue("ToggleInvertX", "InvertCameraX");
        SaveToggleValue("ToggleInvertY", "InvertCameraY");
        SaveToggleValue("ToggleShowPing", "ShowPingFPS");
        SaveToggleValue("ToggleShowNames", "ShowPlayerNames");

        // Save Dropdown indexes
        SaveDropdownIndex("DropdownDisplayMode", "DisplayModeIndex");
        SaveDropdownIndex("DropdownResolution", "ResolutionIndex");
        SaveDropdownIndex("DropdownRegion", "RegionIndex");

        var dropdownInput = _root.Q<DropdownField>("DropdownInputDevice");
        if (dropdownInput != null)
        {
            PlayerPrefs.SetInt("VoiceInputDeviceIndex", dropdownInput.index);
            PlayerPrefs.SetString("VoiceInputDeviceName", dropdownInput.value);
        }

        var dropdownOutput = _root.Q<DropdownField>("DropdownOutputDevice");
        if (dropdownOutput != null)
        {
            PlayerPrefs.SetInt("VoiceOutputDeviceIndex", dropdownOutput.index);
            PlayerPrefs.SetString("VoiceOutputDeviceName", dropdownOutput.value);
        }

        // Apply immediately
        LoadAndApplyAllSettings();

        PlayerPrefs.Save();
        Debug.Log("[SettingsMenu] Saved all settings successfully!");

        // Update captured initial values
        CaptureInitialValues();

        // Close after saving
        CloseSettings();
    }

    private void SaveSliderValue(string sliderName, string prefKey)
    {
        var slider = _root.Q<Slider>(sliderName);
        if (slider != null) PlayerPrefs.SetFloat(prefKey, slider.value);
    }

    private void SaveToggleValue(string toggleName, string prefKey)
    {
        var toggle = _root.Q<VisualElement>(toggleName);
        if (toggle != null && _toggleStates.ContainsKey(toggle))
        {
            PlayerPrefs.SetInt(prefKey, _toggleStates[toggle] ? 1 : 0);
        }
    }

    private void SaveDropdownIndex(string dropdownName, string prefKey)
    {
        var dropdown = _root.Q<DropdownField>(dropdownName);
        if (dropdown != null)
        {
            PlayerPrefs.SetInt(prefKey, dropdown.index);
        }
    }

    public void ResetToDefaults()
    {
        // Reset Prefs
        PlayerPrefs.DeleteKey("MasterVolume");
        PlayerPrefs.DeleteKey("MusicVolume");
        PlayerPrefs.DeleteKey("SFXVolume");
        PlayerPrefs.DeleteKey("FPSLimit");
        PlayerPrefs.DeleteKey("CameraSensitivityX");
        PlayerPrefs.DeleteKey("CameraSensitivityY");
        PlayerPrefs.DeleteKey("VoiceChatEnabled");
        PlayerPrefs.DeleteKey("MuteUnfocused");
        PlayerPrefs.DeleteKey("VSyncEnabled");
        PlayerPrefs.DeleteKey("InvertCameraX");
        PlayerPrefs.DeleteKey("InvertCameraY");
        PlayerPrefs.DeleteKey("ShowPingFPS");
        PlayerPrefs.DeleteKey("ShowPlayerNames");
        PlayerPrefs.DeleteKey("DisplayModeIndex");
        PlayerPrefs.DeleteKey("ResolutionIndex");
        PlayerPrefs.DeleteKey("RegionIndex");
        PlayerPrefs.DeleteKey("VoiceInputDeviceIndex");
        PlayerPrefs.DeleteKey("VoiceOutputDeviceIndex");
        PlayerPrefs.DeleteKey("VoiceInputDeviceName");
        PlayerPrefs.DeleteKey("VoiceOutputDeviceName");
        PlayerPrefs.DeleteKey("TransmissionModePTT");
        PlayerPrefs.DeleteKey("KeybindPTT");
        PlayerPrefs.DeleteKey("KeybindForward");
        PlayerPrefs.DeleteKey("KeybindBackward");
        PlayerPrefs.DeleteKey("KeybindLeft");
        PlayerPrefs.DeleteKey("KeybindRight");
        PlayerPrefs.DeleteKey("KeybindJump");

        // Reload UI
        if (_root != null) ReloadUIFromPrefs();

        LoadAndApplyAllSettings();
        Debug.Log("[SettingsMenu] Reset all settings to defaults!");
    }

    public void LoadAndApplyAllSettings()
    {
        // 1. Audio
        float master = PlayerPrefs.GetFloat("MasterVolume", 80f) / 100f;
        float music = PlayerPrefs.GetFloat("MusicVolume", 65f) / 100f;
        float sfx = PlayerPrefs.GetFloat("SFXVolume", 100f) / 100f;

        ApplyMasterVolume(master);
        ApplyMusicVolume(music);
        ApplySFXVolume(sfx);

        // Voice Chat toggle integration
        bool voiceChat = PlayerPrefs.GetInt("VoiceChatEnabled", 1) == 1;
        if (EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance != null)
        {
            EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance.SetMute(!voiceChat);
        }

        // Apply mic/playback devices
        ApplyVoiceDevices();

        // 2. Graphics
        int displayMode = PlayerPrefs.GetInt("DisplayModeIndex", 0);
        FullScreenMode fsm = displayMode switch
        {
            0 => FullScreenMode.FullScreenWindow,
            1 => FullScreenMode.Windowed,
            2 => FullScreenMode.ExclusiveFullScreen,
            _ => FullScreenMode.FullScreenWindow
        };

        if (_resolutions == null)
        {
            _resolutions = Screen.resolutions;
        }

        int resIndex = PlayerPrefs.GetInt("ResolutionIndex", -1);
        if (resIndex >= 0 && _resolutions != null && resIndex < _resolutions.Length)
        {
            Resolution r = _resolutions[resIndex];
            Screen.SetResolution(r.width, r.height, fsm);
        }
        else
        {
            Screen.fullScreenMode = fsm;
        }

        int fpsLimit = Mathf.RoundToInt(PlayerPrefs.GetFloat("FPSLimit", 144f));
        Application.targetFrameRate = fpsLimit;

        bool vsync = PlayerPrefs.GetInt("VSyncEnabled", 1) == 1;
        QualitySettings.vSyncCount = vsync ? 1 : 0;
    }

    private async void ApplyVoiceDevices()
    {
        if (EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance == null || 
            !EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance.IsReady) return;

        try
        {
            // Set Input device
            string inputDevName = PlayerPrefs.GetString("VoiceInputDeviceName", "Default System Microphone");
            var inputDevices = VivoxService.Instance.AvailableInputDevices;
            if (inputDevices != null)
            {
                if (inputDevName == "Default System Microphone")
                {
                    foreach (var dev in inputDevices)
                    {
                        if (dev.DeviceName.IndexOf("default", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dev.DeviceName.IndexOf("system", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            await VivoxService.Instance.SetActiveInputDeviceAsync(dev);
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var dev in inputDevices)
                    {
                        if (dev.DeviceName == inputDevName)
                        {
                            await VivoxService.Instance.SetActiveInputDeviceAsync(dev);
                            break;
                        }
                    }
                }
            }

            // Set Output device
            string outputDevName = PlayerPrefs.GetString("VoiceOutputDeviceName", "Default System Speakers");
            var outputDevices = VivoxService.Instance.AvailableOutputDevices;
            if (outputDevices != null)
            {
                if (outputDevName == "Default System Speakers")
                {
                    foreach (var dev in outputDevices)
                    {
                        if (dev.DeviceName.IndexOf("default", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dev.DeviceName.IndexOf("system", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            await VivoxService.Instance.SetActiveOutputDeviceAsync(dev);
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var dev in outputDevices)
                    {
                        if (dev.DeviceName == outputDevName)
                        {
                            await VivoxService.Instance.SetActiveOutputDeviceAsync(dev);
                            break;
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[SettingsMenu] Failed to apply Vivox devices: {ex.Message}");
        }
    }

    private void ApplyMasterVolume(float value)
    {
        if (mixer == null) return;
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("MasterVolume", db);
    }

    private void ApplyMusicVolume(float value)
    {
        if (mixer == null) return;
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("MusicVolume", db);
    }

    private void ApplySFXVolume(float value)
    {
        if (mixer == null) return;
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("SFXVolume", db);
    }
    private void ReloadUIFromPrefs()
    {
        BindSliderValue("SliderMaster", "LabelMasterValue", "MasterVolume", "%", 80);
        BindSliderValue("SliderMusic", "LabelMusicValue", "MusicVolume", "%", 65);
        BindSliderValue("SliderSFX", "LabelSFXValue", "SFXVolume", "%", 100);

        LoadUISettingsFromPrefs();
        SetupDropdowns();

        CaptureInitialValues();
    }
    private void BindSliderValue(
      string sliderName,
      string labelName,
      string prefKey,
      string suffix,
      float defaultValue)
    {
        var slider = _root.Q<Slider>(sliderName);
        var label = _root.Q<Label>(labelName);

        if (slider == null)
            return;

        float value = PlayerPrefs.GetFloat(prefKey, defaultValue);

        slider.SetValueWithoutNotify(value);

        if (label != null)
            label.text = $"{Mathf.RoundToInt(value)}{suffix}";
    }
    // ===================== UNSAVED CHANGES TRACKING =====================

    private void CaptureInitialValues()
    {
        _initialValues.Clear();
        
        CaptureSlider("SliderMaster");
        CaptureSlider("SliderMusic");
        CaptureSlider("SliderSFX");
        CaptureSlider("SliderFPSLimit");
        CaptureSlider("SliderSensitivityX");
        CaptureSlider("SliderSensitivityY");

        CaptureToggle("ToggleVoiceChat");
        CaptureToggle("ToggleMuteUnfocused");
        CaptureToggle("ToggleVSync");
        CaptureToggle("ToggleInvertX");
        CaptureToggle("ToggleInvertY");
        CaptureToggle("ToggleShowPing");
        CaptureToggle("ToggleShowNames");

        CaptureDropdown("DropdownDisplayMode");
        CaptureDropdown("DropdownResolution");
        CaptureDropdown("DropdownRegion");
        CaptureDropdown("DropdownInputDevice");
        CaptureDropdown("DropdownOutputDevice");

        var btnModePTT = _root.Q<Button>("BtnModePTT");
        if (btnModePTT != null)
        {
            _initialValues["TransmissionModePTT"] = btnModePTT.ClassListContains("active-mode");
        }

        CaptureKeybind("BtnPTTKey");
        CaptureKeybind("BtnKeybindForward");
        CaptureKeybind("BtnKeybindBackward");
        CaptureKeybind("BtnKeybindLeft");
        CaptureKeybind("BtnKeybindRight");
        CaptureKeybind("BtnKeybindJump");
    }

    private void CaptureSlider(string name)
    {
        var slider = _root.Q<Slider>(name);
        if (slider != null) _initialValues[name] = slider.value;
    }

    private void CaptureToggle(string name)
    {
        var toggle = _root.Q<VisualElement>(name);
        if (toggle != null)
        {
            _initialValues[name] = _toggleStates.ContainsKey(toggle) ? _toggleStates[toggle] : false;
        }
    }

    private void CaptureDropdown(string name)
    {
        var dropdown = _root.Q<DropdownField>(name);
        if (dropdown != null) _initialValues[name] = dropdown.index;
    }

    private void CaptureKeybind(string name)
    {
        var btn = _root.Q<Button>(name);
        if (btn != null) _initialValues[name] = btn.text;
    }

    private bool HasUnsavedChanges()
    {
        if (_root == null) return false;

        if (CheckSliderChanged("SliderMaster")) return true;
        if (CheckSliderChanged("SliderMusic")) return true;
        if (CheckSliderChanged("SliderSFX")) return true;
        if (CheckSliderChanged("SliderFPSLimit")) return true;
        if (CheckSliderChanged("SliderSensitivityX")) return true;
        if (CheckSliderChanged("SliderSensitivityY")) return true;

        if (CheckToggleChanged("ToggleVoiceChat")) return true;
        if (CheckToggleChanged("ToggleMuteUnfocused")) return true;
        if (CheckToggleChanged("ToggleVSync")) return true;
        if (CheckToggleChanged("ToggleInvertX")) return true;
        if (CheckToggleChanged("ToggleInvertY")) return true;
        if (CheckToggleChanged("ToggleShowPing")) return true;
        if (CheckToggleChanged("ToggleShowNames")) return true;

        if (CheckDropdownChanged("DropdownDisplayMode")) return true;
        if (CheckDropdownChanged("DropdownResolution")) return true;
        if (CheckDropdownChanged("DropdownRegion")) return true;
        if (CheckDropdownChanged("DropdownInputDevice")) return true;
        if (CheckDropdownChanged("DropdownOutputDevice")) return true;

        var btnModePTT = _root.Q<Button>("BtnModePTT");
        if (btnModePTT != null && _initialValues.ContainsKey("TransmissionModePTT"))
        {
            bool initialPTT = (bool)_initialValues["TransmissionModePTT"];
            bool currentPTT = btnModePTT.ClassListContains("active-mode");
            if (initialPTT != currentPTT) return true;
        }

        if (CheckKeybindChanged("BtnPTTKey")) return true;
        if (CheckKeybindChanged("BtnKeybindForward")) return true;
        if (CheckKeybindChanged("BtnKeybindBackward")) return true;
        if (CheckKeybindChanged("BtnKeybindLeft")) return true;
        if (CheckKeybindChanged("BtnKeybindRight")) return true;
        if (CheckKeybindChanged("BtnKeybindJump")) return true;

        return false;
    }

    private bool CheckSliderChanged(string name)
    {
        var slider = _root.Q<Slider>(name);
        if (slider != null && _initialValues.ContainsKey(name))
        {
            return Mathf.Abs(slider.value - (float)_initialValues[name]) > 0.01f;
        }
        return false;
    }

    private bool CheckToggleChanged(string name)
    {
        var toggle = _root.Q<VisualElement>(name);
        if (toggle != null && _initialValues.ContainsKey(name))
        {
            bool current = _toggleStates.ContainsKey(toggle) ? _toggleStates[toggle] : false;
            return current != (bool)_initialValues[name];
        }
        return false;
    }

    private bool CheckDropdownChanged(string name)
    {
        var dropdown = _root.Q<DropdownField>(name);
        if (dropdown != null && _initialValues.ContainsKey(name))
        {
            return dropdown.index != (int)_initialValues[name];
        }
        return false;
    }

    private bool CheckKeybindChanged(string name)
    {
        var btn = _root.Q<Button>(name);
        if (btn != null && _initialValues.ContainsKey(name))
        {
            return btn.text != (string)_initialValues[name];
        }
        return false;
    }

    public void HandleBackButton()
    {
        if (HasUnsavedChanges())
        {
            ShowUnsavedChangesPopup();
        }
        else
        {
            CloseSettings();
        }
    }

    private void ShowUnsavedChangesPopup()
    {
        // 1. Create full-screen modal overlay
        VisualElement popupOverlay = new VisualElement();
        popupOverlay.style.position = Position.Absolute;
        popupOverlay.style.top = 0;
        popupOverlay.style.left = 0;
        popupOverlay.style.right = 0;
        popupOverlay.style.bottom = 0;
        popupOverlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.6f));
        popupOverlay.style.alignItems = Align.Center;
        popupOverlay.style.justifyContent = Justify.Center;

        // Block input events below this overlay
        popupOverlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

        // 2. Create card panel
        VisualElement card = new VisualElement();
        card.AddToClassList("tactile-panel");
        card.style.width = 460;
        card.style.paddingTop = 32;
        card.style.paddingBottom = 32;
        card.style.paddingLeft = 32;
        card.style.paddingRight = 32;
        card.style.backgroundColor = new StyleColor(new Color(1f, 0.98f, 0.92f));
        card.style.borderTopWidth = 4;
        card.style.borderBottomWidth = 4;
        card.style.borderLeftWidth = 4;
        card.style.borderRightWidth = 4;
        card.style.borderTopColor = new StyleColor(new Color(0.8f, 0.7f, 0.5f));
        card.style.borderBottomColor = new StyleColor(new Color(0.8f, 0.7f, 0.5f));
        card.style.borderLeftColor = new StyleColor(new Color(0.8f, 0.7f, 0.5f));
        card.style.borderRightColor = new StyleColor(new Color(0.8f, 0.7f, 0.5f));
        card.style.borderTopLeftRadius = 24;
        card.style.borderTopRightRadius = 24;
        card.style.borderBottomLeftRadius = 24;
        card.style.borderBottomRightRadius = 24;

        // Title
        Label title = new Label("Unsaved Changes");
        title.AddToClassList("font-headline");
        title.style.fontSize = 24;
        title.style.color = new StyleColor(new Color(0.2f, 0.15f, 0.05f));
        title.style.marginBottom = 12;
        card.Add(title);

        // Description
        Label desc = new Label("You have unsaved changes. Would you like to save them before leaving?");
        desc.AddToClassList("font-body");
        desc.style.fontSize = 15;
        desc.style.color = new StyleColor(new Color(0.35f, 0.3f, 0.2f));
        desc.style.whiteSpace = WhiteSpace.Normal;
        desc.style.marginBottom = 24;
        card.Add(desc);

        // Buttons row
        VisualElement btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.SpaceBetween;

        // Save button
        Button btnSave = new Button();
        btnSave.text = "Save";
        btnSave.AddToClassList("bouncy-btn");
        btnSave.AddToClassList("btn-primary-3d");
        btnSave.style.flexGrow = 1;
        btnSave.style.marginRight = 8;
        btnSave.clicked += () =>
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
            _root.Remove(popupOverlay);
            SaveChanges(); // Saves and closes settings
        };
        btnRow.Add(btnSave);

        // Don't Save button
        Button btnDontSave = new Button();
        btnDontSave.text = "Don't Save";
        btnDontSave.AddToClassList("bouncy-btn");
        btnDontSave.AddToClassList("btn-surface-3d");
        btnDontSave.style.flexGrow = 1;
        btnDontSave.style.marginRight = 8;
        btnDontSave.clicked += () =>
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");

            _root.Remove(popupOverlay);

            ReloadUIFromPrefs();

            LoadAndApplyAllSettings();

            CloseSettings();
        };
        btnRow.Add(btnDontSave);

        // Cancel button
        Button btnCancel = new Button();
        btnCancel.text = "Cancel";
        btnCancel.AddToClassList("bouncy-btn");
        btnCancel.AddToClassList("btn-surface-3d");
        btnCancel.style.flexGrow = 1;
        btnCancel.clicked += () =>
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
            _root.Remove(popupOverlay);
        };
        btnRow.Add(btnCancel);

        card.Add(btnRow);
        popupOverlay.Add(card);
        _root.Add(popupOverlay);
    }

    // ===================== CONTROL METHODS =====================

    public void OpenSettings()
    {
        if (settingsPanel != null && settingsPanel != gameObject)
        {
            settingsPanel.SetActive(true);
        }
        
        if (_root != null)
        {
            _root.style.display = DisplayStyle.Flex;
        }

        if (uiDocument != null && uiDocument.gameObject != gameObject)
        {
            uiDocument.enabled = true;
            InitializeWithRoot(uiDocument.rootVisualElement);
        }

        OnOpenSettingsEvent?.Invoke();
    }

    public void CloseSettings()
    {
        if (uiDocument != null && uiDocument.gameObject != gameObject)
        {
            uiDocument.enabled = false;
        }

        if (settingsPanel != null && settingsPanel != gameObject)
        {
            settingsPanel.SetActive(false);
        }

        if (_root != null)
        {
            _root.style.display = DisplayStyle.None;
        }

        OnCloseSettingsEvent?.Invoke();
    }

    public void ToggleSettings()
    {
        bool isActive = false;
        if (uiDocument != null)
        {
            isActive = uiDocument.enabled;
        }
        else if (settingsPanel != null)
        {
            isActive = settingsPanel.activeSelf;
        }

        if (isActive) HandleBackButton(); // Always verify unsaved changes when toggling off
        else OpenSettings();
    }
    //Refresh Vivox devices khi login xong
 
    private void OnDisable()
    {
        if (VoiceChatManager.Instance != null)
        {
            VoiceChatManager.Instance.OnVoiceReady -= PopulateVoiceDevicesAsync;
        }
    }
    private void OnDestroy()
    {
        OnCloseSettingsEvent = null;
        OnOpenSettingsEvent = null;
    }
}