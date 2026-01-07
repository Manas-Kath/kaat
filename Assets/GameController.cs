using System.Collections;
using System.Collections.Generic;
using System.Linq; 
using UnityEngine;
using uVegas.Core.Cards;
using uVegas.UI;

public class GameController : MonoBehaviour
{
    public int[] tricksWonInRound = new int[4];
    public GameUIManager uiManager; 

    private int totalTricksPlayed = 0;

    [Header("References")]
    public DeckManager deckManager;
    // ORDER: 0=Bottom (Human), 1=Right, 2=Top, 3=Left
    public HandVisualizer[] players;
    public AuctionUI auctionUI;

    public enum GamePhase { Setup, DealPhase1, PowerAuction, SuitSelection, DealPhase2, FinalBidding, PlayPhase, ScoreBoard }

    [Header("Game State")]
    public GamePhase currentPhase = GamePhase.Setup;
    public Suit currentPowerSuit = Suit.Spades;
    public int gameRoundNumber = 1;
    public int dealerIndex = 0;

    // Bidding & Scoring
    public int[] playerScores = new int[4];
    public int[] finalBids = new int[4];

    // --- AUCTION VARIABLES ---
    private int currentHighestBid = 0;
    private int highestBidderIndex = -1; 
    private bool humanInputReceived = false; 

    // --- PLAY VARIABLES ---
    private int currentPlayerIndex = 0;
    private int cardsPlayedInTrick = 0;
    private Suit? currentLeadSuit = null;
    private UICard currentWinningCard = null;
    private int currentTrickWinnerIndex = -1;

    // --- HELPER: COUNTER-CLOCKWISE ---
    private int GetNextPlayerIndexCCW(int current) => (current + 1) % 4;

    void Start() { StartSession(); }

    public void StartSession()
    {
        gameRoundNumber = 1;
        playerScores = new int[4];
        StartGame();
    }

    public void StartGame()
    {
        StopAllCoroutines();
        ForceCleanupTable();

        currentPhase = GamePhase.Setup;
        deckManager.InitializeDeck();

        // DEALER LOGIC
        if (gameRoundNumber > 1)
        {
            dealerIndex = GetPlayerWithLowestScore();
            Debug.Log($"Round {gameRoundNumber}: Dealer is Player {dealerIndex}");
        }
        else
        {
            dealerIndex = Random.Range(0, 4);
            Debug.Log($"Round 1: Dealer is Player {dealerIndex}");
        }

        currentPhase = GamePhase.DealPhase1;
        StartCoroutine(DealRoundOne());
    }

    IEnumerator DealRoundOne()
    {
        int startNode = GetNextPlayerIndexCCW(dealerIndex);
        for (int c = 0; c < 5; c++)
        {
            int p = startNode;
            for (int k = 0; k < 4; k++)
            {
                var card = deckManager.DrawCard();
                if (card != null) players[p].AddCardToHand(card);
                p = GetNextPlayerIndexCCW(p);
                yield return new WaitForSeconds(0.05f);
            }
        }
        StartCoroutine(RunAuctionSequence());
    }

