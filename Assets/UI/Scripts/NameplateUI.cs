using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NameplateUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text playerNameText;
    public Image micImage;
    [Tooltip("Background panel của nameplate để đổi màu theo team")]
    public Image backgroundImage;

    // Team colors
    public static readonly Color Team1Color = new Color(0.9f, 0.2f, 0.2f, 0.85f); // Đỏ
    public static readonly Color Team2Color = new Color(0.2f, 0.5f, 0.9f, 0.85f); // Xanh
    public static readonly Color DefaultColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);

    public void SetPlayerName(string playerName)
    {
        if (playerNameText != null)
            playerNameText.text = playerName;
    }

    /// <summary>Đổi màu nameplate theo team. teamID = 1 (Đỏ) hoặc 2 (Xanh).</summary>
    public void SetTeamColor(int teamID)
    {
        if (backgroundImage == null) return;
        backgroundImage.color = teamID switch
        {
            1 => Team1Color,
            2 => Team2Color,
            _ => DefaultColor
        };
    }

    public void SetMicLevel(float value)
    {
        if (micImage == null) return;

        value = Mathf.Clamp(value, 0f, 100f);
        float t = value / 100f;

        Color minColor = new Color(0f, 0.25f, 0f);
        Color maxColor = new Color(0f, 1f, 0f);

        micImage.color = Color.Lerp(minColor, maxColor, t);
    }
}