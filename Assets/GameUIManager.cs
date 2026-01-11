using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using uVegas.Core.Cards; 

public class GameUIManager : MonoBehaviour
{
    [Header("Bidding & Info")]
    public TextMeshProUGUI[] visualBidTexts; 
    public Image powerSuitIcon;              
    public Sprite[] suitSprites;             
    public GameObject[] winnerGlows;         

    // --- HELPER: LOGICAL TO VISUAL MAPPING ---
private int GetVisualIndex(int logicalIndex)
    {
        // Use FindObjectOfType since we have no singleton anymore
        var gc = FindObjectOfType<GameController>();
        if (gc == null) return logicalIndex;
        
        int localIndex = gc.localPlayerIndex; // <--- CHANGED THIS
        return (logicalIndex - localIndex + 4) % 4;
    }

    public void SetBidText(int logicalPlayerIndex, string text)
    {
        if (visualBidTexts == null) return;
        int vIndex = GetVisualIndex(logicalPlayerIndex);
        
        if (vIndex >= 0 && vIndex < visualBidTexts.Length && visualBidTexts[vIndex] != null)
        {
            visualBidTexts[vIndex].text = text;
        }
    }

    public void ClearAllBids()
    {
        if (visualBidTexts == null) return;
        foreach (var t in visualBidTexts) if (t != null) t.text = "";
    }

    public void SetPowerSuitDisplay(Suit suit)
    {
        if (powerSuitIcon == null || suitSprites == null) return;
        int index = (int)suit;
        if (index >= 0 && index < suitSprites.Length)
        {
            powerSuitIcon.sprite = suitSprites[index];
            powerSuitIcon.gameObject.SetActive(true);
        }
        else
        {
            powerSuitIcon.gameObject.SetActive(false);
        }
    }
}