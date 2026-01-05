using System.Collections;
using System.Collections.Generic;
using System.Linq; // Needed for Sorting
using UnityEngine;
using uVegas.Core.Cards;
using uVegas.UI;

public class GameController : MonoBehaviour
{
    public int[] tricksWonInRound = new int[4];

    public GameUIManager uiManager; // Drag the object with GameUIManager here

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
    private int highestBidderIndex = -1; // The "Contractor"
    private bool humanInputReceived = false; // Shared flag for UI waiting

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
        // 1. NUCLEAR STOP: Kill all running timers, animations, and game loops immediately.
        StopAllCoroutines();

        // 2. CLEANUP: Now safe to delete everything
        ForceCleanupTable();

        currentPhase = GamePhase.Setup;
        // --- STEP 1: CLEANUP FIRST ---
        ForceCleanupTable();
        // -----------------------------

        currentPhase = GamePhase.Setup;
        deckManager.InitializeDeck();

        // DEALER LOGIC
        if (gameRoundNumber > 1)
        {
            dealerIndex = GetPlayerWithLowestScore();
            Debug.Log($"Round {gameRoundNumber}: Dealer is Player {dealerIndex} (Lowest Score: {playerScores[dealerIndex]})");
        }
        else
        {
            dealerIndex = Random.Range(0, 4);
            Debug.Log($"Round 1: Dealer is Player {dealerIndex} (Random Start)");
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
            yield return new WaitForSeconds(1.0f);
            StartCoroutine(DealRoundTwo());
            yield break;
        }

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
                // Simple Bot Logic: Pass
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
            // If Bot won, they would pick suit here. Defaults to Spades if nobody won.
            if (highestBidderIndex == -1) currentPowerSuit = Suit.Spades;
            StartCoroutine(DealRoundTwo());
        }
    }

    public void OnHumanSuitSelected(Suit s)
    {
        currentPowerSuit = s;
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

    public IEnumerator BiddingPhase()
    {
        uiManager.ClearAllBids();

        // LOGIC: Start from person next to dealer
        int startPlayer = (dealerIndex + 1) % 4;

        for (int i = 0; i < 4; i++)
        {
            int currentPlayer = (startPlayer + i) % 4; // Loop wrapping around 4

            int bid = 0;
            if (players[currentPlayer].isHuman)
            {
                // ... wait for input ...
            }
            else
            {
                // DELEGATE BRAIN WORK TO BOTAI
                bid = BotAI.CalculateBid(players[currentPlayer].hand);
            }

            // DELEGATE VISUALS TO UIMANAGER
            string bidStr = (bid == 0) ? "Pass" : bid.ToString();
            uiManager.SetBidText(currentPlayer, bidStr);

            yield return new WaitForSeconds(1f);
        }
    }

    // --- PHASE B: FINAL BIDDING ---
    IEnumerator RunFinalBidPhase()
    {
        currentPhase = GamePhase.FinalBidding;
        Debug.Log("--- FINAL PREDICTION PHASE ---");

        // Loop through all 4 players to get their written bid
        for (int i = 0; i < 4; i++)
        {
            int minBid = 2;
            // Rule: If you are the Contractor (Changed Trump), Min Bid = Your Auction Bid
            if (i == highestBidderIndex && currentHighestBid > 2) minBid = currentHighestBid;

            if (players[i].isHuman)
            {
                humanInputReceived = false;
                auctionUI.ShowFinalBidSelector(minBid); // Show new UI
                yield return new WaitUntil(() => humanInputReceived);
                Debug.Log($"Human set Final Bid to: {finalBids[i]}");
            }
            else
            {
                // Simple Bot Logic
                finalBids[i] = minBid + Random.Range(0, 3);
                Debug.Log($"Bot {i} writes bid: {finalBids[i]}");
                yield return new WaitForSeconds(0.5f);
            }
        }

        // VALIDATION: Sum >= 11
        int totalBids = 0;
        foreach (var b in finalBids) totalBids += b;

        if (totalBids < 11)
        {
            Debug.LogError($"MISDEAL! Total bids {totalBids} < 11. Redealing...");
            // TODO: Add visual feedback for Redeal
            yield return new WaitForSeconds(2.0f);
            StartGame();
            yield break;
        }

        Debug.Log("Bids Valid. Starting Play.");
        tricksWonInRound = new int[4]; // Reset counters to 0
        totalTricksPlayed = 0;         // Reset trick counter

        // --- FIX STARTS HERE ---

        // 1. Clean up the UI so it doesn't block mouse clicks
        auctionUI.HideAll();

        // 2. CRITICAL: Update the phase so OnCardClicked allows input!
        currentPhase = GamePhase.PlayPhase;

        // --- FIX ENDS HERE ---

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

        List<UICard> legal = bot.GetLegalCards(currentLeadSuit);
        UICard cardToPlay = null;

        // SCENARIO 1: Bot is Leading (First to play)
        if (currentWinningCard == null)
        {
            // Simple Strategy: Play the highest card to try and win, or random
            // Let's stick to Random for now (or change to legal.Last() for highest)
            cardToPlay = legal[Random.Range(0, legal.Count)];
        }
        // SCENARIO 2: Bot is Following (Must try to win)
        else
        {
            // Filter the legal cards to find ones that actually BEAT the current winner
            var winningCards = legal.Where(c => WillCardWinTrick(c)).OrderBy(c => c.Data.rank).ToList();

            if (winningCards.Count > 0)
            {
                // RULE: If we have cards that win, play the LOWEST one that wins.
                // (Don't waste a King if a 10 is enough to beat the table's 9)
                cardToPlay = winningCards[0];
            }
            else
            {
                // If we CANNOT win, play our LOWEST rank card to save high ones for later.
                cardToPlay = legal.OrderBy(c => c.Data.rank).First();
            }
        }

        PlayCard(cardToPlay, currentPlayerIndex);
    }

    // Helper function to check if a specific card beats the current table winner
    bool WillCardWinTrick(UICard candidate)
    {
        if (currentWinningCard == null) return true;

        bool isTrump = candidate.Data.suit == currentPowerSuit;
        bool winnerIsTrump = currentWinningCard.Data.suit == currentPowerSuit;

        // If I play Trump and current winner is NOT Trump -> I win
        if (isTrump && !winnerIsTrump) return true;

        // If both are Trump -> I must have higher rank
        if (isTrump && winnerIsTrump && candidate.Data.rank > currentWinningCard.Data.rank) return true;

        // If neither is Trump (and I followed suit) -> I must have higher rank
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
        StartCoroutine(uiManager.AnimateWinnerGlow(winnerIndex));
        
        yield return new WaitForSeconds(1.5f);
        Debug.Log($"Trick Winner: Player {currentTrickWinnerIndex}");

        // 1. AWARD THE TRICK
        tricksWonInRound[currentTrickWinnerIndex]++;
        totalTricksPlayed++;

        // 2. CHECK IF ROUND IS OVER (Standard game is 13 tricks)
        if (totalTricksPlayed >= 13)
        {
            Debug.Log("Round Over! Calculating Scores...");
            CalculateRoundScores();
        }
        else
        {
            // If round is NOT over, keep playing
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

        Debug.Log($"--- END OF ROUND {gameRoundNumber} ---");

        for (int i = 0; i < 4; i++)
        {
            int bid = finalBids[i];
            int tricks = tricksWonInRound[i];
            int roundScore = 0;

            // SCORING LOGIC:
            // 1. If Tricks >= Bid --> Score = +Bid
            // 2. If Tricks < Bid  --> Score = -Bid
            // (Note: If you want extra points for overtricks later, change this line)

            if (tricks >= bid)
                roundScore = bid;
            else
                roundScore = -bid;

            playerScores[i] += roundScore;
            Debug.Log($"Player {i}: Bid {bid}, Won {tricks} -> Round Pts: {roundScore} | Total: {playerScores[i]}");
        }

        // Wait 3 seconds to see logs, then start next round
        StartCoroutine(PrepareNextRound());
    }
    IEnumerator PrepareNextRound()
    {
        yield return new WaitForSeconds(3.0f);

        StopAllCoroutines();

        // INCREMENT ROUND
        gameRoundNumber++;

        // RESET GAME STATE VARIABLES
        currentWinningCard = null;
        currentTrickWinnerIndex = -1;
        currentLeadSuit = null;
        finalBids = new int[4];
        tricksWonInRound = new int[4];
        totalTricksPlayed = 0;

        // StartGame will handle the Cleanup & Dealer selection now
        StartGame();
    }
    // Add this inside GameController.cs
    void ForceCleanupTable()
    {
        // 1. IDENTIFY TEMPLATES (The "Do Not Destroy" List)
        // We check what cards the HandVisualizers are using as Prefabs so we don't kill them.
        HashSet<UICard> protectedCards = new HashSet<UICard>();
        foreach (var p in players)
        {
            if (p.cardPrefab != null) protectedCards.Add(p.cardPrefab);
        }

        // 2. Destroy cards in everyone's HANDS
        foreach (var p in players)
        {
            p.ClearHandVisuals();
        }

        // 3. Destroy cards currently played on the TABLE
        foreach (var p in players)
        {
            foreach (Transform child in p.playSlot)
            {
                Destroy(child.gameObject);
            }
        }

        // 4. Destroy Strays (BUT RESPECT THE TEMPLATES)
        var strays = FindObjectsByType<UICard>(FindObjectsSortMode.None);
        foreach (var card in strays)
        {
            // CRITICAL CHECK: Is this card actually our Prefab?
            if (protectedCards.Contains(card)) continue;

            if (card != null && card.gameObject != null)
            {
                Destroy(card.gameObject);
            }
        }

        Debug.Log("--- TABLE CLEARED ---");
    }




}