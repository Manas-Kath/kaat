using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using uVegas.Core.Cards;

public static class BotAI
{
    // --------------------------------------------------------------------------------
    // 1. RULE ENGINE (Validates Human & Bot Moves)
    // --------------------------------------------------------------------------------
    public static List<Card> GetLegalMoves(List<Card> hand, List<Card> table, Suit? leadSuit, Suit trumpSuit)
    {
        // 1. If leading (empty table), any card is valid
        if (leadSuit == null) return new List<Card>(hand);

        // 2. Check if we have cards of the Lead Suit
        var myLeadCards = hand.Where(c => c.suit == leadSuit).ToList();

        // Get current winner info for decision making
        Card currentWinner = GetCurrentWinner(table, leadSuit.Value, trumpSuit);
        bool isTrumped = (currentWinner != null && currentWinner.suit == trumpSuit);

        if (myLeadCards.Count > 0)
        {
            // --- SMART DISCARD RULE (The Fix) ---
            // If the trick is already trumped (by an opponent), and the Lead suit is NOT Trump,
            // we cannot win with a lead-suit card. Do not force a high card.
            if (isTrumped && leadSuit != trumpSuit)
            {
                return myLeadCards;
            }

            // Otherwise, we must try to beat the current highest card if possible.
            if (currentWinner != null)
            {
                // Find cards higher than the current winner
                var higherCards = myLeadCards.Where(c => c.rank > currentWinner.rank).ToList();

                // If we have higher cards, we MUST play one of them (Rule: Beat highest if possible)
                if (higherCards.Count > 0) return higherCards;
            }

            // If we can't beat it (or don't need to), we can play any card of the lead suit
            return myLeadCards;
        }

        // 3. If we are void (don't have lead suit):
        var myTrumpCards = hand.Where(c => c.suit == trumpSuit).ToList();

        if (myTrumpCards.Count > 0)
        {
            // --- VOID LOGIC FIX (KAAT v4.0) ---
            // Inside GetLegalMoves...
            if (isTrumped)
            {
                // 1. MUST KILL: Check if we have a trump HIGHER than the current winner (e.g., 10♠)
                var winningTrumps = myTrumpCards.Where(c => c.rank > currentWinner.rank).ToList();

                if (winningTrumps.Count > 0)
                {
                    // We have a higher trump. We are FORCED to play it.
                    return winningTrumps;
                }
                else
                {
                    // 2. FAANS: We cannot beat the 10♠. 
                    // We are allowed to save our small trumps and play "Faans" (trash) instead.
                    var nonTrumpCards = hand.Where(c => c.suit != trumpSuit).ToList();

                    if (nonTrumpCards.Count > 0)
                    {
                        return nonTrumpCards; // Play Faans
                    }

                    // 3. FORCED LOW TRUMP: We have nothing but trumps left. Must play one.
                    return myTrumpCards;
                }
            }
            else
            {
                // No one has trumped yet. We must play trump to cut (win).
                return myTrumpCards;
            }
        }

        // 4. Neither lead nor trump: Play anything (Discard)
        return new List<Card>(hand);
    }

    // --------------------------------------------------------------------------------
    // 2. BOT STRATEGY (Playing the actual card)
    // --------------------------------------------------------------------------------
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        // A. If we are leading (Table empty)
        if (leadSuit == null)
        {
            // Strategy: Play highest card to guarantee a win
            return hand.OrderByDescending(c => c.rank).First();
        }

        // B. Get Legal Moves
        List<Card> legalMoves = GetLegalMoves(hand, tableCards, leadSuit, trumpSuit);

        // Safety check
        if (legalMoves.Count == 0) return hand[0];

        Card currentWinner = GetCurrentWinner(tableCards, leadSuit.Value, trumpSuit);
        bool isTrumped = currentWinner != null && currentWinner.suit == trumpSuit;

        Card cardToPlay = legalMoves[0]; // Default fallback

        // --- SCENARIO 1: FOLLOWING SUIT ---
        if (legalMoves[0].suit == leadSuit)
        {
            if (isTrumped && leadSuit != trumpSuit)
            {
                // We can't win. Dump the LOWEST card.
                cardToPlay = legalMoves.OrderBy(c => c.rank).First();
            }
            else
            {
                var winningCards = legalMoves
                    .Where(c => currentWinner == null || c.rank > currentWinner.rank)
                    .OrderBy(c => c.rank)
                    .ToList();

                if (winningCards.Count > 0) cardToPlay = winningCards[0];
                else cardToPlay = legalMoves.OrderBy(c => c.rank).First();
            }
        }
        // --- SCENARIO 2: TRUMPING ---
        else if (legalMoves[0].suit == trumpSuit)
        {
            // If someone else already trumped...
            if (isTrumped)
            {
                // Try to over-trump
                var winningTrumps = legalMoves
                    .Where(c => c.rank > currentWinner.rank)
                    .OrderBy(c => c.rank)
                    .ToList();

                if (winningTrumps.Count > 0) cardToPlay = winningTrumps[0];
                else cardToPlay = legalMoves.OrderBy(c => c.rank).First(); // Forced play (only trumps in hand)
            }
            else
            {
                // Cheap win
                cardToPlay = legalMoves.OrderBy(c => c.rank).First();
            }
        }
        // --- SCENARIO 3: DISCARDING (FAANS) ---
        else
        {
            // Discard lowest value
            cardToPlay = legalMoves.OrderBy(c => c.rank).First();
        }

        return cardToPlay;
    }

    // --------------------------------------------------------------------------------
    // 3. AUCTION STRATEGIES (Bidding)
    // --------------------------------------------------------------------------------
    public static int CalculateAuctionBid(List<Card> hand)
    {
        int maxBid = 0;
        Suit[] candidates = { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };

        foreach (Suit potentialTrump in candidates)
        {
            float predictedTricks = EvaluateHandStrength(hand, potentialTrump);

            // Risk Logic: Bid aggressively if hand is strong
            if (predictedTricks >= 4.0f)
            {
                int riskBid = Mathf.FloorToInt(predictedTricks) + 1;
                riskBid = Mathf.Min(riskBid, 8); // Cap realistic auction bids
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
        // Add bonus for having many trumps (control)
        points += (trumpCount * 0.25f);

        // --- DEALER PEEK EDGE ---
        // If hand has 6 cards, this bot is the dealer (5 + 1 peek).
        // We add a "Confidence Bonus" because they know the bottom card.
        // Also, the 6th card's value is naturally added in the loop above.
        if (hand.Count > 5) points += 0.5f;

        return points;
    }

    // --------------------------------------------------------------------------------
    // 4. FINAL TRICK PREDICTION
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

        // 3. Void/Singleton Strength (ability to trump later)
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
            // 1. New card is Trump
            if (card.suit == trumpSuit)
            {
                // If previous winner was NOT trump, new card wins
                if (winner.suit != trumpSuit) winner = card;
                // If previous winner WAS trump, higher rank wins
                else if (card.rank > winner.rank) winner = card;
            }
            // 2. New card is Lead Suit (and winner is NOT Trump)
            else if (card.suit == leadSuit && winner.suit != trumpSuit)
            {
                if (card.rank > winner.rank) winner = card;
            }
        }
        return winner;
    }
}