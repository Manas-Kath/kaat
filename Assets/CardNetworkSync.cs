using UnityEngine;
using Unity.Netcode;
using uVegas.Core.Cards; 
using uVegas.UI;
using DG.Tweening;
using System.Collections;

public class CardNetworkSync : NetworkBehaviour
{
    public NetworkVariable<int> netOwnerIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> netSuit = new NetworkVariable<int>(0);
    public NetworkVariable<int> netRank = new NetworkVariable<int>(0);

    private UICard uiCard;

    private void Awake()
    {
        uiCard = GetComponent<UICard>();
    }

    public override void OnNetworkSpawn()
    {
        netSuit.OnValueChanged += OnCardDataChanged;
        netRank.OnValueChanged += OnCardDataChanged;
        netOwnerIndex.OnValueChanged += OnOwnerChanged;

        StartCoroutine(InitializeCardRoutine());
    }

    private IEnumerator InitializeCardRoutine()
    {
        GameController controller = null;
        while (controller == null)
        {
            controller = FindObjectOfType<GameController>();
            yield return null;
        }

        // Apply Ownership
        if(netOwnerIndex.Value >= 0) UpdateParent(netOwnerIndex.Value);
        
        yield return null; 

        // Apply Data if ready
        if(netSuit.Value != 0 && netRank.Value != 0) 
        {
            UpdateSprite(netSuit.Value, netRank.Value);
        }
    }

    private void OnOwnerChanged(int oldVal, int newVal) => UpdateParent(newVal);
    
    private void OnCardDataChanged(int oldVal, int newVal) 
    {
         if (netRank.Value != 0 && netSuit.Value != 0) UpdateSprite(netSuit.Value, netRank.Value);
    }

    private void UpdateParent(int ownerIdx)
    {
        var controller = FindObjectOfType<GameController>();
        if (controller != null && ownerIdx >= 0 && ownerIdx < controller.players.Length)
        {
            var targetHand = controller.players[ownerIdx];
            
            // CLEAN RESET
            transform.SetParent(targetHand.cardContainer, false);
            transform.DOKill(); 
            
            // UI needs AnchoredPosition reset, not just localPosition
            var rect = GetComponent<RectTransform>();
            if(rect != null) rect.anchoredPosition = Vector2.zero;
            else transform.localPosition = Vector3.zero;

            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // Ensure we are in the visualizer list
            targetHand.AddCardToHand(uiCard, targetHand.transform.position);
        }
    }

    private void UpdateSprite(int suitInt, int rankInt)
    {
        Suit s = (Suit)suitInt;
        Rank r = (Rank)rankInt;

        var controller = FindObjectOfType<GameController>();
        if (controller != null && uiCard != null)
        {
            uiCard.Init(new Card(s, r), controller.players[0].cardTheme);
            uiCard.name = $"{r} of {s} [Net]";
            
            var targetHand = controller.players[netOwnerIndex.Value];

            // Logic: Is this card Mine or Theirs?
            if (!targetHand.isHuman)
            {
                uiCard.SetFaceDown(true);
            }
            else
            {
                uiCard.SetFaceDown(false);
            }

            // CRITICAL FIX: Now that we know what the card is, SORT the hand again.
            // This forces the "White Card" to update and move to its sorted position.
            targetHand.SortHand();
        }
    }
    
    // Cleanup when destroyed
    public override void OnNetworkDespawn()
    {
        var controller = FindObjectOfType<GameController>();
        if (controller != null && uiCard != null)
        {
            foreach (var p in controller.players) p.RemoveCard(uiCard);
        }
        base.OnNetworkDespawn();
    }
}