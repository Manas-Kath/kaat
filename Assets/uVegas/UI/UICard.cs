using UnityEngine;
using UnityEngine.UI;
using uVegas.Core.Cards;

namespace uVegas.UI
{
    public class UICard : MonoBehaviour
    {
        [SerializeField] private Image baseImage;
        [SerializeField] private Image rankImage; 
        [SerializeField] private Image suitImage; 
        [SerializeField] private Card currentCard; 

        private CardTheme currentTheme; 
        
        // Helper to check if card is visually hidden
        public bool IsFaceDown { get; private set; } = false;

        public Card Data => currentCard; 

        public void Init(Card card, CardTheme theme)
        {
            currentCard = card;
            currentTheme = theme;
            UpdateTheme(); 
        }

        // NEW: Call this to toggle Face Up / Face Down
        public void SetFaceDown(bool state)
        {
            IsFaceDown = state;
            UpdateTheme();
        }

        private void UpdateTheme()
        {
            if (currentTheme == null) return; // Safety check

            // PRIORITY 1: If it's Face Down or actually Hidden data -> Show Back
            if (IsFaceDown || currentCard.suit == Suit.Hidden)
            {
                suitImage.gameObject.SetActive(false);
                rankImage.gameObject.SetActive(true); // Ensure rank image object is active to show the back pattern

                baseImage.sprite = currentTheme.baseImage;
                baseImage.color = currentTheme.frontColor;

                // Apply the "Back" sprite to the Rank Image slot (common trick) 
                // or Base Image depending on your prefab setup. 
                // Assuming standard setup where RankImage sits on top:
                rankImage.sprite = currentTheme.backImage;
                rankImage.color = currentTheme.backColor;
                
                // If your prefab uses baseImage for the back, swap the logic above.
                return;
            }

            // PRIORITY 2: Joker
            if (currentCard.suit == Suit.Joker)
            {
                suitImage.gameObject.SetActive(false);
                rankImage.gameObject.SetActive(true);

                baseImage.sprite = currentTheme.baseImage;
                baseImage.color = currentTheme.frontColor;

                rankImage.sprite = currentTheme.jokerImage;
                rankImage.color = currentTheme.jokerColor;
                return;
            }

            // PRIORITY 3: Normal Card
            suitImage.gameObject.SetActive(true);
            rankImage.gameObject.SetActive(true);

            baseImage.sprite = currentTheme.baseImage;
            baseImage.color = currentTheme.frontColor;

            RankEntry? rankEntry = currentTheme.GetRank(currentCard.rank);
            SuitEntry? suitEntry = currentTheme.GetSuit(currentCard.suit);

            if (rankEntry.HasValue)
            {
                rankImage.sprite = rankEntry.Value.image;
                rankImage.color = currentTheme.rankColor; 
            }

            if (suitEntry.HasValue)
            {
                suitImage.sprite = suitEntry.Value.image;
                suitImage.color = suitEntry.Value.color; 
            }
        }

        public void Reveal(Card card)
        {
            // If revealed, we update data and force face up
            currentCard = card;
            SetFaceDown(false);
        }
    }
}