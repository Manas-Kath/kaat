using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using uVegas.Core.Cards;
using uVegas.UI;
using DG.Tweening;

public class GameController : NetworkBehaviour
{
    [Header("Configuration")]
    public int totalRounds = 5;

    [Header("Game Data")]
    public int[] tricksWonInRound = new int[4];
    public GameUIManager uiManager;
    private int totalTricksPlayed = 0;

    [Header("References")]
    public DeckManager deckManager;
    public HandVisualizer[] players;
    public AuctionUI auctionUI;

    public enum GamePhase { Setup, DealPhase1, PowerAuction, SuitSelection, DealPhase2, FinalBidding, PlayPhase, ScoreBoard }

    [Header("Game State")]
    public GamePhase currentPhase = GamePhase.Setup;
    public Suit currentPowerSuit = Suit.Spades;
    public int gameRoundNumber = 1;
    public int dealerIndex = 0;

    public int[] playerScores = new int[4];
    public int[] finalBids = new int[4];

    private int currentHighestBid = 0;
    private int highestBidderIndex = -1;
    private bool humanInputReceived = false;

    // Play Phase State
    private int currentPlayerIndex = 0;
    private int cardsPlayedInTrick = 0;
    private Suit? currentLeadSuit = null;
    private UICard currentWinningCard = null;
    private int currentTrickWinnerIndex = -1;

    private int GetNextPlayerIndexCCW(int current) => (current + 1) % 4;

    public override void OnNetworkSpawn()
    {
        DG.Tweening.DOTween.SetTweensCapacity(5000, 200);
        if (IsServer)
        {
            Debug.Log("[HOST] Server Started. Initializing Game...");
            StartSession();
        }
        else
        {
            Debug.Log("[CLIENT] Connected to Server. Waiting for game state...");
        }
    }

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

        if (gameRoundNumber > 1)
            dealerIndex = GetPlayerWithLowestScore();
        else
            dealerIndex = Random.Range(0, 4);

        Debug.Log($"Round {gameRoundNumber} Start. Dealer: Player {dealerIndex}");