    // --- PHASE A: POWER AUCTION ---
    IEnumerator RunAuctionSequence()
    {
        currentPhase = GamePhase.PowerAuction;
        currentHighestBid = 0;
        highestBidderIndex = -1;

        if (gameRoundNumber == 1)
        {
            Debug.Log("Game 1: Default Spades.");
            currentPowerSuit = Suit.Spades;
            uiManager.SetPowerSuitDisplay(currentPowerSuit); // Update UI
            yield return new WaitForSeconds(1.0f);
            StartCoroutine(DealRoundTwo());
            yield break;
        }

        // Declare the variable here so it is available inside the loop
        int activeBidder = GetNextPlayerIndexCCW(dealerIndex);

        for (int k = 0; k < 4; k++)
        {
            if (players[activeBidder].isHuman)
            {
                humanInputReceived = false;
                auctionUI.ShowHumanTurn(currentHighestBid, highestBidderIndex);
                yield return new WaitUntil(() => humanInputReceived);
            }
            else
            {
                // --- SMART BOT LOGIC FOR AUCTION ---
                // Convert UI Cards to Data Cards
                var botHandData = players[activeBidder].currentHandObjects.Select(c => c.Data).ToList();
                int botBid = BotAI.CalculateAuctionBid(botHandData);

                if (botBid >= 5 && botBid > currentHighestBid)
                {
                    Debug.Log($"Bot {activeBidder} bids {botBid} to CHANGE TRUMP!");
                    currentHighestBid = botBid;
                    highestBidderIndex = activeBidder; 
                }
                yield return new WaitForSeconds(0.5f);
            }
            activeBidder = GetNextPlayerIndexCCW(activeBidder);
        }

        EvaluateAuctionWinner();
    }

    public void OnHumanAuctionAction(int bidAmount)
    {
        if (bidAmount > 0) { currentHighestBid = bidAmount; highestBidderIndex = 0; }
        humanInputReceived = true;
    }

    void EvaluateAuctionWinner()
    {
        auctionUI.HideAll();
        if (highestBidderIndex != -1 && players[highestBidderIndex].isHuman)
        {
            currentPhase = GamePhase.SuitSelection;
            auctionUI.ShowSuitSelection();
        }
        else
        {
            // If Nobody won -> Default Spades
            if (highestBidderIndex == -1)
            {
                currentPowerSuit = Suit.Spades;
                uiManager.SetPowerSuitDisplay(currentPowerSuit); // Update UI
            }
            // If Bot won, we could implement bot suit selection here, 
            // but for now, let's default Spades or implement simpler logic later.
            // (The bot logic calculated bid based on non-spades, so ideally we pick that suit)
            
            StartCoroutine(DealRoundTwo());
        }
    }

    public void OnHumanSuitSelected(Suit s)
    {
        currentPowerSuit = s;
        uiManager.SetPowerSuitDisplay(currentPowerSuit); // Update UI
        auctionUI.HideAll();
        StartCoroutine(DealRoundTwo());
    }

    IEnumerator DealRoundTwo()
    {
        currentPhase = GamePhase.DealPhase2;
        int startNode = GetNextPlayerIndexCCW(dealerIndex);

        for (int i = 0; i < 8; i++)
        {
            int p = startNode;
            for (int k = 0; k < 4; k++)
            {
                var card = deckManager.DrawCard();
                if (card != null) players[p].AddCardToHand(card);
                p = GetNextPlayerIndexCCW(p);
                yield return new WaitForSeconds(0.02f);
            }
        }

        foreach (var p in players) p.SortHand();
        StartCoroutine(RunFinalBidPhase());
    }

    // --- PHASE B: FINAL BIDDING ---
    IEnumerator RunFinalBidPhase()
    {
        currentPhase = GamePhase.FinalBidding;
        Debug.Log("--- FINAL PREDICTION PHASE ---");
        
        uiManager.ClearAllBids();

        for (int i = 0; i < 4; i++)
        {
            int minBid = 2;
            if (i == highestBidderIndex && currentHighestBid > 2) minBid = currentHighestBid;

            if (players[i].isHuman)
            {
                humanInputReceived = false;
                auctionUI.ShowFinalBidSelector(minBid); 
                yield return new WaitUntil(() => humanInputReceived);
                Debug.Log($"Human set Final Bid to: {finalBids[i]}");
            }
            else
            {
                // --- SMART BOT LOGIC FOR FINAL BID ---
                var botHandData = players[i].currentHandObjects.Select(c => c.Data).ToList();
                finalBids[i] = BotAI.CalculateFinalBid(botHandData, currentPowerSuit);
                
                // Enforce Contractor Min Bid Rule
                if (i == highestBidderIndex && finalBids[i] < minBid) finalBids[i] = minBid;

                Debug.Log($"Bot {i} calculates strength: {finalBids[i]}");
                yield return new WaitForSeconds(0.5f);
            }

            uiManager.SetBidText(i, finalBids[i].ToString()); // Update UI
        }

        int totalBids = 0;
        foreach (var b in finalBids) totalBids += b;

        if (totalBids < 11)
        {
            Debug.LogError($"MISDEAL! Total bids {totalBids} < 11. Redealing...");
            yield return new WaitForSeconds(2.0f);
            StartGame();
            yield break;
        }

        Debug.Log("Bids Valid. Starting Play.");
        tricksWonInRound = new int[4]; 
        totalTricksPlayed = 0;       

        auctionUI.HideAll();
        currentPhase = GamePhase.PlayPhase;

        int leader = GetNextPlayerIndexCCW(dealerIndex);
        StartNewTrick(leader);
    }

