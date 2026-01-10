using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using uVegas.Core.Cards;

public static class BotAI
{
    private const int POINT_ACE = 4;
    private const int POINT_KING = 3;
    private const int POINT_QUEEN = 2;
    private const int POINT_JACK = 1;

    // --- STRATEGY 1: AUCTION BID (PHASE A) ---
    public static int CalculateAuctionBid(List<Card> hand)
    {
        var suitGroups = hand.GroupBy(c => c.suit).ToList();
        
        foreach (var group in suitGroups)
        {
            Suit s = group.Key;
            if (s == Suit.Spades) continue; // Don't change TO Spades

            int count = group.Count();
            int highCardPoints = CountHighCardPoints(group.ToList());

            // --- AGGRESSION UPDATE ---
            // OLD LOGIC: count >= 6 || (count == 5 && points >= 7)
            // NEW LOGIC: count >= 6 OR (count == 5 && points >= 4)
            // Basically, if they have 5 cards and at least an Ace (4) or King+Jack (4), they try it.
            if (count >= 6 || (count == 5 && highCardPoints >= 4))
            {
                return Mathf.Min(count, 8); 
            }
        }

        return 0; 
    }

    // --- STRATEGY 2: FINAL BID (PHASE B) ---
    public static int CalculateFinalBid(List<Card> hand, Suit trumpSuit)
    {
        float estimatedTricks = 0;

        foreach (var card in hand)
        {
            if (card.rank == Rank.Ace) estimatedTricks += 1.0f;
            else if (card.rank == Rank.King) estimatedTricks += 0.75f;
            else if (card.rank == Rank.Queen) estimatedTricks += 0.5f;
        }

        int trumpCount = hand.Count(c => c.suit == trumpSuit);
        // Bonus for length
        if (trumpCount >= 4) estimatedTricks += (trumpCount - 3) * 0.8f;

        var groups = hand.GroupBy(c => c.suit);
        foreach (var g in groups)
        {
            if (g.Key == trumpSuit) continue;
            if (g.Count() == 0) estimatedTricks += 1.0f; 
            else if (g.Count() == 1) estimatedTricks += 0.5f; 
        }

        int bid = Mathf.RoundToInt(estimatedTricks);
        return Mathf.Clamp(bid, 2, 13);
    }

    // --- STRATEGY 3: PLAYING A CARD ---
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        // 1. LEAD
        if (tableCards == null || tableCards.Count == 0)
        {
            var myTrumps = hand.Where(c => c.suit == trumpSuit).OrderByDescending(c => c.rank).ToList();
            if (myTrumps.Count >= 5) return myTrumps[0];

            var aces = hand.Where(c => c.rank == Rank.Ace && c.suit != trumpSuit).ToList();
            if (aces.Count > 0) return aces[0];

            var bestSuitGroup = hand.GroupBy(c => c.suit).OrderByDescending(g => g.Count()).First();
            return bestSuitGroup.OrderBy(c => c.rank).First(); 
        }

        // 2. FOLLOW
        Suit currentSuit = leadSuit.Value;
        var legalMoves = hand.Where(c => c.suit == currentSuit).ToList();
        Card currentWinner = GetCurrentWinner(tableCards, currentSuit, trumpSuit);

        if (legalMoves.Count > 0)
        {
            var winningMoves = legalMoves.Where(c => BeatCard(c, currentWinner, currentSuit, trumpSuit)).OrderBy(c => c.rank).ToList();
            if (winningMoves.Count > 0) return winningMoves[0]; 
            else return legalMoves.OrderBy(c => c.rank).First(); // Dump low
        }

        // 3. CUT (Or Discard)
        // KAAT RULE: If I can't follow, I MUST Cut if I have a Spade
        var myTrumpsInHand = hand.Where(c => c.suit == trumpSuit).OrderBy(c => c.rank).ToList();
        
        if (myTrumpsInHand.Count > 0)
        {
            // Only play trump if it beats the current winner (or if winner isn't trump yet)
            var winningTrumps = myTrumpsInHand.Where(c => BeatCard(c, currentWinner, currentSuit, trumpSuit)).ToList();
            
            // If I can win with a trump, play the lowest winner
            if (winningTrumps.Count > 0) return winningTrumps[0];
            
            // If I can't win even with trump (e.g. they played Ace of Spades), 
            // standard Kaat strategy usually says "Save your trump" or "Play low trump".
            // For now, let's just discard trash instead of wasting a losing trump.
        }

        var trash = hand.Where(c => c.suit != trumpSuit).OrderBy(c => c.rank).ToList();
        if (trash.Count > 0) return trash[0];

        return hand.OrderBy(c => c.rank).First();
    }

    // --- HELPERS ---
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
        if (currentWinner.suit == trumpSuit)
        {
            return (challenger.suit == trumpSuit && challenger.rank > currentWinner.rank);
        }

        if (challenger.suit == trumpSuit) return true;

        if (challenger.suit == leadSuit)
        {
            return (challenger.rank > currentWinner.rank);
        }

        return false;
    }

    private static Card GetCurrentWinner(List<Card> tableCards, Suit leadSuit, Suit trumpSuit)
    {
        Card winner = tableCards[0];
        for (int i = 1; i < tableCards.Count; i++)
        {
            if (BeatCard(tableCards[i], winner, leadSuit, trumpSuit)) winner = tableCards[i];
        }
        return winner;
    }
}