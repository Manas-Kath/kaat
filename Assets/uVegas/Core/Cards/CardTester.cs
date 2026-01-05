using UnityEngine;
using uVegas.Core.Cards; // Needed to see Suit/Rank
using uVegas.UI;         // Needed to see UICard

public class CardTester : MonoBehaviour
{
    [Header("Setup")]
    public UICard cardPrefab;   // Drag your blank prefab here
    public CardTheme cardTheme; // Drag the Theme file (from Step 1) here
    public Transform spawnPoint; // Where the card should appear

    void Start()
    {
        // 1. Create the data for the card (e.g., Ace of Spades)
        Card myCardData = new Card(Suit.Spades, Rank.Ace);
    UICard newCardObject = Instantiate(cardPrefab, spawnPoint);

    // 2. RESET THE POSITION
    // This tells Unity: "I don't care where the prefab was saved, put it at (0,0) of the parent."
    newCardObject.transform.localPosition = Vector3.zero;
    
    // Optional: Reset scale just in case (sometimes it spawns massive or tiny)
    newCardObject.transform.localScale = Vector3.one;

    // 3. Initialize data
    newCardObject.Init(myCardData, cardTheme);
    }
}