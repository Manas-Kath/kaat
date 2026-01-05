using System.Collections.Generic;
using UnityEngine;
using uVegas.Core.Cards;
using uVegas.UI;
using System.Linq; 

public class HandVisualizer : MonoBehaviour
{
    [Header("Identity")]
    public int playerIndex; // 0=Bottom, 1=Right, 2=Top, 3=Left
    public bool isHuman;    

    [Header("References")]
    public Transform playSlot; 
    public UICard cardPrefab;
    public CardTheme cardTheme;
    public Transform cardContainer;

    [Header("Layout Settings")]
    // CHANGED: Defaults set to 0 to prevent fanning/arching
    public float fanSpreadAngle = 0f;  
    public float cardSpacing = 50f;     // Increased slightly for flat layout
    public float verticalArch = 0f;     

    public List<UICard> currentHandObjects = new List<UICard>();

    public void AddCardToHand(Card cardData)
    {
        UICard newCard = Instantiate(cardPrefab, cardContainer);
        newCard.name = $"{cardData.rank} of {cardData.suit}";
        newCard.transform.localPosition = Vector3.zero;
        newCard.Init(cardData, cardTheme);

        var interaction = newCard.GetComponent<CardInteraction>();
        if (interaction != null) interaction.ownerIndex = playerIndex;

        currentHandObjects.Add(newCard);
        UpdateHandVisuals();
    }

    public void RemoveCard(UICard cardToRemove)
    {
        if (currentHandObjects.Contains(cardToRemove))
        {
            currentHandObjects.Remove(cardToRemove);
            UpdateHandVisuals();
        }
    }

    public void ClearPlayedCard()
    {
        foreach (Transform child in playSlot) Destroy(child.gameObject);
    }

    public void SortHand()
    {
        currentHandObjects = currentHandObjects
            .OrderBy(c => c.Data.suit)
            .ThenByDescending(c => c.Data.rank) // Sort High to Low usually looks better
            .ToList();

        for (int i = 0; i < currentHandObjects.Count; i++)
        {
            currentHandObjects[i].transform.SetSiblingIndex(i);
        }
        UpdateHandVisuals();
    }

    // --- LOGIC HELPERS ---
    public bool HasSuit(Suit suitToCheck)
    {
        return currentHandObjects.Any(c => c.Data.suit == suitToCheck);
    }

    public bool HasHigherCardInSuit(Suit suit, Rank targetRank)
    {
        return currentHandObjects.Any(c => c.Data.suit == suit && c.Data.rank > targetRank);
    }

    public List<UICard> GetLegalCards(Suit? leadSuit)
    {
        if (leadSuit == null) return currentHandObjects;
        if (HasSuit(leadSuit.Value))
        {
            return currentHandObjects.Where(c => c.Data.suit == leadSuit.Value).ToList();
        }
        return currentHandObjects;
    }

    private void UpdateHandVisuals()
    {
        int cardCount = currentHandObjects.Count;
        if (cardCount == 0) return;

        float centerIndex = (cardCount - 1) / 2f;

        for (int i = 0; i < cardCount; i++)
        {
            float indexDistanceFromCenter = i - centerIndex;
            
            // --- UPDATED LOGIC FOR FLAT LAYOUT ---
            // If fanSpreadAngle is 0, zRotation becomes 0.
            // If verticalArch is 0, yPos becomes 0.
            
            float angleStep = (cardCount > 1) ? fanSpreadAngle / (cardCount - 1) : 0;
            float zRotation = -indexDistanceFromCenter * angleStep;

            float xPos = indexDistanceFromCenter * cardSpacing;
            float yPos = verticalArch * (1 - Mathf.Pow(indexDistanceFromCenter / (cardCount + 1), 2));

            currentHandObjects[i].transform.localRotation = Quaternion.Euler(0, 0, zRotation);
            currentHandObjects[i].transform.localPosition = new Vector3(xPos, yPos, 0);
        }
    }
    // Add this to HandVisualizer.cs
    public void ClearHandVisuals()
    {
        // 1. Destroy all card GameObjects currently in the list
        foreach (var card in currentHandObjects)
        {
            if (card != null) Destroy(card.gameObject);
        }
        
        // 2. Clear the list references
        currentHandObjects.Clear();
    }
}