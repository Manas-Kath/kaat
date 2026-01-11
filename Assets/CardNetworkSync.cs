using UnityEngine;
using Unity.Netcode;
using uVegas.Core.Cards; 
using uVegas.UI;
using System.Collections;
using DG.Tweening; 

public class CardNetworkSync : NetworkBehaviour
{
    public NetworkVariable<int> netOwnerIndex = new NetworkVariable<int>(-1); 
    public NetworkVariable<int> netSuit = new NetworkVariable<int>(0);
    public NetworkVariable<int> netRank = new NetworkVariable<int>(0);

    private UICard uiCard;
    private GameController gameController;

    private void Awake() { uiCard = GetComponent<UICard>(); }

    public override void OnNetworkSpawn()
    {
        netSuit.OnValueChanged += OnCardDataChanged;
        netRank.OnValueChanged += OnCardDataChanged;
        netOwnerIndex.OnValueChanged += OnOwnerChanged;
        StartCoroutine(InitializeCardRoutine());
    }

    public override void OnNetworkDespawn()
    {
        transform.DOKill();
        if (gameController != null && netOwnerIndex.Value >= 0 && gameController.players != null)
        {
            if (netOwnerIndex.Value < gameController.players.Length)
                gameController.players[netOwnerIndex.Value].RemoveCard(uiCard);
        }
        base.OnNetworkDespawn();
    }

    private IEnumerator InitializeCardRoutine()
    {
        while (gameController == null) {
            gameController = FindObjectOfType<GameController>();
            yield return null;
        }

        yield return new WaitUntil(() => netRank.Value != 0);

        // Wait for Seat Assignment
        float timer = 5f;
        while ((gameController.players == null || 
                gameController.players.Length <= netOwnerIndex.Value || 
                gameController.players[netOwnerIndex.Value] == null) && timer > 0)
        {
            timer -= Time.deltaTime;
            yield return null;
        }

        // ANIMATION FIX:
        // Use NetworkVariables to check Phase and Dealer
        var phase = gameController.netCurrentPhase.Value;
        if (phase == GameController.GamePhase.Deal1 || phase == GameController.GamePhase.Deal2)
        {
            int dealerIdx = gameController.netDealerIndex.Value;
            var dealerHand = gameController.players[dealerIdx];
            if(dealerHand != null) transform.position = dealerHand.transform.position;
        }

        SyncParent(netOwnerIndex.Value);
        SyncVisuals(netSuit.Value, netRank.Value);
    }

    private void OnOwnerChanged(int oldVal, int newVal) { SyncParent(newVal); }
    private void OnCardDataChanged(int oldVal, int newVal) { SyncVisuals(netSuit.Value, netRank.Value); }

    private void SyncParent(int ownerIdx)
    {
        if (gameController == null || gameController.players == null) return;
        var targetHand = gameController.players[ownerIdx];
        
        transform.DOKill();
        // Snap parent, keep world position (dealer pos), then tween
        transform.SetParent(targetHand.cardContainer, true);
        
        targetHand.AddCardToHand(uiCard, targetHand.transform.position);
    }

    private void SyncVisuals(int suitInt, int rankInt)
    {
        if (gameController == null || uiCard == null) return;
        
        // Use localPlayerIndex which is set in OnNetworkSpawn
        bool isMine = (netOwnerIndex.Value == gameController.localPlayerIndex);
        uiCard.SetFaceDown(!isMine);

        var hand = gameController.players[netOwnerIndex.Value];
        CardTheme theme = (hand != null) ? hand.cardTheme : null;
        
        uiCard.Init(new Card((Suit)suitInt, (Rank)rankInt), theme);
        uiCard.name = $"Card_{(Suit)suitInt}_{(Rank)rankInt}";
        if (hand != null) hand.SortHand();
    }
}