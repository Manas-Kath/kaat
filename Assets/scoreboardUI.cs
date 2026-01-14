using UnityEngine;
using TMPro;

public class ScoreboardUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panelRoot; 
    public TextMeshProUGUI[] playerRowTexts; 

    // Internal cache for names so we don't lose them when score updates
    private string[] currentNames = new string[] { "Player 0", "Player 1", "Player 2", "Player 3" };

    private void Start()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        // Initialize with default values
        UpdateValues(new int[4], -1, -1); 
    }

    // Called when Scores change (End of round, etc.)
    public void UpdateScoreboard(int[] scores, int dealerIndex, int myPlayerIndex)
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        UpdateValues(scores, dealerIndex, myPlayerIndex);
    }

    // Called when Players join/leave or names sync
    public void UpdateNames(string[] newNames)
    {
        for (int i = 0; i < newNames.Length; i++)
        {
            if (i < currentNames.Length) currentNames[i] = newNames[i];
        }
        // Force a redraw with existing scores (passing null scores won't break if we handle it, 
        // but easier to just wait for next score update or trigger a refresh if you stored scores.
        // For now, we update names and wait for the score update, or you can call UpdateValues here with cached scores if needed.)
    }

    private void UpdateValues(int[] scores, int dealerIndex, int myPlayerIndex)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i >= playerRowTexts.Length) break;

            // 1. Get Name (from cache)
            string displayName = currentNames[i];
            
            // 2. Add "YOU" marker if it's local player
            if (i == myPlayerIndex) 
            {
                displayName = $"<color=yellow>{displayName} (You)</color>";
            }

            // 3. Add Dealer Marker
            string dealerMarker = (i == dealerIndex) ? " <color=orange>[D]</color>" : "";

            // 4. Set Text: "Bob (You) [D]: 150"
            playerRowTexts[i].text = $"{displayName}{dealerMarker}: {scores[i]}";
        }
    }
}