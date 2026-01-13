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
    [Header("Game Mode UI")]
    [SerializeField] private GameObject connectionPanel; // Drag your Host/Join UI here
    [SerializeField] private GameObject gameUIPanel;     // Drag your Game HUD here

    [Header("Configuration")]
    public int totalRounds = 5;

    [Header("Scene References")]
    // 0=Bottom, 1=Right, 2=Top, 3=Left
    public HandVisualizer[] visualHands;
    public AuctionUI auctionUI;
    public GameUIManager uiManager;
    public ScoreboardUI scoreboard;

    [HideInInspector] public HandVisualizer[] players;
    private List<Card> deck = new List<Card>();

    public int localPlayerIndex = -1;

    // --- NETWORKED STATE ---
    public NetworkVariable<int> netDealerIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> netCurrentLeadPlayer = new NetworkVariable<int>(0);
    public NetworkVariable<int> netCurrentHighestBid = new NetworkVariable<int>(0);
    public NetworkVariable<int> netHighestBidderIndex = new NetworkVariable<int>(-1);
    public NetworkVariable<int> netCurrentTrump = new NetworkVariable<int>((int)Suit.Spades);
    public NetworkVariable<GamePhase> netCurrentPhase = new NetworkVariable<GamePhase>(GamePhase.Setup);

    public enum GamePhase { Setup, Deal1, Auction, Deal2, Bidding, Play, Score }

    // Local Data
    public int[] playerScores = new int[4];
    public int[] finalBids = new int[4];
    public int[] tricksWon = new int[4];

    // Bot Management
    private bool[] isBot = new bool[4]; // Tracks which seats are AI

    // Logic Vars
    private int cardsPlayedInTrick = 0;
    private Suit? leadSuit = null;
    private UICard currentWinnerCard = null;
    private int currentTrickWinner = -1;
    private bool humanInputReceived = false;

    private bool isGameRunning = false;

    // --------------------------------------------------------------------------------
    // 1. INITIALIZATION & MODE SELECTION
    // --------------------------------------------------------------------------------

    void Start()
    {
        // Reset UI
        if (connectionPanel != null) connectionPanel.SetActive(false);
        if (gameUIPanel != null) gameUIPanel.SetActive(false);

        // Handle Mode
        switch (GameSettings.CurrentMode)
        {
            case GameMode.SinglePlayerAI:
                SetupSinglePlayerGame();
                break;

            case GameMode.LocalLAN:
                SetupLANGame();
                break;

            case GameMode.OnlineRelay:
                SetupRelayGame();
                break;
        }
    }

    private void SetupSinglePlayerGame()
    {
        Debug.Log("[Mode] Starting Single Player Host...");

        // Start Host (Local Player is Server + Client)
        bool started = NetworkManager.Singleton.StartHost();

        if (started)
        {
            // Configure Bots: Player 0 is Human, 1-3 are Bots
            isBot[0] = false;
            isBot[1] = true;
            isBot[2] = true;
            isBot[3] = true;

            // Start Game Logic Immediately
            if (gameUIPanel != null) gameUIPanel.SetActive(true);
            StartGame();
        }
    }

    private void SetupLANGame()
    {
        Debug.Log("[Mode] LAN - Waiting for connection...");
        if (connectionPanel != null) connectionPanel.SetActive(true);
        // Note: Your Buttons in ConnectionPanel should call NetworkManager.StartHost() or StartClient()
        // Once connected, the Host must manually call StartGame() via a UI button or when 4 players connect.
    }

    private void SetupRelayGame()
    {
        Debug.Log("[Mode] Relay - Waiting for setup...");
        if (connectionPanel != null) connectionPanel.SetActive(true);
    }

    // Called by UI Button (Host) or Auto-called in Single Player
    public void StartGame()
    {
        if (isGameRunning)
        {
            Debug.LogWarning("⚠️ StartGame was called twice! Ignoring the second call.");
            return;
        }

        if (IsServer)
        {
            isGameRunning = true; // Lock it
            netDealerIndex.Value = Random.Range(0, 4);
            StartCoroutine(GameLoop());
        }
    }

    // --------------------------------------------------------------------------------
    // 2. NETWORK SPAWN & SETUP
    // --------------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        localPlayerIndex = (int)NetworkManager.Singleton.LocalClientId;
        Debug.Log($"[Identity] Assigned Player Index: {localPlayerIndex}");

        // Setup Visuals
        players = new HandVisualizer[4];
        for (int i = 0; i < 4; i++)
        {
            // Map logical ID to Physical Layout (0-3) relative to local client
            int visualIndex = (i - localPlayerIndex + 4) % 4;

            players[i] = visualHands[visualIndex];
            players[i].playerIndex = i;
            players[i].layoutId = visualIndex;
            players[i].isHuman = true; // Visualizer assumes everyone needs animations
            players[i].ClearHandVisuals();
        }

        // If this is LAN/Relay, we hide the connection panel now that we are spawned
        if (connectionPanel != null) connectionPanel.SetActive(false);
        if (gameUIPanel != null) gameUIPanel.SetActive(true);
    }

    // --------------------------------------------------------------------------------
    // 3. MAIN GAME LOOP
    // --------------------------------------------------------------------------------

    IEnumerator GameLoop()
    {
        yield return new WaitForSeconds(1f);

        for (int round = 1; round <= totalRounds; round++)
        {
            bool validDeal = false;

            // --- DEAL LOOP ---
            while (!validDeal)
            {
                netCurrentPhase.Value = GamePhase.Setup;
                ResetRoundData();
                InitializeDeck();

                // --- DEAL 1 ---
                netCurrentPhase.Value = GamePhase.Deal1;
                yield return StartCoroutine(DealCards(5));

                // --- DEALER PEEK ---
                if (deck.Count > 0)
                {
                    Card peekCard = deck[deck.Count - 1];
                    deck.RemoveAt(deck.Count - 1);
                    SpawnCardNetworked(netDealerIndex.Value, peekCard);
                    ShowDealerPeekClientRpc(netDealerIndex.Value, (int)peekCard.suit, (int)peekCard.rank);
                    yield return new WaitForSeconds(1.0f);
                }

                // --- AUCTION ---
                netCurrentPhase.Value = GamePhase.Auction;
                yield return StartCoroutine(RunAuction(round));

                // --- DEAL 2 ---
                netCurrentPhase.Value = GamePhase.Deal2;
                yield return StartCoroutine(DealRemainingCards());

                SortHandsClientRpc();

                // --- RESHUFFLE CHECK ---
                int poorPlayer = CheckForReshuffleCondition();
                if (poorPlayer != -1)
                {
                    NotifyReshuffleClientRpc(poorPlayer);
                    yield return new WaitForSeconds(2.5f);
                    continue; // Restart Deal
                }

                validDeal = true;
            }

            // --- FINAL BIDS ---
            netCurrentPhase.Value = GamePhase.Bidding;
            yield return StartCoroutine(RunFinalBids());

            // --- PLAY ---
            netCurrentPhase.Value = GamePhase.Play;
            uiManager.ClearAllBids();
            UpdateScoreUI();

            int leader = (netHighestBidderIndex.Value != -1) ? netHighestBidderIndex.Value : (netDealerIndex.Value + 1) % 4;
            netCurrentLeadPlayer.Value = leader;

            for (int trick = 0; trick < 13; trick++)
            {
                yield return StartCoroutine(PlayTrick());
            }

            // --- SCORING ---
            netCurrentPhase.Value = GamePhase.Score;
            CalculateScores();

            // --- NEXT DEALER ---
            int bestPlayer = 0;
            int lowestScore = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                if (playerScores[i] < lowestScore)
                {
                    lowestScore = playerScores[i];
                    bestPlayer = i;
                }
            }
            netDealerIndex.Value = bestPlayer;

            yield return new WaitForSeconds(3f);
        }
    }

    // --------------------------------------------------------------------------------
    // 4. PLAY LOGIC (UPDATED FOR BOTS)
    // --------------------------------------------------------------------------------

    IEnumerator PlayTrick()
    {
        leadSuit = null; currentWinnerCard = null; currentTrickWinner = -1; cardsPlayedInTrick = 0;
        int pIdx = netCurrentLeadPlayer.Value;

        for (int i = 0; i < 4; i++)
        {
            var player = players[pIdx];
            ClearHighlightsClientRpc();

            // === BOT CHECK ===
            if (isBot[pIdx])
            {
                // -- AI LOGIC --
                yield return new WaitForSeconds(1.0f); // Artificial Delay
                PlayBotTurn(pIdx);
            }
            else
            {
                // -- HUMAN LOGIC --
                // Highlight Valid Cards for the Human Client
                var hand = player.currentHandObjects.Select(c => c.Data).ToList();
                var table = GetTableCards();
                var legalCards = BotAI.GetLegalMoves(hand, table, leadSuit, (Suit)netCurrentTrump.Value);

                List<int> flat = new List<int>();
                foreach (var c in legalCards) { flat.Add((int)c.suit); flat.Add((int)c.rank); }

                HighlightCardsClientRpc(pIdx, flat.ToArray());

                humanInputReceived = false;
                yield return new WaitUntil(() => humanInputReceived);
            }

            pIdx = (pIdx + 1) % 4;
            netCurrentLeadPlayer.Value = pIdx;
        }

        ClearHighlightsClientRpc();
        yield return new WaitForSeconds(0.5f);
        AnimateTrickWinClientRpc(currentTrickWinner);
        yield return new WaitForSeconds(1.0f);
        CleanUpTable();
        tricksWon[currentTrickWinner]++;
        netCurrentLeadPlayer.Value = currentTrickWinner;
        UpdateScoreUI();
    }

    // New Function to handle AI Moves
    void PlayBotTurn(int pIdx)
    {
        var playerHandObj = players[pIdx].currentHandObjects;
        var handData = playerHandObj.Select(c => c.Data).ToList();

        // SAFETY: If bot has no cards, stop.
        if (handData.Count == 0) return;

        var table = GetTableCards();

        // 1. Get Legal Moves
        var legalMoves = BotAI.GetLegalMoves(handData, table, leadSuit, (Suit)netCurrentTrump.Value);

        // Fallback if AI returns nothing (shouldn't happen, but safe to handle)
        if (legalMoves.Count == 0) legalMoves = handData;

        // 2. Pick Card
        Card bestCard = legalMoves[Random.Range(0, legalMoves.Count)];

        // 3. Find the visual object
        // SAFETY: Ensure the card actually exists in the visual hand before playing
        var cardObjToPlay = playerHandObj.FirstOrDefault(c => c.Data.suit == bestCard.suit && c.Data.rank == bestCard.rank);

        if (cardObjToPlay != null)
        {
            PlayCardLogic(pIdx, cardObjToPlay);
        }
        else
        {
            Debug.LogError($"Bot {pIdx} tried to play {bestCard.rank} of {bestCard.suit}, but didn't have it!");
        }
    }
    void PlayCardLogic(int playerIndex, UICard card)
    {
        if (cardsPlayedInTrick == 0) leadSuit = card.Data.suit;

        // We need the NetID to animate on clients
        AnimateCardPlayClientRpc(card.GetComponent<NetworkObject>().NetworkObjectId, playerIndex);

        // Determine Winner logic
        bool isNewWinner = false;
        Suit trmp = (Suit)netCurrentTrump.Value;

        if (currentWinnerCard == null) isNewWinner = true;
        else
        {
            bool isTrump = card.Data.suit == trmp;
            bool winIsTrump = currentWinnerCard.Data.suit == trmp;
            if (isTrump && !winIsTrump) isNewWinner = true;
            else if (isTrump == winIsTrump)
            {
                if (card.Data.suit == currentWinnerCard.Data.suit)
                {
                    if (card.Data.rank > currentWinnerCard.Data.rank) isNewWinner = true;
                }
                else if (card.Data.suit == leadSuit) isNewWinner = true;
            }
        }

        if (isNewWinner) { currentWinnerCard = card; currentTrickWinner = playerIndex; }
        cardsPlayedInTrick++;
        humanInputReceived = true; // Unblocks the coroutine
    }

    // --------------------------------------------------------------------------------
    // 5. AUCTION & BIDDING (UPDATED FOR BOTS)
    // --------------------------------------------------------------------------------

    IEnumerator RunAuction(int round)
    {
        netCurrentHighestBid.Value = 0;
        netHighestBidderIndex.Value = -1;

        // Round 1 is always Spades (standard Callbreak rules often dictate this)
        if (round == 1)
        {
            netCurrentTrump.Value = (int)Suit.Spades;
            UpdateTrumpUIClientRpc(Suit.Spades);
            yield return new WaitForSeconds(2.0f);
            yield break;
        }

        int start = (netDealerIndex.Value + 1) % 4;

        // Give everyone one chance to bid (or pass)
        for (int k = 0; k < 4; k++)
        {
            int pIdx = (start + k) % 4;

            if (isBot[pIdx])
            {
                // 1. Get Bot's Hand Data
                var handObjects = players[pIdx].currentHandObjects;
                var handData = handObjects.Select(h => h.Data).ToList();

                // 2. Ask AI: "What is the max I am willing to bid?"
                // This returns 0 if hand is weak, or 5-8 if they have a strong suit
                int maxWillingToBid = BotAI.CalculateAuctionBid(handData);

                // 3. Determine current cost to enter
                int currentHighest = netCurrentHighestBid.Value;
                int minRequired = 5; // Rule: Must bid at least 5 to change suit

                // If current is 0, price is 5. If current is 5, price is 6.
                int costToBid = (currentHighest < minRequired) ? minRequired : (currentHighest + 1);

                // 4. Decision: Do I have enough strength?
                if (maxWillingToBid >= costToBid)
                {
                    // Bid the minimum necessary to take the lead (not our max immediately)
                    netCurrentHighestBid.Value = costToBid;
                    netHighestBidderIndex.Value = pIdx;
                    UpdateBidUIClientRpc(pIdx, costToBid.ToString());
                    yield return new WaitForSeconds(1.0f);
                }
                else
                {
                    UpdateBidUIClientRpc(pIdx, "Pass");
                    yield return new WaitForSeconds(0.5f);
                }
            }
            else
            {
                // Human Logic (Unchanged)
                humanInputReceived = false;
                ShowAuctionUIClientRpc(pIdx, netCurrentHighestBid.Value, netHighestBidderIndex.Value);
                yield return new WaitUntil(() => humanInputReceived);
                HideUIClientRpc();
            }
        }

        // --- SUIT SELECTION PHASE ---
        if (netHighestBidderIndex.Value != -1)
        {
            int winnerIdx = netHighestBidderIndex.Value;
            Suit selectedSuit = Suit.Spades;

            if (isBot[winnerIdx])
            {
                // SMART BOT: Pick the suit they actually have cards for!
                var handObjects = players[winnerIdx].currentHandObjects;
                var handData = handObjects.Select(h => h.Data).ToList();

                // Group cards by suit
                var groups = handData.GroupBy(c => c.suit).OrderByDescending(g => g.Count());

                // Pick the suit with the most cards (that isn't Spades, usually)
                // If BotAI.CalculateAuctionBid suggested a bid, it found a specific suit.
                // For simplicity, we just pick their longest non-Spade suit here.
                foreach (var g in groups)
                {
                    if (g.Key != Suit.Spades)
                    {
                        selectedSuit = g.Key;
                        break;
                    }
                }

                netCurrentTrump.Value = (int)selectedSuit;
                UpdateTrumpUIClientRpc(selectedSuit);
                yield return new WaitForSeconds(1f);
            }
            else
            {
                // Human Selection
                humanInputReceived = false;
                ShowSuitSelectUIClientRpc(winnerIdx);
                yield return new WaitUntil(() => humanInputReceived);
            }
        }
        else
        {
            // No one bid -> Default to Spades
            netCurrentTrump.Value = (int)Suit.Spades;
            UpdateTrumpUIClientRpc(Suit.Spades);
        }
    }

    IEnumerator RunFinalBids()
    {
        uiManager.ClearAllBids();
        int start = (netDealerIndex.Value + 1) % 4;

        for (int k = 0; k < 4; k++)
        {
            int pIdx = (start + k) % 4;

            // Determine the minimum bid allowed for this player
            // (If they won the auction with a bid of 6, they MUST bid at least 6 now)
            int minAllowed = 2; // Standard minimum
            if (pIdx == netHighestBidderIndex.Value && netCurrentHighestBid.Value > 2)
                minAllowed = netCurrentHighestBid.Value;

            if (isBot[pIdx])
            {
                // 1. Get Hand Data
                var handObjects = players[pIdx].currentHandObjects;
                var handData = handObjects.Select(h => h.Data).ToList();

                // 2. Ask AI: "How many tricks can I win?"
                // We pass the currently selected trump suit so it values those cards higher
                Suit currentTrump = (Suit)netCurrentTrump.Value;
                int calculatedBid = BotAI.CalculateFinalBid(handData, currentTrump);

                // 3. Enforce Rules
                // Bid cannot be lower than the auction commitment or game min (2)
                int finalBid = Mathf.Max(calculatedBid, minAllowed);

                // Cap at 13 just in case
                finalBid = Mathf.Min(finalBid, 13);

                // 4. Save and Update UI
                finalBids[pIdx] = finalBid;
                UpdateBidUIClientRpc(pIdx, finalBid.ToString());
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                // Human Logic (Unchanged)
                humanInputReceived = false;
                ShowFinalBidUIClientRpc(pIdx, minAllowed);
                yield return new WaitUntil(() => humanInputReceived);

                // Sync the human's choice to the network var/array
                // (Assumed handled by SubmitBidServerRpc inside OnHumanFinalBidSubmitted)
            }

            // Ensure the UI reflects the final stored value
            UpdateBidUIClientRpc(pIdx, finalBids[pIdx].ToString());
        }
    }


    // --------------------------------------------------------------------------------
    // 6. SERVER RPCs (INPUT)
    // --------------------------------------------------------------------------------

    [ServerRpc(RequireOwnership = false)]
    public void PlayCardInputServerRpc(ulong netId, int pIdx)
    {
        if (netCurrentPhase.Value != GamePhase.Play || netCurrentLeadPlayer.Value != pIdx) return;
        // Anti-Cheat: Don't allow input if this slot is a Bot
        if (isBot[pIdx]) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var obj))
        {
            UICard cardToPlay = obj.GetComponent<UICard>();

            // STRICT SERVER VALIDATION
            var hand = players[pIdx].currentHandObjects.Select(x => x.Data).ToList();
            var table = GetTableCards();
            Suit? currentLead = (cardsPlayedInTrick == 0) ? null : leadSuit;
            var legal = BotAI.GetLegalMoves(hand, table, currentLead, (Suit)netCurrentTrump.Value);

            bool isLegal = legal.Any(l => l.suit == cardToPlay.Data.suit && l.rank == cardToPlay.Data.rank);
            if (!isLegal)
            {
                Debug.LogWarning($"[Server] Illegal Move by Player {pIdx}");
                return;
            }
            PlayCardLogic(pIdx, cardToPlay);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitBidServerRpc(int a, int p)
    {
        // Anti-Cheat: Block bot slots
        if (isBot[p]) return;

        if (netCurrentPhase.Value == GamePhase.Auction)
        {
            if (a > 0)
            {
                netCurrentHighestBid.Value = a;
                netHighestBidderIndex.Value = p;
            }
            humanInputReceived = true;
            UpdateBidUIClientRpc(p, a.ToString());
        }
        else if (netCurrentPhase.Value == GamePhase.Bidding)
        {
            finalBids[p] = a;
            humanInputReceived = true;
            UpdateBidUIClientRpc(p, a.ToString());
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectTrumpServerRpc(Suit s)
    {
        netCurrentTrump.Value = (int)s;
        humanInputReceived = true;
        UpdateTrumpUIClientRpc(s);
    }

    // --------------------------------------------------------------------------------
    // 7. CLIENT RPCs (DISPLAY)
    // --------------------------------------------------------------------------------

    [ClientRpc]
    void ShowDealerPeekClientRpc(int dIdx, int s, int r)
    {
        if (localPlayerIndex == dIdx) Debug.Log($"<color=yellow><b>[PEEK]</b> You received {(Rank)r} of {(Suit)s}</color>");
    }
    [ClientRpc] void NotifyReshuffleClientRpc(int pIdx) { Debug.Log($"<color=red>Reshuffle requested by Player {pIdx}</color>"); }

    [ClientRpc]
    void HighlightCardsClientRpc(int pIdx, int[] flatData)
    {
        if (localPlayerIndex != pIdx) return;
        List<Card> cards = new List<Card>();
        if (flatData != null)
        {
            for (int i = 0; i < flatData.Length; i += 2)
                cards.Add(new Card((Suit)flatData[i], (Rank)flatData[i + 1]));
        }
        players[localPlayerIndex].HighlightValidCards(cards);
    }

    [ClientRpc] void ClearHighlightsClientRpc() { if (localPlayerIndex >= 0) players[localPlayerIndex].ClearHighlights(); }
    [ClientRpc] void SortHandsClientRpc() { foreach (var p in players) p.SortHand(); }
    [ClientRpc] void ShowAuctionUIClientRpc(int client, int bid, int winner) { if (localPlayerIndex == client) auctionUI.ShowHumanTurn(bid, winner); }
    [ClientRpc] void ShowSuitSelectUIClientRpc(int client) { if (localPlayerIndex == client) auctionUI.ShowSuitSelection(); }
    [ClientRpc] void ShowFinalBidUIClientRpc(int client, int min) { if (localPlayerIndex == client) auctionUI.ShowFinalBidSelector(min); }
    [ClientRpc] void HideUIClientRpc() { auctionUI.HideAll(); }
    [ClientRpc] void UpdateBidUIClientRpc(int p, string t) { uiManager.SetBidText(p, t); }
    [ClientRpc] void UpdateTrumpUIClientRpc(Suit s) { uiManager.SetPowerSuitDisplay(s); }
    [ClientRpc] void UpdateScoreboardClientRpc(int[] currentScores, int dealerIdx) { if (scoreboard != null) scoreboard.UpdateScoreboard(currentScores, dealerIdx, localPlayerIndex); }

    [ClientRpc]
    void AnimateCardPlayClientRpc(ulong netId, int pIdx)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var obj))
        {
            var card = obj.GetComponent<UICard>();
            var p = players[pIdx];
            card.transform.SetParent(p.playSlot, false);
            p.RemoveCard(card);
            card.SetFaceDown(false);
            card.transform.DOLocalMove(Vector3.zero, 0.4f).SetEase(Ease.OutBack);
            float rot = Random.Range(-15f, 15f);
            card.transform.DOLocalRotate(new Vector3(0, 0, rot), 0.4f);
            if (AudioManager.Instance) AudioManager.Instance.PlayClick();
        }
    }

    [ClientRpc]
    void AnimateTrickWinClientRpc(int winnerIdx)
    {
        Vector3 target = players[winnerIdx].transform.position;
        foreach (var p in players)
        {
            if (p.playSlot.childCount > 0)
            {
                Transform t = p.playSlot.GetChild(0);
                t.DOMove(target, 0.6f).SetEase(Ease.InBack);
                t.DOScale(0.3f, 0.6f);
            }
        }
        if (AudioManager.Instance) AudioManager.Instance.PlayWin();
    }

    // --------------------------------------------------------------------------------
    // 8. HELPERS & INPUT
    // --------------------------------------------------------------------------------

    void InitializeDeck()
    {
        deck.Clear();
        Suit[] suits = { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };
        foreach (Suit s in suits)
        {
            foreach (Rank r in System.Enum.GetValues(typeof(Rank)))
            {
                if (r == Rank.None || r == Rank.Joker || r.ToString() == "Hidden") continue;
                deck.Add(new Card(s, r));
            }
        }
        for (int i = 0; i < deck.Count; i++) { Card temp = deck[i]; int rnd = Random.Range(i, deck.Count); deck[i] = deck[rnd]; deck[rnd] = temp; }
    }

    IEnumerator DealCards(int countPerPlayer)
    {
        int startPlayer = (netDealerIndex.Value + 1) % 4;
        for (int i = 0; i < countPerPlayer; i++)
        {
            int pIdx = startPlayer;
            for (int k = 0; k < 4; k++)
            {
                if (deck.Count > 0)
                {
                    Card data = deck[0]; deck.RemoveAt(0);
                    SpawnCardNetworked(pIdx, data);
                }
                pIdx = (pIdx + 1) % 4;
                yield return new WaitForSeconds(0.05f);
            }
        }
    }

    IEnumerator DealRemainingCards()
    {
        int pIdx = (netDealerIndex.Value + 1) % 4;
        while (deck.Count > 0)
        {
            Card data = deck[0]; deck.RemoveAt(0);
            SpawnCardNetworked(pIdx, data);
            pIdx = (pIdx + 1) % 4;
            yield return new WaitForSeconds(0.05f);
        }
    }

    private int CheckForReshuffleCondition()
    {
        for (int i = 0; i < 4; i++)
        {
            bool hasFace = players[i].currentHandObjects.Any(c => c.Data != null && (int)c.Data.rank >= 11);
            if (!hasFace) return i;
        }
        return -1;
    }

    void SpawnCardNetworked(int ownerIndex, Card data)
    {
        var hand = players[ownerIndex];
        Vector3 spawnPos = players[netDealerIndex.Value].transform.position;
        var instance = Instantiate(hand.cardPrefab, spawnPos, Quaternion.identity);
        instance.Init(data, hand.cardTheme);
        instance.GetComponent<NetworkObject>().Spawn();

        var sync = instance.GetComponent<CardNetworkSync>();
        sync.netOwnerIndex.Value = ownerIndex;
        sync.netSuit.Value = (int)data.suit;
        sync.netRank.Value = (int)data.rank;

        hand.AddCardToHand(instance, spawnPos);
    }

    List<Card> GetTableCards()
    {
        List<Card> t = new List<Card>();
        foreach (var p in players)
        {
            if (p.playSlot.childCount > 0)
                t.Add(p.playSlot.GetComponentInChildren<UICard>().Data);
        }
        return t;
    }

    void CleanUpTable()
    {
        foreach (var p in players)
        {
            if (p.playSlot.childCount > 0 && IsServer)
                p.playSlot.GetComponentInChildren<NetworkObject>().Despawn();
        }
    }

    void UpdateScoreUI() { for (int i = 0; i < 4; i++) uiManager.SetBidText(i, $"{tricksWon[i]} / {finalBids[i]}"); }

    void ResetRoundData()
    {
        tricksWon = new int[4];
        finalBids = new int[4];
        foreach (var p in players) p.ClearHandVisuals();
        uiManager.ClearAllBids();
    }

    void CalculateScores()
    {
        for (int i = 0; i < 4; i++)
        {
            int b = finalBids[i];
            int t = tricksWon[i];
            int roundScore = (t < b) ? -b * 10 : (b * 10) + (t - b);
            playerScores[i] += roundScore;
        }
        UpdateScoreboardClientRpc(playerScores, netDealerIndex.Value);
    }

    // INPUT HOOKS
    public void OnHumanAuctionAction(int b) { SubmitBidServerRpc(b, localPlayerIndex); }
    public void OnHumanSuitSelected(Suit s) { SelectTrumpServerRpc(s); }
    public void OnHumanFinalBidSubmitted(int b) { SubmitBidServerRpc(b, localPlayerIndex); }
    public void OnCardClicked(CardInteraction c)
    {
        if (netCurrentLeadPlayer.Value != localPlayerIndex) return;
        var netSync = c.GetComponent<CardNetworkSync>();
        if (netSync == null || netSync.netOwnerIndex.Value != localPlayerIndex) return;
        PlayCardInputServerRpc(c.GetComponent<NetworkObject>().NetworkObjectId, localPlayerIndex);
    }
    public override void OnNetworkDespawn()
    {
        isGameRunning = false;
        StopAllCoroutines(); // Kill any running loops
        base.OnNetworkDespawn();
    }
}