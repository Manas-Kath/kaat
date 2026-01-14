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

    // --------------------------------------------------------------------------------
    // 1. RULE ENGINE (Validates Human & Bot Moves)
    // --------------------------------------------------------------------------------
    public static List<Card> GetLegalMoves(List<Card> hand, List<Card> table, Suit? leadSuit, Suit trumpSuit)
    {
        // If leading (empty table), any card is valid
        if (leadSuit == null) return new List<Card>(hand);

        // 1. Check if we have cards of the Lead Suit
        var myLeadCards = hand.Where(c => c.suit == leadSuit).ToList();

        if (myLeadCards.Count > 0)
        {
            // Determine who is currently winning the table
            Card currentWinner = GetCurrentWinner(table, leadSuit.Value, trumpSuit);
            bool isTrumped = currentWinner != null && currentWinner.suit == trumpSuit;

            // --- THE FIX: SMART DISCARD RULE ---
            // If the trick is already trumped (by someone else), and the Lead suit is NOT Trump:
            // It is impossible for a lead-suit card to win. 
            // Therefore, you are not forced to play a high card. You can play ANY card of the lead suit.
            if (isTrumped && leadSuit != trumpSuit)
            {
                return myLeadCards;
            }

            // Otherwise (Trick is NOT trumped), Standard Rule applies:
            // "You must beat the current highest card if possible."
            if (currentWinner != null)
            {
                var higherCards = myLeadCards.Where(c => c.rank > currentWinner.rank).ToList();
                if (higherCards.Count > 0)
                {
                    return higherCards; // Force player to try to win
                }
            }

            // If we can't beat it, we must still follow suit (play any lead card)
            return myLeadCards;
        }

        // 2. If we are void (don't have lead suit):
        // Standard Rule: Must play Trump if available
        var myTrumpCards = hand.Where(c => c.suit == trumpSuit).ToList();
        if (myTrumpCards.Count > 0)
        {
            // Simple Rule: Just playing a trump is enough (you don't strictly have to over-trump, though bots usually try)
            return myTrumpCards;
        }

        // 3. Neither lead nor trump: Play anything (Discard)
        return new List<Card>(hand);
    }

    // --------------------------------------------------------------------------------
    // 2. BOT STRATEGY (Playing the actual card)
    // --------------------------------------------------------------------------------
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        // If we are leading (Table empty)
        if (leadSuit == null)
        {
            // Strategy: Play highest card to guarantee a win
            // (Improvements could be made here to lead "long" suits, but this is solid basic play)
            return hand.OrderByDescending(c => c.rank).First();
        }

        List<Card> legalMoves = GetLegalMoves(hand, tableCards, leadSuit, trumpSuit);
        Card currentWinner = GetCurrentWinner(tableCards, leadSuit.Value, trumpSuit);
        bool isTrumped = currentWinner != null && currentWinner.suit == trumpSuit;

        // --- STRATEGY 1: FOLLOWING SUIT ---
        if (legalMoves[0].suit == leadSuit)
        {
            // Scenario A: The trick is already Trumped
            if (isTrumped && leadSuit != trumpSuit)
            {
                // I can't win. Dump my LOWEST card to save high ones for later.
                return legalMoves.OrderBy(c => c.rank).First();
            }

            // Scenario B: Trick is NOT trumped. Can I win?
            // Find the smallest card that is still higher than the current winner.
            var winningCards = legalMoves.Where(c => c.rank > currentWinner.rank).OrderBy(c => c.rank).ToList();

            if (winningCards.Count > 0)
            {
                return winningCards[0]; // Win cheaply (e.g. play Queen on a 10, save Ace)
            }
            else
            {
                // I can't win. Dump lowest card.
                return legalMoves.OrderBy(c => c.rank).First();
            }
        }

        // --- STRATEGY 2: TRUMPING ---
        if (legalMoves[0].suit == trumpSuit)
        {
            // If someone else already trumped, try to beat them
            if (isTrumped)
            {
                var winningTrumps = legalMoves.Where(c => c.rank > currentWinner.rank).OrderBy(c => c.rank).ToList();
                if (winningTrumps.Count > 0) return winningTrumps[0];
            }
            
            // Otherwise play lowest available trump (save big trumps)
            return legalMoves.OrderBy(c => c.rank).First();
        }

        // --- STRATEGY 3: DISCARDING (Trash) ---
        // If playing off-suit (no lead, no trump), get rid of lowest garbage card
        return legalMoves.OrderBy(c => c.rank).First();
    }

    // --------------------------------------------------------------------------------
    // 3. AUCTION STRATEGIES (Bidding for Trump)
    // --------------------------------------------------------------------------------
    public static int CalculateAuctionBid(List<Card> hand)
    {
        int maxBid = 0;
        Suit[] candidates = { Suit.Clubs, Suit.Diamonds, Suit.Hearts };

        foreach (Suit potentialTrump in candidates)
        {
            float predictedTricks = EvaluateHandStrength(hand, potentialTrump);

            // Risk Rule: If strength is > 4, bid aggressively to take control
            if (predictedTricks >= 4.0f)
            {
                int riskBid = Mathf.FloorToInt(predictedTricks) + 1;
                riskBid = Mathf.Min(riskBid, 8); // Cap at 8

                if (riskBid > maxBid) maxBid = riskBid;
            }
        }
        return maxBid;
    }

    private static float EvaluateHandStrength(List<Card> hand, Suit trumpSuit)
    {
        float points = 0;
        int trumpCount = 0;

        foreach (var card in hand)
        {
            if (card.suit == trumpSuit)
            {
                trumpCount++;
                if (card.rank == Rank.Ace) points += 1.0f;
                else if (card.rank == Rank.King) points += 1.0f;
                else if (card.rank == Rank.Queen) points += 0.5f;
            }
            else
            {
                if (card.rank == Rank.Ace) points += 1.0f;
                else if (card.rank == Rank.King) points += 0.8f;
            }
        }
        // Bonus for Trump Length
        points += (trumpCount * 0.25f);
        return points;
    }

    // --------------------------------------------------------------------------------
    // 4. FINAL TRICK PREDICTION (Game Start)
    // --------------------------------------------------------------------------------
    public static int CalculateFinalBid(List<Card> hand, Suit trumpSuit)
    {
        float tricks = 0;

        // 1. High Card Strength
        foreach (var card in hand)
        {
            if (card.rank == Rank.Ace) tricks += 1.0f;
            else if (card.rank == Rank.King) tricks += 0.75f; 
            else if (card.rank == Rank.Queen) tricks += 0.25f;
        }

        // 2. Trump Length Strength 
        int trumpCount = hand.Count(c => c.suit == trumpSuit);
        if (trumpCount >= 4)
        {
            tricks += (trumpCount - 3) * 0.8f;
        }

        // 3. Void/Singleton Strength
        var groups = hand.GroupBy(c => c.suit);
        foreach (var g in groups)
        {
            if (g.Key == trumpSuit) continue;
            if (g.Count() == 0) tricks += 1.0f;      // Void
            else if (g.Count() == 1) tricks += 0.5f; // Singleton
        }

        return Mathf.Clamp(Mathf.RoundToInt(tricks), 2, 13);
    }

    // --------------------------------------------------------------------------------
    // 5. HELPER: DETERMINE WINNER
    // --------------------------------------------------------------------------------
    private static Card GetCurrentWinner(List<Card> tableCards, Suit leadSuit, Suit trumpSuit)
    {
        if (tableCards == null || tableCards.Count == 0) return null;

        Card winner = tableCards[0];

        foreach (var card in tableCards.Skip(1))
        {
            // 1. If new card is Trump...
            if (card.suit == trumpSuit)
            {
                // If winner was not trump, new card wins
                if (winner.suit != trumpSuit) winner = card;
                // If winner was also trump, higher rank wins
                else if (card.rank > winner.rank) winner = card;
            }
            // 2. If new card is Lead Suit (and winner is not Trump)...
            else if (card.suit == leadSuit && winner.suit != trumpSuit)
            {
                if (card.rank > winner.rank) winner = card;
            }
        }
        return winner;
    }

    private static bool BeatCard(Card challenger, Card currentWinner, Suit leadSuit, Suit trumpSuit)
    {
        // Simple helper to check if challenger > winner
        if (currentWinner.suit == trumpSuit)
            return (challenger.suit == trumpSuit && challenger.rank > currentWinner.rank);

        if (challenger.suit == trumpSuit) return true;

        if (currentWinner.suit == leadSuit)
            return (challenger.suit == leadSuit && challenger.rank > currentWinner.rank);

        return false;
    }
}