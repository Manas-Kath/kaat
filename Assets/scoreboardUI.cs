using UnityEngine;
using TMPro;

public class ScoreboardUI : MonoBehaviour
{
    [Header("UI References")]
    // Drag your 4 Text objects here in the Inspector
    public TextMeshProUGUI[] playerRowTexts; 

    public void UpdateScoreboard(int[] scores, int dealerIndex, int myPlayerIndex)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i >= playerRowTexts.Length) break;

            string prefix = $"P{i}";
            
            // highlight the local player
            if (i == myPlayerIndex) prefix = "YOU";

            // Mark the dealer
            string dealerMarker = (i == dealerIndex) ? " [D]" : "";

            playerRowTexts[i].text = $"{prefix}{dealerMarker}: {scores[i]}";
            
            // Optional: Highlight winning player in Green, losing in Red
            // (Simple version just updates text)
        }
    }
}