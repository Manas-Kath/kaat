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
    public static GameController Instance; // SINGLETON

    [Header("Game Mode UI")]
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private GameObject gameUIPanel;

    [Header("Configuration")]
    public int totalRounds = 5;

    [Header("Scene References")]
    public HandVisualizer[] visualHands;
    public AuctionUI auctionUI;
    public GameUIManager uiManager;
    public ScoreboardUI scoreboard;

    [HideInInspector] public HandVisualizer[] players;
    private List<Card> deck = new List<Card>();

    // LOGICAL TRACKER (Instant, unlike the UI)
    private List<Card> serverTrickCards = new List<Card>();

    public int localPlayerIndex = -1;

    // --- NETWORKED STATE ---
    public NetworkVariable<int> netDealerIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> netCurrentLeadPlayer = new NetworkVariable<int>(0);
    public NetworkVariable<int> netCurrentHighestBid = new NetworkVariable<int>(0);
    public NetworkVariable<int> netHighestBidderIndex = new NetworkVariable<int>(-1);
    public NetworkVariable<int> netCurrentTrump = new NetworkVariable<int>((int)Suit.Spades);
    public NetworkVariable<GamePhase> netCurrentPhase = new NetworkVariable<GamePhase>(GamePhase.Setup);

    // KAAT v3.0: Trump Lock Flag
    public NetworkVariable<bool> netTrumpLocked = new NetworkVariable<bool>(false);

    public enum GamePhase { Setup, Deal1, Auction, Deal2, Bidding, Play, Score }

    public int[] playerScores = new int[4];
    public int[] finalBids = new int[4];
    public int[] tricksWon = new int[4];

    // --- FIX: PLAYER NAMES CACHE ---
    private string[] networkedNames = new string[] { "Player 0", "Player 1", "Player 2", "Player 3" };

    private bool[] isBot = new bool[4];

    private int cardsPlayedInTrick = 0;
    private Suit? leadSuit = null;
    private UICard currentWinnerCard = null;
    private int currentTrickWinner = -1;
    private bool humanInputReceived = false;

    private bool isGameRunning = false;

    // --------------------------------------------------------------------------------
    // 1. INITIALIZATION
    // --------------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Default State: Show Connection, Hide Game
        if (connectionPanel != null) connectionPanel.SetActive(true);
        if (gameUIPanel != null) gameUIPanel.SetActive(false);

        switch (GameSettings.CurrentMode)
        {
            case GameMode.SinglePlayerAI: SetupSinglePlayerGame(); break;
            case GameMode.LocalLAN: SetupLANGame(); break;
            case GameMode.OnlineRelay: SetupRelayGame(); break;
        }
    }

    // --- SETUP LOGIC ---

    private void SetupSinglePlayerGame()
    {
        Debug.Log("[Mode] Starting Single Player Host...");
        bool started = NetworkManager.Singleton.StartHost();
        if (started)
        {
            isBot[0] = false; isBot[1] = true; isBot[2] = true; isBot[3] = true;

            // --- FIX: Set Bot Names ---
            networkedNames = new string[] { ConnectionManager.LocalPlayerName, "Bot 1", "Bot 2", "Bot 3" };

            // Single Player bypasses the "Start Match" button, so we swap UI manually here
            if (connectionPanel != null) connectionPanel.SetActive(false);
            if (gameUIPanel != null) gameUIPanel.SetActive(true);

            StartGame();
        }
    }

    private void SetupLANGame() { if (connectionPanel != null) connectionPanel.SetActive(true); }
    private void SetupRelayGame() { if (connectionPanel != null) connectionPanel.SetActive(true); }

    public override void OnNetworkSpawn()
    {
        localPlayerIndex = (int)NetworkManager.Singleton.LocalClientId;
        players = new HandVisualizer[4];
        for (int i = 0; i < 4; i++)
        {
            int visualIndex = (i - localPlayerIndex + 4) % 4;
            players[i] = visualHands[visualIndex];
            players[i].playerIndex = i;
            players[i].layoutId = visualIndex;
            players[i].isHuman = (i == localPlayerIndex);
            players[i].ClearHandVisuals();
        }

        // --- FIX: REGISTER NAME ---
        // Send our name to the server so it can update the scoreboard
        if (IsClient)
        {
            RegisterNameServerRpc(ConnectionManager.LocalPlayerName, localPlayerIndex);
        }
    }

    // --------------------------------------------------------------------------------
    // 2. START MATCH TRIGGERS (NEW)
    // --------------------------------------------------------------------------------

    public void StartMultiplayerMatch()
    {
        if (IsServer)
        {
            StartGameClientRpc();
            StartGame();
        }
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        if (connectionPanel != null) connectionPanel.SetActive(false);
        if (gameUIPanel != null) gameUIPanel.SetActive(true);
    }

    // Internal start logic
    private void StartGame()
    {
        if (isGameRunning) return;
        if (IsServer)
        {
            isGameRunning = true;
            netDealerIndex.Value = Random.Range(0, 4);

            // --- FIX: SYNC NAMES ---
            // Send the collected names to all clients' scoreboards
            // (Pass elements individually to avoid array serialization errors)
            SyncNamesClientRpc(networkedNames[0], networkedNames[1], networkedNames[2], networkedNames[3]);

            // Update initial "D" marker
            UpdateScoreboardClientRpc(playerScores, netDealerIndex.Value);

            StartCoroutine(GameLoop());
        }
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

            while (!validDeal)
            {
                netCurrentPhase.Value = GamePhase.Setup;
                ResetRoundData();

                InitializeDeck(round);
                netTrumpLocked.Value = false;

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

                int poorPlayer = CheckForReshuffleCondition();
                if (poorPlayer != -1)
                {
                    NotifyReshuffleClientRpc(poorPlayer);
                    yield return new WaitForSeconds(2.5f);
                    continue;
                }

                validDeal = true;
            }

            // --- FINAL BIDS ---
            netCurrentPhase.Value = GamePhase.Bidding;
            yield return StartCoroutine(RunFinalBids());

            // --- NEW: MISDEAL LOGIC ---
            // Rule: Total bids must be >= 11 (unless a 10-call is active)
            int totalBids = finalBids.Sum();
            bool isKaat = finalBids.Contains(10);

            if (totalBids < 11 && !isKaat)
            {
                Debug.Log($"<color=red>MISDEAL! Total bids: {totalBids}. Minimum 11 required. Redealing...</color>");

                // Show "MISDEAL" on UI for all players
                for (int i = 0; i < 4; i++) UpdateBidUIClientRpc(i, "MISDEAL");

                yield return new WaitForSeconds(3.0f);

                // Decrement round counter so we repeat the same round number
                round--;

                // Restart loop immediately. 
                // NOTE: We do NOT change the dealer index here, satisfying the "Same Dealer Redeals" rule.
                continue;
            }

            // --- PLAY ---
            netCurrentPhase.Value = GamePhase.Play;

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
            UpdateScoreboardClientRpc(playerScores, netDealerIndex.Value);

            yield return new WaitForSeconds(3f);
        }
    }

    // --------------------------------------------------------------------------------
    // 4. PLAY LOGIC
    // --------------------------------------------------------------------------------

    IEnumerator PlayTrick()
    {
        leadSuit = null; currentWinnerCard = null; currentTrickWinner = -1; cardsPlayedInTrick = 0;

        // --- FIX: Clear logical tracker ---
        serverTrickCards.Clear();

        int pIdx = netCurrentLeadPlayer.Value;

        for (int i = 0; i < 4; i++)
        {
            var player = players[pIdx];
            ClearHighlightsClientRpc();

            if (isBot[pIdx])
            {
                yield return new WaitForSeconds(1.0f);
                PlayBotTurn(pIdx);
            }
            else
            {
                var hand = player.currentHandObjects.Select(c => c.Data).ToList();
                // --- FIX: Use logical table tracker for accuracy ---
                var table = new List<Card>(serverTrickCards);

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

    void PlayBotTurn(int pIdx)
    {
        var playerHandObj = players[pIdx].currentHandObjects;
        var handData = playerHandObj.Select(c => c.Data).ToList();
        if (handData.Count == 0) return;

        // Use logical tracker
        var table = new List<Card>(serverTrickCards);

        var legalMoves = BotAI.GetLegalMoves(handData, table, leadSuit, (Suit)netCurrentTrump.Value);
        if (legalMoves.Count == 0) legalMoves = handData;

        Card cardToPlay = BotAI.ChooseCardToPlay(handData, table, leadSuit, (Suit)netCurrentTrump.Value);

        // --- FIX: Remove .Data from cardToPlay (it is already a Card) ---
        var cardObjToPlay = playerHandObj.FirstOrDefault(c => c.Data.suit == cardToPlay.suit && c.Data.rank == cardToPlay.rank);

        if (cardObjToPlay != null) PlayCardLogic(pIdx, cardObjToPlay);
    }

    void PlayCardLogic(int playerIndex, UICard card)
    {
        if (cardsPlayedInTrick == 0)
        {
            leadSuit = card.Data.suit;
            serverTrickCards.Clear();
        }

        // --- FIX: Update Logical Tracker ---
        serverTrickCards.Add(card.Data);

        AnimateCardPlayClientRpc(card.GetComponent<NetworkObject>().NetworkObjectId, playerIndex);

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
        humanInputReceived = true;
    }

    // --------------------------------------------------------------------------------
    // 5. AUCTION & BIDDING
    // --------------------------------------------------------------------------------

    IEnumerator RunAuction(int round)
    {
        netCurrentHighestBid.Value = 0;
        netHighestBidderIndex.Value = -1;

        if (round == 1)
        {
            netCurrentTrump.Value = (int)Suit.Spades;
            UpdateTrumpUIClientRpc(Suit.Spades);
            yield return new WaitForSeconds(2.0f);
            yield break;
        }

        int start = (netDealerIndex.Value + 1) % 4;

        for (int k = 0; k < 4; k++)
        {
            int pIdx = (start + k) % 4;

            if (isBot[pIdx])
            {
                var handData = players[pIdx].currentHandObjects.Select(h => h.Data).ToList();
                int maxWillingToBid = BotAI.CalculateAuctionBid(handData);
                int currentHighest = netCurrentHighestBid.Value;
                int minRequired = 5;
                int costToBid = (currentHighest < minRequired) ? minRequired : (currentHighest + 1);

                if (maxWillingToBid >= costToBid)
                {
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
                humanInputReceived = false;
                ShowAuctionUIClientRpc(pIdx, netCurrentHighestBid.Value, netHighestBidderIndex.Value);
                yield return new WaitUntil(() => humanInputReceived);
                HideUIClientRpc();
            }
        }

        if (netHighestBidderIndex.Value != -1)
        {
            int winnerIdx = netHighestBidderIndex.Value;
            Suit selectedSuit = Suit.Spades;

            if (isBot[winnerIdx])
            {
                var handData = players[winnerIdx].currentHandObjects.Select(h => h.Data).ToList();
                var groups = handData.GroupBy(c => c.suit).OrderByDescending(g => g.Count());
                foreach (var g in groups) { if (g.Key != Suit.Spades) { selectedSuit = g.Key; break; } }

                netCurrentTrump.Value = (int)selectedSuit;
                UpdateTrumpUIClientRpc(selectedSuit);
                yield return new WaitForSeconds(1f);
            }
            else
            {
                humanInputReceived = false;
                ShowSuitSelectUIClientRpc(winnerIdx);
                yield return new WaitUntil(() => humanInputReceived);
            }
        }
        else
        {
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

            bool kaatActive = finalBids.Any(b => b == 10);

            if (kaatActive && finalBids[pIdx] != 10)
            {
                finalBids[pIdx] = 2; // Store logical value
                UpdateBidUIClientRpc(pIdx, "2");
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            int minAllowed = 2;
            if (pIdx == netHighestBidderIndex.Value && netCurrentHighestBid.Value > 2)
                minAllowed = netCurrentHighestBid.Value;

            if (isBot[pIdx])
            {
                var handData = players[pIdx].currentHandObjects.Select(h => h.Data).ToList();
                Suit currentTrump = (Suit)netCurrentTrump.Value;
                int calculatedBid = BotAI.CalculateFinalBid(handData, currentTrump);
                int finalBid = Mathf.Max(calculatedBid, minAllowed);
                finalBid = Mathf.Min(finalBid, 13);

                finalBids[pIdx] = finalBid;
                UpdateBidUIClientRpc(pIdx, finalBid.ToString());

                if (finalBid == 10) ForceKaatBids(pIdx);

                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                humanInputReceived = false;
                ShowFinalBidUIClientRpc(pIdx, minAllowed);
                yield return new WaitUntil(() => humanInputReceived);
            }
            UpdateBidUIClientRpc(pIdx, finalBids[pIdx].ToString());
        }
    }

    // --------------------------------------------------------------------------------
    // 6. SERVER RPCs
    // --------------------------------------------------------------------------------

    // --- FIX: Name Registration RPC ---
    [ServerRpc(RequireOwnership = false)]
    public void RegisterNameServerRpc(string name, int index)
    {
        if (index >= 0 && index < 4)
        {
            networkedNames[index] = name;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestLateTrumpChangeServerRpc(int playerIndex, Suit newSuit)
    {
        if (netCurrentPhase.Value != GamePhase.Bidding) return;
        if (netTrumpLocked.Value) return;
        if (isBot[playerIndex]) return;

        netCurrentTrump.Value = (int)newSuit;
        netTrumpLocked.Value = true;
        finalBids[playerIndex] = 10;

        ForceKaatBids(playerIndex);

        UpdateTrumpUIClientRpc(newSuit);
        UpdateBidUIClientRpc(playerIndex, "10");

        humanInputReceived = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlayCardInputServerRpc(ulong netId, int pIdx)
    {
        if (netCurrentPhase.Value != GamePhase.Play || netCurrentLeadPlayer.Value != pIdx) return;
        if (isBot[pIdx]) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var obj))
        {
            UICard cardToPlay = obj.GetComponent<UICard>();
            var hand = players[pIdx].currentHandObjects.Select(x => x.Data).ToList();
            var table = new List<Card>(serverTrickCards); // Use logical tracker
            Suit? currentLead = (cardsPlayedInTrick == 0) ? null : leadSuit;
            var legal = BotAI.GetLegalMoves(hand, table, currentLead, (Suit)netCurrentTrump.Value);

            if (!legal.Any(l => l.suit == cardToPlay.Data.suit && l.rank == cardToPlay.Data.rank))
                return;

            PlayCardLogic(pIdx, cardToPlay);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitBidServerRpc(int a, int p)
    {
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
            if (a == 10) ForceKaatBids(p);
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
    // 7. CLIENT RPCs (RESTORED FROM DESTRUCTION)
    // --------------------------------------------------------------------------------

    // --- FIX: Name Synchronization RPC ---
    [ClientRpc]
    public void SyncNamesClientRpc(string n0, string n1, string n2, string n3)
    {
        if (scoreboard != null)
        {
            // Re-pack them into an array for the scoreboard
            scoreboard.UpdateNames(new string[] { n0, n1, n2, n3 });
            // Add back the scoreboard visual update
            scoreboard.UpdateScoreboard(playerScores, netDealerIndex.Value, localPlayerIndex);
        }
    }

    // --- FIX: Clear Bids RPC ---
    [ClientRpc]
    private void ClearBidsClientRpc()
    {
        // THIS WAS EMPTY - RESTORED
        if (uiManager != null) uiManager.ClearAllBids();
    }

    [ClientRpc] void ShowDealerPeekClientRpc(int dIdx, int s, int r) { if (localPlayerIndex == dIdx) Debug.Log($"<color=yellow><b>[PEEK]</b> {(Rank)r} of {(Suit)s}</color>"); }
    [ClientRpc] void NotifyReshuffleClientRpc(int pIdx) { Debug.Log($"<color=red>Reshuffle requested by Player {pIdx}</color>"); }
    [ClientRpc]
    void HighlightCardsClientRpc(int pIdx, int[] flatData)
    {
        if (localPlayerIndex != pIdx) return;
        List<Card> cards = new List<Card>();
        if (flatData != null) { for (int i = 0; i < flatData.Length; i += 2) cards.Add(new Card((Suit)flatData[i], (Rank)flatData[i + 1])); }
        players[localPlayerIndex].HighlightValidCards(cards);
    }
    [ClientRpc] void ClearHighlightsClientRpc() { if (localPlayerIndex >= 0) players[localPlayerIndex].ClearHighlights(); }
    [ClientRpc] void SortHandsClientRpc() { foreach (var p in players) p.SortHand(); }
    [ClientRpc] void ShowAuctionUIClientRpc(int client, int bid, int winner) { if (localPlayerIndex == client) auctionUI.ShowHumanTurn(bid, winner); }
    [ClientRpc] void ShowSuitSelectUIClientRpc(int client) { if (localPlayerIndex == client) auctionUI.ShowSuitSelection(); }

    [ClientRpc]
    void ShowFinalBidUIClientRpc(int client, int min)
    {
        // THIS WAS EMPTY - RESTORED
        if (localPlayerIndex == client)
        {
            auctionUI.ShowFinalBidSelector(min);
        }
    }

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
    // 8. HELPERS
    // --------------------------------------------------------------------------------

    void InitializeDeck(int round)
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

        if (round == 1)
        {
            for (int i = 0; i < deck.Count; i++)
            {
                Card temp = deck[i];
                int rnd = Random.Range(i, deck.Count);
                deck[i] = deck[rnd];
                deck[rnd] = temp;
            }
        }
        else
        {
            for (int r = 0; r < 4; r++) PerformRiffleShuffle();
        }
    }

    void PerformRiffleShuffle()
    {
        int mid = deck.Count / 2;
        List<Card> left = deck.GetRange(0, mid);
        List<Card> right = deck.GetRange(mid, deck.Count - mid);
        deck.Clear();

        while (left.Count > 0 || right.Count > 0)
        {
            bool takeLeft;
            if (left.Count == 0) takeLeft = false;
            else if (right.Count == 0) takeLeft = true;
            else takeLeft = Random.value > 0.5f;

            if (takeLeft) { deck.Add(left[0]); left.RemoveAt(0); }
            else { deck.Add(right[0]); right.RemoveAt(0); }
        }
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

    void UpdateScoreUI()
    {
        for (int i = 0; i < 4; i++)
        {
            string statusText = $"{tricksWon[i]} / {finalBids[i]}";
            UpdateBidUIClientRpc(i, statusText);
        }
    }

    void ResetRoundData()
    {
        tricksWon = new int[4];
        finalBids = new int[4];
        foreach (var p in players) p.ClearHandVisuals();
        
        // --- FIX: CLEAR BIDS ON CLIENTS TOO ---
        ClearBidsClientRpc();
    }

    void CalculateScores()
    {
        bool isKaatRound = finalBids.Contains(10);
        for (int i = 0; i < 4; i++)
        {
            int b = finalBids[i];
            int t = tricksWon[i];
            int roundScore = 0;

            if (isKaatRound)
            {
                if (b == 10) roundScore = (t >= 10) ? 10 : -10;
                else roundScore = t;
            }
            else
            {
                roundScore = (t >= b) ? b : -b;
            }
            playerScores[i] += roundScore;
        }
        UpdateScoreboardClientRpc(playerScores, netDealerIndex.Value);
    }

    // INPUT HOOKS
    public void OnHumanAuctionAction(int b) { SubmitBidServerRpc(b, localPlayerIndex); }
    public void OnHumanSuitSelected(Suit s) { SelectTrumpServerRpc(s); }
    public void OnHumanFinalBidSubmitted(int b) { SubmitBidServerRpc(b, localPlayerIndex); }

    public void OnHumanLateTrumpChange(Suit newSuit)
    {
        RequestLateTrumpChangeServerRpc(localPlayerIndex, newSuit);
    }

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
        StopAllCoroutines();
        base.OnNetworkDespawn();
    }

    private void ForceKaatBids(int tenBidderIndex)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i != tenBidderIndex)
            {
                finalBids[i] = 2;
                UpdateBidUIClientRpc(i, "2");
            }
        }
    }
}