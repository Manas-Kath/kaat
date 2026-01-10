using System.Collections.Generic;
using UnityEngine;
using uVegas.Core.Cards;
using uVegas.UI;
using System.Linq; 
using DG.Tweening;
using Unity.Netcode; 

public class HandVisualizer : MonoBehaviour
{
    [Header("Identity")]
    public int playerIndex; 
    public bool isHuman;    

    [Header("References")]
    public Transform playSlot; 
    public UICard cardPrefab; 
    public CardTheme cardTheme;
    public Transform cardContainer;

    [Header("Layout Settings")]
    public float fanSpreadAngle = 15f;  
    public float cardSpacing = 35f; 
    public float verticalArch = 10f;     

    public List<UICard> currentHandObjects = new List<UICard>();

    public void AddCardToHand(UICard cardInstance, Vector3 dealerWorldPos)
    {
        SanitizeList();

        // Safety: Ensure we aren't adding duplicates
        if (currentHandObjects.Contains(cardInstance)) return;

        cardInstance.transform.position = dealerWorldPos;
        
        // Fix: Use AnchoredPosition for UI to prevent weird offsets
        cardInstance.transform.SetParent(cardContainer, false);
        var rect = cardInstance.GetComponent<RectTransform>();
        if(rect != null) rect.anchoredPosition = Vector2.zero;

        cardInstance.transform.localRotation = Quaternion.identity; 
        cardInstance.transform.localScale = Vector3.one;

        if (!isHuman) cardInstance.SetFaceDown(true);

        var interaction = cardInstance.GetComponent<CardInteraction>();
        if (interaction != null) interaction.ownerIndex = playerIndex;

        currentHandObjects.Add(cardInstance);
        
        // Wait one frame to sort (helps with network batching)
        // We call update immediately, but the tweens will handle smoothing
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

    public void SortHand()
    {
        SanitizeList();
        currentHandObjects = currentHandObjects.OrderBy(c => c.Data.suit).ThenByDescending(c => c.Data.rank).ToList();
        
        for (int i = 0; i < currentHandObjects.Count; i++) 
        {
            if(currentHandObjects[i] != null) 
                currentHandObjects[i].transform.SetSiblingIndex(i);
        }
        UpdateHandVisuals();
    }

    private void SanitizeList()
    {
        for (int i = currentHandObjects.Count - 1; i >= 0; i--)
        {
            // Strict check: If the object is destroyed or the Transform is missing
            if (currentHandObjects[i] == null || currentHandObjects[i].transform == null)
            {
                currentHandObjects.RemoveAt(i);
            }
        }
    }

    public void UpdateHandVisuals()
    {
        SanitizeList();
        int cardCount = currentHandObjects.Count;
        if (cardCount == 0) return;

        float centerIndex = (cardCount - 1) / 2f;
        bool isSidePlayer = (playerIndex == 1 || playerIndex == 3);

        for (int i = 0; i < cardCount; i++)
        {
            var card = currentHandObjects[i];
            
            // 1. SKIP INVALID CARDS
            if (card == null || card.transform == null) continue;

            // 2. STOP OLD TWEENS
            card.transform.DOKill();

            float indexDistanceFromCenter = i - centerIndex;
            float angleStep = (cardCount > 1) ? fanSpreadAngle / (cardCount - 1) : 0;
            
            Vector3 targetPos;
            Vector3 targetRot;

            if (isSidePlayer)
            {
                float sideRotation = (playerIndex == 1) ? 90f : -90f;
                float archOffset = verticalArch * (1 - Mathf.Pow(indexDistanceFromCenter / (cardCount + 1), 2));
                float spacingOffset = indexDistanceFromCenter * cardSpacing;
                if (playerIndex == 1) archOffset = -archOffset;

                targetPos = new Vector3(archOffset, spacingOffset, 0);
                targetRot = new Vector3(0, 0, sideRotation - (indexDistanceFromCenter * angleStep));
            }
            else
            {
                float xPos = indexDistanceFromCenter * cardSpacing;
                float yPos = verticalArch * (1 - Mathf.Pow(indexDistanceFromCenter / (cardCount + 1), 2));
                if (playerIndex == 2) yPos = -yPos;

                float zRotation = -indexDistanceFromCenter * angleStep;
                if (playerIndex == 2) zRotation = -zRotation;

                targetPos = new Vector3(xPos, yPos, 0);
                targetRot = new Vector3(0, 0, zRotation);
            }

            // --- CRASH PREVENTION ---
            // Wrap the animation in Try-Catch. If DOTween fails on one card, 
            // it won't stop the loop for the others.
            try
            {
                card.transform.DOLocalMove(targetPos, 0.4f).SetEase(Ease.OutCubic);
                card.transform.DOLocalRotate(targetRot, 0.4f).SetEase(Ease.OutCubic);
                card.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            }
            catch
            {
                // Just reset position if tween fails
                if(card != null && card.transform != null)
                {
                    card.transform.localPosition = targetPos;
                    card.transform.localRotation = Quaternion.Euler(targetRot);
                }
            }
        }
    }
    
    // Logic Helpers
    public bool HasSuit(Suit s) => currentHandObjects.Any(c => c != null && c.Data.suit == s);
    public bool HasHigherCardInSuit(Suit s, Rank r) => currentHandObjects.Any(c => c != null && c.Data.suit == s && c.Data.rank > r);
    public void ClearHandVisuals() { SanitizeList(); foreach (var c in currentHandObjects) if(c!=null) Destroy(c.gameObject); currentHandObjects.Clear(); }
}