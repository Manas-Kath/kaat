using System.Collections.Generic;
using UnityEngine;
using uVegas.Core.Cards;
using uVegas.UI;
using System.Linq; 
using DG.Tweening;

public class HandVisualizer : MonoBehaviour
{
    [Header("Identity")]
    public int playerIndex; 
    public bool isHuman;    
    [HideInInspector] public int layoutId; 

    [Header("References")]
    public Transform cardContainer; 
    public Transform playSlot;      
    public UICard cardPrefab;       
    public CardTheme cardTheme;     

    [Header("Layout Settings")]
    public float fanSpreadAngle = 20f;  
    public float cardSpacing = 40f; 
    public float verticalArch = 15f;     
    public float popUpAmount = 20f; 

    [Header("Platform Adjustments")]
    [Tooltip("Widens the gap between cards for the player (easier to tap).")]
    public float humanSpacingMultiplier = 1.6f; 
    [Tooltip("Squishes opponents' cards to save screen space.")]
    public float opponentSpacingMultiplier = 0.6f;

    public List<UICard> currentHandObjects = new List<UICard>();
    private List<Card> highlightedCardsData = new List<Card>();

    public void HighlightValidCards(List<Card> validCards) {
        highlightedCardsData = validCards ?? new List<Card>();
        UpdateHandVisuals();
    }
    public void ClearHighlights() {
        highlightedCardsData.Clear();
        UpdateHandVisuals();
    }

    public void UpdateHandVisuals()
    {
        int cardCount = currentHandObjects.Count;
        if (cardCount == 0) return;

        float centerIndex = (cardCount - 1) / 2f;
        
        bool isRight = (layoutId == 1);
        bool isTop   = (layoutId == 2);
        bool isLeft  = (layoutId == 3);
        bool isSide  = (isRight || isLeft);

        // --- NEW LOGIC START ---
        // 1. Determine Spacing: Wide for human, tight for opponents
        float currentSpacing = cardSpacing * (isHuman ? humanSpacingMultiplier : opponentSpacingMultiplier);

        // 2. Determine Arch & Fan: 0 for human (straight line), normal for opponents
        float currentArch = isHuman ? 0f : verticalArch;
        float currentFanAngle = isHuman ? 0f : fanSpreadAngle;
        // --- NEW LOGIC END ---

        for (int i = 0; i < cardCount; i++)
        {
            UICard card = currentHandObjects[i];
            if (card == null) continue;

            card.transform.SetSiblingIndex(i);
            card.transform.DOKill();

            float indexDistanceFromCenter = i - centerIndex;
            
            // Use currentFanAngle here
            float actualFanAngle = (cardCount > 1) ? currentFanAngle : 0;
            float angleStep = (cardCount > 1) ? actualFanAngle / (cardCount - 1) : 0;

            Vector3 targetPos = Vector3.zero;
            Vector3 targetRot = Vector3.zero;

            // BASE LAYOUT
            if (isSide)
            {
                float sideRotBase = isRight ? 90f : -90f;
                float normalizedPos = indexDistanceFromCenter / (cardCount > 1 ? cardCount : 1f);
                
                // Use currentArch here
                float archOffset = currentArch * (1f - (normalizedPos * normalizedPos));
                if (isRight) archOffset = -archOffset; 
                
                // Use currentSpacing here
                float spacingOffset = -indexDistanceFromCenter * currentSpacing; 
                
                targetPos = new Vector3(archOffset, spacingOffset, 0);
                targetRot = new Vector3(0, 0, sideRotBase - (indexDistanceFromCenter * (angleStep * 0.5f))); 
            }
            else
            {
                // Use currentSpacing here
                float xPos = indexDistanceFromCenter * currentSpacing;
                
                float normalizedPos = indexDistanceFromCenter / (cardCount > 1 ? cardCount : 1f);
                
                // Use currentArch here
                float yPos = currentArch * (1f - Mathf.Abs(normalizedPos)); 
                if (isTop) yPos = -yPos; 
                
                float zRot = -indexDistanceFromCenter * angleStep;
                if (isTop) zRot = -zRot; 
                targetPos = new Vector3(xPos, yPos, 0);
                targetRot = new Vector3(0, 0, zRot);
            }

            // VALUE CHECK
            bool isValid = false;
            if (isHuman && highlightedCardsData.Count > 0 && card.Data != null)
            {
                foreach(var validCard in highlightedCardsData) {
                    if (validCard.suit == card.Data.suit && validCard.rank == card.Data.rank) {
                        isValid = true; break;
                    }
                }
            }

            // DIRECTIONAL POP
            if (isValid)
            {
                if (isRight) targetPos.x -= popUpAmount;
                else if (isLeft) targetPos.x += popUpAmount;
                else if (isTop) targetPos.y -= popUpAmount;
                else targetPos.y += popUpAmount;
            }

            card.transform.DOLocalMove(targetPos, 0.35f).SetEase(Ease.OutQuad);
            card.transform.DOLocalRotate(targetRot, 0.35f).SetEase(Ease.OutQuad);
            card.transform.DOScale(Vector3.one, 0.35f);
        }
    }
    
    public void AddCardToHand(UICard cardInstance, Vector3 spawnWorldPos) {
        if (cardInstance == null) return;
        if (currentHandObjects.Contains(cardInstance)) return;
        currentHandObjects.Add(cardInstance);
        if (cardInstance.transform.parent != cardContainer) cardInstance.transform.SetParent(cardContainer, false);
        if (cardInstance.Data != null) SortHand();
        else UpdateHandVisuals();
    }
    public void RemoveCard(UICard cardToRemove) {
        if (currentHandObjects.Contains(cardToRemove)) { currentHandObjects.Remove(cardToRemove); UpdateHandVisuals(); }
    }
    public void SortHand() {
        currentHandObjects.RemoveAll(c => c == null);
        currentHandObjects = currentHandObjects.OrderBy(c => c.Data != null ? c.Data.suit : 0).ThenByDescending(c => c.Data != null ? c.Data.rank : 0).ToList();
        UpdateHandVisuals();
    }
    public void ClearHandVisuals() {
        foreach(var c in currentHandObjects) if(c != null) Destroy(c.gameObject);
        currentHandObjects.Clear(); highlightedCardsData.Clear();
    }
    public bool HasSuit(Suit s) => currentHandObjects.Any(c => c.Data != null && c.Data.suit == s);
}