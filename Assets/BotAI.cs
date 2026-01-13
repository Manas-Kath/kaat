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

    // --- STRICT RULE ENGINE ---
    public static List<Card> GetLegalMoves(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        // 1. LEAD: Anything goes
        if (leadSuit == null)
            return hand;

        Suit currentSuit = leadSuit.Value;
        var cardsOfLeadSuit = hand.Where(c => c.suit == currentSuit).ToList();

        // 2. HAS LEAD SUIT? -> MUST FOLLOW
        if (cardsOfLeadSuit.Count > 0)
        {
            var tableLeadCards = tableCards.Where(c => c.suit == currentSuit).ToList();

            // Try to beat the highest card of the lead suit on the table
            if (tableLeadCards.Count > 0)
            {
                int maxRankOnTable = tableLeadCards.Max(c => (int)c.rank);
                var winningMoves = cardsOfLeadSuit.Where(c => (int)c.rank > maxRankOnTable).ToList();

                // Rule: If you can win the suit, you MUST play the winner
                if (winningMoves.Count > 0)
                    return winningMoves;
            }

            // If you can't win, play any card of the lead suit
            return cardsOfLeadSuit;
        }

        // 3. VOID IN LEAD SUIT
        var trumps = hand.Where(c => c.suit == trumpSuit).ToList();

        // If I have no trumps, I can play anything
        if (trumps.Count == 0)
            return hand;

        // Check if the trick is currently being won by a Trump
        var tableTrumps = tableCards.Where(c => c.suit == trumpSuit).ToList();

        if (tableTrumps.Count > 0)
        {
            // SOMEONE ELSE ALREADY TRUMPED
            int maxTrumpRank = tableTrumps.Max(c => (int)c.rank);

            // Check if I can OVER-TRUMP (Beat the current highest trump)
            var winningTrumps = trumps.Where(c => (int)c.rank > maxTrumpRank).ToList();

            if (winningTrumps.Count > 0)
            {
                // Rule: Must try to win if possible
                return winningTrumps;
            }
            else
            {
                // USER RULE REQUEST: 
                // I have trumps, but they are all LOWER than the current winner.
                // I am NOT forced to waste a trump. I can play ANY card.
                return hand;
            }
        }
        else
        {
            // NO ONE HAS TRUMPED YET -> I MUST TRUMP
            return trumps;
        }
    }

    // --- STRATEGY: PLAYING A CARD ---
    public static Card ChooseCardToPlay(List<Card> hand, List<Card> tableCards, Suit? leadSuit, Suit trumpSuit)
    {
        if (leadSuit == null)
        {
            // Simple Lead Logic
            return hand.OrderByDescending(c => c.rank).First();
        }

        List<Card> legalMoves = GetLegalMoves(hand, tableCards, leadSuit, trumpSuit);

        Card currentWinner = GetCurrentWinner(tableCards, leadSuit.Value, trumpSuit);

        // Strategy: 
        // 1. Can I win? Pick lowest winning card.
        // 2. Can't win? Dump lowest garbage card.

        var winningMoves = legalMoves.Where(c => BeatCard(c, currentWinner, leadSuit.Value, trumpSuit)).OrderBy(c => c.rank).ToList();

        if (winningMoves.Count > 0)
            return winningMoves[0];

        // Dump lowest card
        return legalMoves.OrderBy(c => c.rank).First();
    }

    // --- AUCTION STRATEGIES ---

    // --- SMART AUCTION LOGIC ---
    public static int CalculateAuctionBid(List<Card> hand)
    {
        int maxBid = 0;

        // We evaluate Clubs, Diamonds, and Hearts as potential "Power Suits"
        // (Spades is the default, so we don't bid to change it to Spades)
        Suit[] candidates = { Suit.Clubs, Suit.Diamonds, Suit.Hearts };

        foreach (Suit potentialTrump in candidates)
        {
            // 1. Calculate how strong our hand becomes if THIS suit is Trump
            float predictedTricks = EvaluateHandStrength(hand, potentialTrump);

            // 2. Apply the "Risk" Rule
            // User Rule: "If I have 4 sure tricks, I can risk bidding 5 to change power."
            // Logic: If strength is 4.2, Bid = 5. If strength is 5.1, Bid = 6.
            if (predictedTricks >= 4.0f)
            {
                // Risk Strategy: Bid 1 higher than our sure count
                // (Because becoming the Power Suit gives us control)
                int riskBid = Mathf.FloorToInt(predictedTricks) + 1;

                // Cap bid at 8 for safety (unless you want aggressive bots)
                riskBid = Mathf.Min(riskBid, 8);

                if (riskBid > maxBid)
                    maxBid = riskBid;
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
                // Trump High Cards are extremely valuable
                if (card.rank == Rank.Ace) points += 1.0f;
                else if (card.rank == Rank.King) points += 1.0f; // King of Trump is solid
                else if (card.rank == Rank.Queen) points += 0.5f;
            }
            else
            {
                // Side Suit High Cards
                if (card.rank == Rank.Ace) points += 1.0f;       // Ace is almost definite
                else if (card.rank == Rank.King) points += 0.8f; // King is likely (0.8)
            }
        }

        // Bonus for Trump Length (The "Control" Factor)
        // Even small trumps (like a 5 of Hearts) can win if you cut someone else.
        // We add 0.25 points for every trump card we hold.
        points += (trumpCount * 0.25f);

        return points;
    }
    // --- FINAL TRICK PREDICTION ---
    public static int CalculateFinalBid(List<Card> hand, Suit trumpSuit)
    {
        float tricks = 0;

        // 1. High Card Strength
        foreach (var card in hand)
        {
            if (card.rank == Rank.Ace) tricks += 1.0f;
            else if (card.rank == Rank.King) tricks += 0.75f; // King is strong but not guaranteed
            else if (card.rank == Rank.Queen) tricks += 0.25f;
        }

        // 2. Trump Length Strength (Extra trumps usually win tricks)
        int trumpCount = hand.Count(c => c.suit == trumpSuit);
        if (trumpCount >= 4)
        {
            tricks += (trumpCount - 3) * 0.8f;
        }

        // 3. Void/Singleton Strength (Ability to cut other suits)
        var groups = hand.GroupBy(c => c.suit);
        foreach (var g in groups)
        {
            if (g.Key == trumpSuit) continue;

            // If we have 0 or 1 card of a side suit, we can likely ruff (trump) it later
            if (g.Count() == 0) tricks += 1.0f;
            else if (g.Count() == 1) tricks += 0.5f;
        }

        // Clamp between 2 and 13
        return Mathf.Clamp(Mathf.RoundToInt(tricks), 2, 13);
    }

    // --- HELPERS ---
    private static int CountHighCardPoints(List<Card> cards)
    {
        int p = 0;
        foreach (var c in cards)
        {
            if (c.rank >= Rank.Jack) p += (int)c.rank - 10;
        }
        return p;
    }

    private static bool BeatCard(Card challenger, Card currentWinner, Suit leadSuit, Suit trumpSuit)
    {
        if (currentWinner.suit == trumpSuit)
            return (challenger.suit == trumpSuit && challenger.rank > currentWinner.rank);

        if (challenger.suit == trumpSuit) return true;

        if (currentWinner.suit == leadSuit)
            return (challenger.suit == leadSuit && challenger.rank > currentWinner.rank);

        return false;
    }

    private static Card GetCurrentWinner(List<Card> tableCards, Suit leadSuit, Suit trumpSuit)
    {
        if (tableCards.Count == 0) return new Card(leadSuit, Rank.Two);

        Card winner = tableCards[0];
        for (int i = 1; i < tableCards.Count; i++)
        {
            if (BeatCard(tableCards[i], winner, leadSuit, trumpSuit))
                winner = tableCards[i];
        }
        return winner;
    }
}