    public void OnHumanFinalBidSubmitted(int amount)
    {
        finalBids[0] = amount;
        humanInputReceived = true;
    }

    // --- PLAY PHASE ---
    void StartNewTrick(int leader)
    {
        cardsPlayedInTrick = 0;
        currentLeadSuit = null;
        currentWinningCard = null;
        currentTrickWinnerIndex = -1;
        currentPlayerIndex = leader;
        foreach (var p in players) p.ClearPlayedCard();
        StartTurn();
    }

    void StartTurn()
    {
        HandVisualizer active = players[currentPlayerIndex];
        if (!active.isHuman) StartCoroutine(BotTurnRoutine(active));
    }

    public void OnCardClicked(CardInteraction c)
    {
        if (currentPhase != GamePhase.PlayPhase) return;
        if (currentPlayerIndex != 0 || c.ownerIndex != 0) return;

        UICard clickedCard = c.GetComponent<UICard>();
        if (!IsValidPlay(clickedCard, players[0])) return;

        PlayCard(clickedCard, 0);
    }

    bool IsValidPlay(UICard cardToPlay, HandVisualizer playerHand)
    {
        if (currentLeadSuit == null) return true;
        Suit lead = currentLeadSuit.Value;
        bool hasLeadSuit = playerHand.HasSuit(lead);

        if (hasLeadSuit)
        {
            if (cardToPlay.Data.suit != lead) return false;
            if (currentWinningCard != null && currentWinningCard.Data.suit == lead)
            {
                bool clickedCardWins = cardToPlay.Data.rank > currentWinningCard.Data.rank;
                bool holdsWinningCard = playerHand.HasHigherCardInSuit(lead, currentWinningCard.Data.rank);
                if (holdsWinningCard && !clickedCardWins) return false;
            }
        }
        return true;
    }

    IEnumerator BotTurnRoutine(HandVisualizer bot)
    {
        yield return new WaitForSeconds(0.8f);

        // 1. Gather Data for Bot
        var handData = bot.currentHandObjects.Select(c => c.Data).ToList();
        
        // Find cards currently on the table
        List<Card> tableCards = new List<Card>();
        foreach(var p in players)
        {
           if(p.playSlot.childCount > 0) 
           {
               var uiCard = p.playSlot.GetComponentInChildren<UICard>();
               if (uiCard != null) tableCards.Add(uiCard.Data);
           }
        }

        // 2. Ask BotAI for the best move
        Card bestCardData = BotAI.ChooseCardToPlay(handData, tableCards, currentLeadSuit, currentPowerSuit);

        // 3. Find the UI object matching that data
        UICard cardToPlay = bot.currentHandObjects.First(c => c.Data == bestCardData);

        PlayCard(cardToPlay, currentPlayerIndex);
    }

    bool WillCardWinTrick(UICard candidate)
    {
        if (currentWinningCard == null) return true;

        bool isTrump = candidate.Data.suit == currentPowerSuit;
        bool winnerIsTrump = currentWinningCard.Data.suit == currentPowerSuit;

        if (isTrump && !winnerIsTrump) return true;
        if (isTrump && winnerIsTrump && candidate.Data.rank > currentWinningCard.Data.rank) return true;
        if (!isTrump && !winnerIsTrump &&
            candidate.Data.suit == currentLeadSuit &&
            candidate.Data.rank > currentWinningCard.Data.rank) return true;

        return false;
    }