        currentPhase = GamePhase.DealPhase1;
        StartCoroutine(DealRoundOne());
    }

    // --- PHASE 1: INITIAL DEAL (5 Cards) ---
    IEnumerator DealRoundOne()
    {
        int startNode = GetNextPlayerIndexCCW(dealerIndex);
        Vector3 dealerPos = players[dealerIndex].transform.position;

        for (int c = 0; c < 5; c++)
        {
            int p = startNode;
            for (int k = 0; k < 4; k++)
            {
                var cardData = deckManager.DrawCard();
                if (cardData != null)
                {
                    SpawnAndGiveCard(p, cardData, dealerPos);

                    if (AudioManager.Instance != null) AudioManager.Instance.PlayDeal();
                }
                p = GetNextPlayerIndexCCW(p);
                yield return new WaitForSeconds(0.03f);
            }
        }
        StartCoroutine(RunAuctionSequence());
    }

    // --- HELPER TO SPAWN AND PARENT CARDS CORRECTLY ---
   void SpawnAndGiveCard(int playerIndex, Card cardData, Vector3 startPos)
{
    if (!IsServer) return;

    // 1. Instantiate
    UICard newCardInstance = Instantiate(players[playerIndex].cardPrefab, startPos, Quaternion.identity);
    
    // 2. Init Local (Server sees it immediately)
    newCardInstance.Init(cardData, players[playerIndex].cardTheme);

    // 3. SPAWN FIRST (Fixes "NetworkVariable written to..." warning)
    var netObj = newCardInstance.GetComponent<NetworkObject>();
    netObj.Spawn();

    // 4. SET DATA AFTER SPAWN (Syncs to Clients)
    var netSync = newCardInstance.GetComponent<CardNetworkSync>();
    if (netSync != null)
    {
        netSync.netOwnerIndex.Value = playerIndex;
        
        // Cast enums to int for transmission
        netSync.netSuit.Value = (int)cardData.suit;
        netSync.netRank.Value = (int)cardData.rank;
    }

    // 5. Add to Server's visual hand list
    players[playerIndex].AddCardToHand(newCardInstance, startPos);
}

    // --- PHASE 2: AUCTION ---
    IEnumerator RunAuctionSequence()
    {
        currentPhase = GamePhase.PowerAuction;
        currentHighestBid = 0;
        highestBidderIndex = -1;

        if (gameRoundNumber == 1)
        {
            currentPowerSuit = Suit.Spades;
            uiManager.SetPowerSuitDisplay(currentPowerSuit);
            yield return new WaitForSeconds(1.0f);
            StartCoroutine(DealRoundTwo());
            yield break;
        }

        int startPlayer = GetNextPlayerIndexCCW(dealerIndex);

        for (int k = 0; k < 4; k++)
        {
            int activeBidder = (startPlayer + k) % 4;

            if (players[activeBidder].isHuman)
            {
                humanInputReceived = false;
                auctionUI.ShowHumanTurn(currentHighestBid, highestBidderIndex);
                yield return new WaitUntil(() => humanInputReceived);
            }
            else
            {
                var botHandData = players[activeBidder].currentHandObjects.Select(c => c.Data).ToList();
                int botBid = BotAI.CalculateAuctionBid(botHandData);

                if (botBid >= 5 && botBid > currentHighestBid)
                {
                    currentHighestBid = botBid;
                    highestBidderIndex = activeBidder;
                }
                yield return new WaitForSeconds(0.5f);
            }
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
            if (highestBidderIndex == -1)
            {
                currentPowerSuit = Suit.Spades;
            }
            else
            {
                var botHand = players[highestBidderIndex].currentHandObjects.Select(c => c.Data).ToList();
                var bestSuitGroup = botHand.Where(c => c.suit != Suit.Spades)
                                           .GroupBy(c => c.suit)
                                           .OrderByDescending(g => g.Count())
                                           .FirstOrDefault();

                currentPowerSuit = (bestSuitGroup != null) ? bestSuitGroup.Key : Suit.Spades;
            }

            uiManager.SetPowerSuitDisplay(currentPowerSuit);
            StartCoroutine(DealRoundTwo());
        }
    }

    public void OnHumanSuitSelected(Suit s)
    {
        currentPowerSuit = s;
        uiManager.SetPowerSuitDisplay(currentPowerSuit);
        auctionUI.HideAll();
        StartCoroutine(DealRoundTwo());
    }

    // --- PHASE 3: SECOND DEAL ---
    IEnumerator DealRoundTwo()
    {
        currentPhase = GamePhase.DealPhase2;
        int startNode = GetNextPlayerIndexCCW(dealerIndex);
        Vector3 dealerPos = players[dealerIndex].transform.position;

        for (int i = 0; i < 8; i++)
        {
            int p = startNode;
            for (int k = 0; k < 4; k++)
            {
                var card = deckManager.DrawCard();
                if (card != null)
                {
                    SpawnAndGiveCard(p, card, dealerPos); // Uses the new Helper
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayDeal();
                }
                p = GetNextPlayerIndexCCW(p);
                yield return new WaitForSeconds(0.02f);
            }
        }

        foreach (var p in players) p.SortHand();
        StartCoroutine(RunFinalBidPhase());
    }

    // --- PHASE 4: FINAL BIDDING ---
    IEnumerator RunFinalBidPhase()
    {
        currentPhase = GamePhase.FinalBidding;
        uiManager.ClearAllBids();

        int startPlayer = GetNextPlayerIndexCCW(dealerIndex);

        for (int k = 0; k < 4; k++)
        {
            int i = (startPlayer + k) % 4;
            int minBid = 2;

            if (highestBidderIndex != -1 && i == highestBidderIndex && currentHighestBid > 2)
                minBid = currentHighestBid;

            if (players[i].isHuman)
            {
                humanInputReceived = false;
                auctionUI.ShowFinalBidSelector(minBid);
                yield return new WaitUntil(() => humanInputReceived);
            }
            else
            {
                var botHandData = players[i].currentHandObjects.Select(c => c.Data).ToList();
                finalBids[i] = BotAI.CalculateFinalBid(botHandData, currentPowerSuit);

                if (highestBidderIndex != -1 && i == highestBidderIndex && finalBids[i] < minBid)
                    finalBids[i] = minBid;

                yield return new WaitForSeconds(0.5f);
            }
            uiManager.SetBidText(i, finalBids[i].ToString());
        }

        int totalBids = 0;
        foreach (var b in finalBids) totalBids += b;
        if (totalBids < 11)
        {
            Debug.LogError("MISDEAL! Redealing...");
            yield return new WaitForSeconds(2.0f);
            StartGame();
            yield break;
        }

        tricksWonInRound = new int[4];
        totalTricksPlayed = 0;
        auctionUI.HideAll();
        currentPhase = GamePhase.PlayPhase;

        for (int i = 0; i < 4; i++) uiManager.SetBidText(i, $"0 / {finalBids[i]}");

        int leader = (highestBidderIndex != -1) ? highestBidderIndex : GetNextPlayerIndexCCW(dealerIndex);
        StartNewTrick(leader);
    }

    public void OnHumanFinalBidSubmitted(int amount)
    {
        finalBids[0] = amount;
        humanInputReceived = true;
    }

    // --- PHASE 5: PLAY ---
    void StartNewTrick(int leader)
    {
        cardsPlayedInTrick = 0;
        currentLeadSuit = null;
        currentWinningCard = null;
        currentTrickWinnerIndex = -1;
        currentPlayerIndex = leader;

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

        var handData = bot.currentHandObjects.Select(c => c.Data).ToList();
        List<Card> tableCards = new List<Card>();

        foreach (var p in players)
        {
            if (p.playSlot.childCount > 0)
            {
                var uiCard = p.playSlot.GetComponentInChildren<UICard>();
                if (uiCard != null) tableCards.Add(uiCard.Data);
            }
        }

        Card bestCardData = BotAI.ChooseCardToPlay(handData, tableCards, currentLeadSuit, currentPowerSuit);
        UICard cardToPlay = bot.currentHandObjects.First(c => c.Data == bestCardData);

        PlayCard(cardToPlay, currentPlayerIndex);
    }

    void PlayCard(UICard c, int pIdx)
    {
        if (cardsPlayedInTrick == 0) currentLeadSuit = c.Data.suit;

        // --- NETWORK FIX: PARENTING ---
        // When moving to the table, we update Network Parent and UI Parent
        if (IsServer) c.GetComponent<NetworkObject>().TrySetParent(players[pIdx].playSlot);

        // Ensure Visuals update locally immediately
        c.transform.SetParent(players[pIdx].playSlot, false);

        players[pIdx].RemoveCard(c);

        if (c.IsFaceDown) c.SetFaceDown(false);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayClick();

        c.transform.DOLocalMove(Vector3.zero, 0.4f).SetEase(Ease.OutBack);
        float randomRot = Random.Range(-15f, 15f);
        c.transform.DOLocalRotate(new Vector3(0, 0, randomRot), 0.4f);

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
        yield return new WaitForSeconds(0.5f);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayWin();

        yield return new WaitForSeconds(1.0f);

        List<UICard> cardsOnTable = new List<UICard>();
        foreach (var p in players)
        {
            if (p.playSlot.childCount > 0)
            {
                var card = p.playSlot.GetComponentInChildren<UICard>();
                if (card != null) cardsOnTable.Add(card);
            }
        }

        Vector3 winnerPos = players[currentTrickWinnerIndex].transform.position;
        foreach (var c in cardsOnTable)
        {
            c.transform.DOMove(winnerPos, 0.6f).SetEase(Ease.InBack);
            c.transform.DOScale(0.2f, 0.6f);
        }

        yield return new WaitForSeconds(0.6f);
        foreach (var c in cardsOnTable)
        {
            if (IsServer) c.GetComponent<NetworkObject>().Despawn(); // Despawn network object
            else Destroy(c.gameObject);
        }

        tricksWonInRound[currentTrickWinnerIndex]++;
        totalTricksPlayed++;

        int winnerIndex = currentTrickWinnerIndex;
        uiManager.SetBidText(winnerIndex, $"{tricksWonInRound[winnerIndex]} / {finalBids[winnerIndex]}");

        if (totalTricksPlayed >= 13)
            CalculateRoundScores();
        else
            StartNewTrick(currentTrickWinnerIndex);
    }

    void AdvanceTurnCCW()
    {
        currentPlayerIndex = GetNextPlayerIndexCCW(currentPlayerIndex);
        StartTurn();
    }

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
            if (tricks < bid) roundScore = -bid * 10;
            else roundScore = (bid * 10) + (tricks - bid);

            playerScores[i] += roundScore;
        }

        highestBidderIndex = -1;
        StartCoroutine(PrepareNextRound());
    }

    IEnumerator PrepareNextRound()
    {
        Debug.Log($"Round {gameRoundNumber} Over. Preparing Next...");
        yield return new WaitForSeconds(3.0f);
        StopAllCoroutines();

        if (gameRoundNumber >= totalRounds)
        {
            Debug.Log("GAME OVER!");
        }
        else
        {
            gameRoundNumber++;
            currentWinningCard = null;
            currentTrickWinnerIndex = -1;
            currentLeadSuit = null;
            finalBids = new int[4];
            tricksWonInRound = new int[4];
            totalTricksPlayed = 0;
            uiManager.ClearAllBids();
            StartGame();
        }
    }

    void ForceCleanupTable()
    {
        foreach (var p in players) p.ClearHandVisuals();
        foreach (var p in players) { foreach (Transform child in p.playSlot) Destroy(child.gameObject); }

        // Note: For Network objects, cleanup usually involves Despawning, 
        // but this brute force method works for strays in simple setups.
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        base.OnDestroy();
    }
}