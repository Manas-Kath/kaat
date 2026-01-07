using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using uVegas.Core.Cards;

public static class BotAI
{
    // --- EVALUATION CONSTANTS ---
    private const int POINT_ACE = 4;
    private const int POINT_KING = 3;
    private const int POINT_QUEEN = 2;
    private const int POINT_JACK = 1;

    // --- STRATEGY 1: AUCTION BID (PHASE A) ---
    // Returns a bid of 5 or higher if they want to change the suit.
    // Returns 0 if they want to Pass (keep Spades).
    public static int CalculateAuctionBid(List<Card> hand)
    {
        // 1. Group cards by suit to find long suits
        var suitGroups = hand.GroupBy(c => c.suit).ToList();
        
        // 2. Check each suit for "Change Potential"
        foreach (var group in suitGroups)
        {
            Suit s = group.Key;
            if (s == Suit.Spades) continue; // We don't bid to change to Spades (it's default)

            int count = group.Count();
            int highCardPoints = CountHighCardPoints(group.ToList());

            // LOGIC: If I have 6+ cards OR 5 cards with Ace & King -> BID 5!
            if (count >= 6 || (count == 5 && highCardPoints >= 7))
            {
                // We found a strong suit! Bid the minimum required (5) or more based on strength
                return Mathf.Min(count, 8); // Bid between 5 and 8
            }
        }

        return 0; // Pass (Happy with Spades or hand is too weak to lead)
    }

    // --- STRATEGY 2: FINAL BID (PHASE B) ---
    // How many tricks do I think I can win given the current Trump?
    public static int CalculateFinalBid(List<Card> hand, Suit trumpSuit)
    {
        float estimatedTricks = 0;

        // 1. High Card Strength
        foreach (var card in hand)
        {
            if (card.rank == Rank.Ace) estimatedTricks += 1.0f;
            else if (card.rank == Rank.King) estimatedTricks += 0.75f; // King is strong but can be cut
            else if (card.rank == Rank.Queen) estimatedTricks += 0.5f;
        }

        // 2. Trump Length Strength
        int trumpCount = hand.Count(c => c.suit == trumpSuit);
        // If I have a lot of trumps, I can win by cutting others
        if (trumpCount >= 4) estimatedTricks += (trumpCount - 3) * 0.8f;

        // 3. Short Suit Bonus (Ability to cut early)
        // If I have 0 or 1 card of a non-trump suit, I can use my trumps!
        var groups = hand.GroupBy(c => c.suit);
        foreach (var g in groups)
        {
            if (g.Key == trumpSuit) continue;
            if (g.Count() == 0) estimatedTricks += 1.0f; // Void = strong cutting power
            else if (g.Count() == 1) estimatedTricks += 0.5f; // Singleton
        }

        // Round to nearest whole number, clamp between 2 and 13
        int bid = Mathf.RoundToInt(estimatedTricks);
        return Mathf.Clamp(bid, 2, 13);
    }

    // --- STRATEGY 3: PLAYING A CARD (THE BRAIN) ---
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        // --- CASE 1: I AM LEADING (Table Empty) ---
        if (tableCards == null || tableCards.Count == 0)
        {
            // A. Pull Trumps? (If I have many trumps, play one to drain opponents)
            var myTrumps = hand.Where(c => c.suit == trumpSuit).OrderByDescending(c => c.rank).ToList();
            if (myTrumps.Count >= 5) return myTrumps[0]; // Play high trump

            // B. Play High Aces (Win guaranteed tricks early)
            var aces = hand.Where(c => c.rank == Rank.Ace && c.suit != trumpSuit).ToList();
            if (aces.Count > 0) return aces[0];

            // C. Otherwise, play lowest card of my longest suit (Safe lead)
            var bestSuitGroup = hand.GroupBy(c => c.suit).OrderByDescending(g => g.Count()).First();
            return bestSuitGroup.OrderBy(c => c.rank).First(); // Low card
        }

        // --- CASE 2: I AM FOLLOWING (Must follow Suit) ---
        Suit currentSuit = leadSuit.Value;
        var legalMoves = hand.Where(c => c.suit == currentSuit).ToList();

        // Find who is winning currently
        Card currentWinner = GetCurrentWinner(tableCards, currentSuit, trumpSuit);
        bool partnerIsWinning = false; // (Add logic later if you want 2v2)

        if (legalMoves.Count > 0)
        {
            // I MUST follow suit. Can I beat the current winner?
            var winningMoves = legalMoves.Where(c => BeatCard(c, currentWinner, currentSuit, trumpSuit)).OrderBy(c => c.rank).ToList();

            if (winningMoves.Count > 0)
            {
                // WIN CHEAP: Play the lowest card that still wins
                return winningMoves[0];
            }
            else
            {
                // CAN'T WIN: Dump lowest card (save high ones)
                return legalMoves.OrderBy(c => c.rank).First();
            }
        }

        // --- CASE 3: I CANNOT FOLLOW SUIT (Time to Cut or Discard) ---
        
        // A. Try to Cut with Trump
        var myTrumpsInHand = hand.Where(c => c.suit == trumpSuit).OrderBy(c => c.rank).ToList();
        
        // Only play trump if it actually beats the current winner (e.g. don't play 2 of Spades if King of Spades is on table)
        var winningTrumps = myTrumpsInHand.Where(c => BeatCard(c, currentWinner, currentSuit, trumpSuit)).ToList();

        if (winningTrumps.Count > 0)
        {
            // WIN CHEAP: Play lowest trump that wins
            return winningTrumps[0];
        }

        // B. Discard (Trash)
        // Throw away low cards of non-trump suits
        var trash = hand.Where(c => c.suit != trumpSuit).OrderBy(c => c.rank).ToList();
        if (trash.Count > 0) return trash[0];

        // If all I have is useless trumps, play lowest
        return hand.OrderBy(c => c.rank).First();
    }

    // --- HELPER METHODS ---

    private static int CountHighCardPoints(List<Card> cards)
    {
        int p = 0;
        foreach (var c in cards)
        {
            if (c.rank == Rank.Ace) p += POINT_ACE;
            if (c.rank == Rank.King) p += POINT_KING;
            if (c.rank == Rank.Queen) p += POINT_QUEEN;
            if (c.rank == Rank.Jack) p += POINT_JACK;
        }
        return p;
    }

    private static bool BeatCard(Card challenger, Card currentWinner, Suit leadSuit, Suit trumpSuit)
    {
        // 1. If winner is Trump
        if (currentWinner.suit == trumpSuit)
        {
            // Challenger must be Trump AND Higher Rank
            return (challenger.suit == trumpSuit && challenger.rank > currentWinner.rank);
        }

        // 2. If winner is NOT Trump
        // If challenger is Trump -> Auto Win
        if (challenger.suit == trumpSuit) return true;

        // If challenger is Lead Suit -> Must be Higher Rank
        if (challenger.suit == leadSuit)
        {
            return (challenger.rank > currentWinner.rank);
        }

        // Otherwise (off-suit non-trump) -> Lose
        return false;
    }

    private static Card GetCurrentWinner(List<Card> tableCards, Suit leadSuit, Suit trumpSuit)
    {
        Card winner = tableCards[0];
        for (int i = 1; i < tableCards.Count; i++)
        {
            if (BeatCard(tableCards[i], winner, leadSuit, trumpSuit))
            {
                winner = tableCards[i];
            }
        }
        return winner;
    }
}