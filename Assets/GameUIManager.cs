using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using uVegas.Core.Cards; // Necessary for 'Suit' enum

public class GameUIManager : MonoBehaviour
{
    [Header("Bidding & Info")]
    [Tooltip("Drag 4 Text objects here. Order: 0=Bottom, 1=Right, 2=Top, 3=Left")]
    public TextMeshProUGUI[] playerBidTexts; 

    [Tooltip("The Image component (top-left) that displays the active Trump suit")]
    public Image powerSuitIcon;              

    [Tooltip("Drag 4 Sprites. Must match Suit Enum order: 0=Hearts, 1=Diamonds, 2=Clubs, 3=Spades")]
    public Sprite[] suitSprites;             

    [Header("Feedback")]
    [Tooltip("Drag 4 Glow Images (behind players). Order: 0=Bottom, 1=Right, 2=Top, 3=Left")]
    public GameObject[] winnerGlows;         

    /// <summary>
    /// Updates the bid text for a specific player.
    /// </summary>
    public void SetBidText(int playerIndex, string text)
    {
        // Safety Checks to prevent errors
        if (playerBidTexts == null || playerIndex < 0 || playerIndex >= playerBidTexts.Length)
            return;

        if (playerBidTexts[playerIndex] != null)
        {
            playerBidTexts[playerIndex].text = text;
        }
    }

    /// <summary>
    /// Clears the bid text for all players (e.g., at the start of a round).
    /// </summary>
    public void ClearAllBids()
    {
        if (playerBidTexts == null) return;

        foreach (var t in playerBidTexts)
        {
            if (t != null) t.text = "";
        }
    }

    /// <summary>
    /// Updates the Trump/Power icon based on the suit provided.
    /// </summary>
    public void SetPowerSuitDisplay(Suit suit)
    {
        if (powerSuitIcon == null) return;
        if (suitSprites == null) return;

        int index = (int)suit;

        // Check if the suit index is valid for our sprite array
        // (This handles cases like 'Hidden' or 'Joker' gracefully by hiding the icon)
        if (index >= 0 && index < suitSprites.Length)
        {
            powerSuitIcon.sprite = suitSprites[index];
            powerSuitIcon.gameObject.SetActive(true);
        }
        else
        {
            // If we don't have a sprite for this suit (e.g. Hidden), hide the image
            powerSuitIcon.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Flashes the winner's glow for 2 seconds.
    /// </summary>
    public IEnumerator AnimateWinnerGlow(int winnerIndex)
    {
        // Safety checks
        if (winnerGlows == null || winnerIndex < 0 || winnerIndex >= winnerGlows.Length)
            yield break;

        if (winnerGlows[winnerIndex] != null)
        {
            winnerGlows[winnerIndex].SetActive(true);
            yield return new WaitForSeconds(2.0f); // Glow duration
            if (winnerGlows[winnerIndex] != null)
                winnerGlows[winnerIndex].SetActive(false);
        }
    }
}