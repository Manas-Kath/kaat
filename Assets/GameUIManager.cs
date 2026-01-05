using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameUIManager : MonoBehaviour
{
    //huhhh
    [Header("Bidding & Info")]
    public TextMeshProUGUI[] playerBidTexts; // Drag 4 text objects here
    public Image powerSuitIcon;              // Drag top-left Image here
    public Sprite[] suitSprites;             // Drag 4 sprites (Spade, Heart, Club, Diamond)

    [Header("Feedback")]
    public GameObject[] winnerGlows;         // Drag 4 Glow Images (behind players)

    // Call this to update a specific player's bid text
    public void SetBidText(int playerIndex, string text)
    {
        if (playerBidTexts[playerIndex] != null)
            playerBidTexts[playerIndex].text = text;
    }

    public void ClearAllBids()
    {
        foreach (var t in playerBidTexts) t.text = "";
    }

    public void SetPowerSuitDisplay(Suit suit)
    {
        int index = (int)suit;
        if (index >= 0 && index < suitSprites.Length)
        {
            powerSuitIcon.sprite = suitSprites[index];
            powerSuitIcon.gameObject.SetActive(true);
        }
    }

    // The coroutine to flash the winner
    public IEnumerator AnimateWinnerGlow(int winnerIndex)
    {
        if (winnerGlows[winnerIndex] != null)
        {
            winnerGlows[winnerIndex].SetActive(true);
            yield return new WaitForSeconds(2.0f); // Glow duration
            winnerGlows[winnerIndex].SetActive(false);
        }
    }
}