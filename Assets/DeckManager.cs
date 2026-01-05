using System.Collections.Generic;
using UnityEngine;
using uVegas.Core.Cards; 

public class DeckManager : MonoBehaviour
{
    private List<Card> deck = new List<Card>();

    public void InitializeDeck()
    {
        deck.Clear();

        // 1. EXPLICITLY define the suits we want (Standard 4 only)
        // This prevents "Hidden", "Joker", or "None" suits from entering the deck
        Suit[] validSuits = { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };

        foreach (Suit s in validSuits)
        {
            // 2. Loop through Ranks, but SKIP the invalid ones
            foreach (Rank r in System.Enum.GetValues(typeof(Rank)))
            {
                // If the Rank is None, Joker, or Hidden, skip it!
                // (Checking .ToString() catches "Hidden" if it's not in the type check)
                if (r == Rank.None || r == Rank.Joker || r.ToString() == "Hidden") 
                    continue;

                deck.Add(new Card(s, r));
            }
        }
        
        Debug.Log("Deck Created. Total Cards: " + deck.Count); // Should say 52 exactly
        ShuffleDeck();
    }

    public void ShuffleDeck()
    {
        for (int i = 0; i < deck.Count; i++)
        {
            Card temp = deck[i];
            int randomIndex = Random.Range(i, deck.Count);
            deck[i] = deck[randomIndex];
            deck[randomIndex] = temp;
        }
    }

    public Card DrawCard()
    {
        if (deck.Count == 0) return null;
        Card cardToReturn = deck[0];
        deck.RemoveAt(0);
        return cardToReturn;
    }
}