using UnityEngine;
using UnityEngine.InputSystem;

public class PauseMenu : MonoBehaviour
{
    [Header("References")]
    // Kéo SettingsMenu component vào đây
    public SettingsMenu settingsMenu;

    private void Start()
    {
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (settingsMenu != null)
        {
            if (settingsMenu.IsOpen)
            {
                // Trigger check for unsaved changes
                settingsMenu.HandleBackButton();
            }
            else
            {
                settingsMenu.OpenSettings();
            }
        }
    }

    public void OpenPause()
    {
        settingsMenu?.OpenSettings();
    }

    public void ClosePause()
    {
        settingsMenu?.CloseSettings();
    }
}