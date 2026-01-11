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
    public static int CalculateAuctionBid(List<Card> hand)
    {
        var suitGroups = hand.GroupBy(c => c.suit).ToList();
        foreach (var group in suitGroups) 
        {
            Suit s = group.Key; 
            if (s == Suit.Spades) continue; 
            int count = group.Count();
            int points = CountHighCardPoints(group.ToList());
            
            if (count >= 6 || (count == 5 && points >= 4)) 
                return Mathf.Min(count, 8); 
        }
        return 0; 
    }

    public static int CalculateFinalBid(List<Card> hand, Suit trumpSuit)
    {
        float tricks = 0;
        foreach (var card in hand) 
        {
            if (card.rank == Rank.Ace) tricks += 1.0f;
            else if (card.rank == Rank.King) tricks += 0.75f;
            else if (card.rank == Rank.Queen) tricks += 0.5f;
        }
        
        int trumpCount = hand.Count(c => c.suit == trumpSuit);
        if (trumpCount >= 4) tricks += (trumpCount - 3) * 0.8f;
        
        var groups = hand.GroupBy(c => c.suit);
        foreach (var g in groups) 
        {
            if (g.Key == trumpSuit) continue;
            if (g.Count() == 0) tricks += 1.0f; 
            else if (g.Count() == 1) tricks += 0.5f; 
        }
        return Mathf.Clamp(Mathf.RoundToInt(tricks), 2, 13);
    }

    // --- HELPERS ---
    private static int CountHighCardPoints(List<Card> cards) 
    {
        int p = 0; 
        foreach (var c in cards) 
        { 
            if(c.rank >= Rank.Jack) p += (int)c.rank - 10; 
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