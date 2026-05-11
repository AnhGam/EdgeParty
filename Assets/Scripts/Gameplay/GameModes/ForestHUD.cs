using UnityEngine;
using TMPro;

public class ForestHUD : MonoBehaviour
{
    public TMP_Text scoreT1Text;
    public TMP_Text scoreT2Text; 

    void Update()
    {
        if (ForestGameManager.Instance != null)
        {
            // Luôn cập nhật con số mới nhất từ NetworkVariable
            scoreT1Text.text = $"Đội Đỏ: {ForestGameManager.Instance.Team1Score.Value}";
            scoreT2Text.text = $"Đội Xanh: {ForestGameManager.Instance.Team2Score.Value}";
        }
    }
}