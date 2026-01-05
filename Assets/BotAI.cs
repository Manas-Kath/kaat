using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class BotAI
{
    // --- STRATEGY 1: BIDDING ---
    public static int CalculateBid(List<Card> hand)
    {
        int strength = 0;
        // Simple Heuristic: Aces/Kings are strong. Long suits are strong.
        foreach (var c in hand)
        {
            if (c.rank == Rank.Ace) strength += 1;
            if (c.rank == Rank.King) strength += 1;
        }
        
        // Bonus for length (having 5+ cards of one suit)
        var groups = hand.GroupBy(c => c.suit);
        if (groups.Any(g => g.Count() >= 5)) strength += 1;

        // If hand is decent (strength >= 3), bid. Otherwise Pass (0).
        return strength >= 3 ? 16 + strength : 0; // Example: Minimum bid 16
    }

    // --- STRATEGY 2: PLAYING A CARD ---
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit currentSuit, Suit powerSuit)
    {
        // 1. LEADING (Table is empty)
        if (tableCards.Count == 0)
        {
            // Play highest card to try and win early
            return hand.OrderByDescending(c => c.rank).First();
        }

        // 2. FOLLOWING (Must follow suit)
        var legalMoves = hand.Where(c => c.suit == currentSuit).ToList();
        if (legalMoves.Count > 0)
        {
            // Try to win if we can
            Card highestOnTable = tableCards.Where(c => c.suit == currentSuit).OrderByDescending(c => c.rank).FirstOrDefault();
            Card myHighest = legalMoves.OrderByDescending(c => c.rank).First();

            if (highestOnTable == null || myHighest.rank > highestOnTable.rank)
                return myHighest; // Win it!
            
            return legalMoves.OrderBy(c => c.rank).First(); // Can't win, dump low card
        }

        // 3. CUTTING (No suit, try Power Suit)
        var trumps = hand.Where(c => c.suit == powerSuit).ToList();
        if (trumps.Count > 0)
        {
            return trumps.OrderBy(c => c.rank).First(); // Use smallest trump to win
        }

        // 4. DISCARDING (Trash logic)
        return hand.OrderBy(c => c.rank).First();
    }
}