    void PlayCard(UICard c, int pIdx)
    {
        if (cardsPlayedInTrick == 0) currentLeadSuit = c.Data.suit;

        c.transform.SetParent(players[pIdx].playSlot);
        c.transform.localPosition = Vector3.zero;
        c.transform.localRotation = Quaternion.identity;
        players[pIdx].RemoveCard(c);

        bool isNewWinner = false;
        if (currentWinningCard == null) isNewWinner = true;
        else
        {
            bool isTrump = c.Data.suit == currentPowerSuit;
            bool currentIsTrump = currentWinningCard.Data.suit == currentPowerSuit;
            if (isTrump && !currentIsTrump) isNewWinner = true;
            else if (isTrump && currentIsTrump && c.Data.rank > currentWinningCard.Data.rank) isNewWinner = true;
            else if (!isTrump && !currentIsTrump && c.Data.suit == currentLeadSuit && c.Data.rank > currentWinningCard.Data.rank) isNewWinner = true;
        }

        if (isNewWinner) { currentWinningCard = c; currentTrickWinnerIndex = pIdx; }

        cardsPlayedInTrick++;
        if (cardsPlayedInTrick >= 4) StartCoroutine(EndTrickRoutine());
        else AdvanceTurnCCW();
    }

    IEnumerator EndTrickRoutine()
    {
        StartCoroutine(uiManager.AnimateWinnerGlow(currentTrickWinnerIndex));
        
        yield return new WaitForSeconds(1.5f);
        
        tricksWonInRound[currentTrickWinnerIndex]++;
        totalTricksPlayed++;

        if (totalTricksPlayed >= 13)
        {
            CalculateRoundScores();
        }
        else
        {
            StartNewTrick(currentTrickWinnerIndex);
        }
    }

    void AdvanceTurnCCW() { currentPlayerIndex = GetNextPlayerIndexCCW(currentPlayerIndex); StartTurn(); }

    private int GetPlayerWithLowestScore()
    {
        int min = 1000, idx = 0;
        for (int i = 0; i < 4; i++) if (playerScores[i] < min) { min = playerScores[i]; idx = i; }
        return idx;
    }

    void CalculateRoundScores()
    {
        currentPhase = GamePhase.ScoreBoard;

        for (int i = 0; i < 4; i++)
        {
            int bid = finalBids[i];
            int tricks = tricksWonInRound[i];
            int roundScore = 0;

            if (tricks >= bid) roundScore = bid;
            else roundScore = -bid;

            playerScores[i] += roundScore;
        }
        StartCoroutine(PrepareNextRound());
    }

    IEnumerator PrepareNextRound()
    {
        yield return new WaitForSeconds(3.0f);

        StopAllCoroutines();

        gameRoundNumber++;
        currentWinningCard = null;
        currentTrickWinnerIndex = -1;
        currentLeadSuit = null;
        finalBids = new int[4];
        tricksWonInRound = new int[4];
        totalTricksPlayed = 0;

        StartGame();
    }

    void ForceCleanupTable()
    {
        HashSet<UICard> protectedCards = new HashSet<UICard>();
        foreach (var p in players)
        {
            if (p.cardPrefab != null) protectedCards.Add(p.cardPrefab);
        }

        foreach (var p in players) p.ClearHandVisuals();

        foreach (var p in players)
        {
            foreach (Transform child in p.playSlot) Destroy(child.gameObject);
        }

        var strays = FindObjectsByType<UICard>(FindObjectsSortMode.None);
        foreach (var card in strays)
        {
            if (protectedCards.Contains(card)) continue;
            if (card != null && card.gameObject != null) Destroy(card.gameObject);
        }
    }
}