using UnityEngine;
using UnityEngine.UI;
using TMPro;
using uVegas.Core.Cards;

public class AuctionUI : MonoBehaviour
{
    [Header("Phase A: Power Auction")]
    public GameObject auctionPanel;
    public TextMeshProUGUI infoText;
    public Button bidButton;
    public TextMeshProUGUI bidButtonLabel; 
    public Button passButton;

    [Header("Phase A: Suit Selection")]
    public GameObject suitSelectPanel;
    public Button btnSpades, btnHearts, btnClubs, btnDiamonds;

    [Header("Phase B: Final Prediction")]
    public GameObject finalBidPanel;
    public TextMeshProUGUI finalBidValueText; // Shows "4"
    public Button btnIncrease, btnDecrease, btnConfirm;

    private GameController gameController;
    private int nextRequiredBid; // For Phase A
    private int currentFinalPrediction = 2; // For Phase B
    private int minAllowedPrediction = 2;

    void Start()
    {
        gameController = FindFirstObjectByType<GameController>();
        
        // --- LISTENERS ---
        bidButton.onClick.AddListener(OnBidClicked);
        passButton.onClick.AddListener(OnPassClicked);

        btnSpades.onClick.AddListener(() => OnSuitSelected(Suit.Spades));
        btnHearts.onClick.AddListener(() => OnSuitSelected(Suit.Hearts));
        btnClubs.onClick.AddListener(() => OnSuitSelected(Suit.Clubs));
        btnDiamonds.onClick.AddListener(() => OnSuitSelected(Suit.Diamonds));

        // Final Bid Listeners
        btnIncrease.onClick.AddListener(() => AdjustFinalBid(1));
        btnDecrease.onClick.AddListener(() => AdjustFinalBid(-1));
        btnConfirm.onClick.AddListener(OnConfirmFinalBid);

        HideAll();
    }

    // --- PHASE A METHODS ---
    public void ShowHumanTurn(int currentHighestBid, int playerWhoBidLast)
    {
        auctionPanel.SetActive(true);
        nextRequiredBid = (currentHighestBid < 5) ? 5 : currentHighestBid + 1;
        
        string status = (playerWhoBidLast == -1) ? "Auction Start" : $"Player {playerWhoBidLast} bid {currentHighestBid}";
        infoText.text = $"{status}. Min to change suit is 5.";
        bidButtonLabel.text = $"Bid {nextRequiredBid}";
    }

    public void ShowSuitSelection() => suitSelectPanel.SetActive(true);

    // --- PHASE B METHODS (NEW) ---
    public void ShowFinalBidSelector(int minBid)
    {
        finalBidPanel.SetActive(true);
        minAllowedPrediction = minBid;
        currentFinalPrediction = minBid; // Start at the minimum required
        UpdateFinalBidText();
    }

    void AdjustFinalBid(int change)
    {
        currentFinalPrediction += change;
        // Clamp between Min and 13
        if (currentFinalPrediction < minAllowedPrediction) currentFinalPrediction = minAllowedPrediction;
        if (currentFinalPrediction > 13) currentFinalPrediction = 13;
        UpdateFinalBidText();
    }

    void UpdateFinalBidText()
    {
        finalBidValueText.text = currentFinalPrediction.ToString();
    }

    void OnConfirmFinalBid()
    {
        finalBidPanel.SetActive(false);
        gameController.OnHumanFinalBidSubmitted(currentFinalPrediction);
    }

    // --- SHARED ---
    public void HideAll()
    {
        if(auctionPanel) auctionPanel.SetActive(false);
        if(suitSelectPanel) suitSelectPanel.SetActive(false);
        if(finalBidPanel) finalBidPanel.SetActive(false);
    }

    // --- INTERNAL CLICKS ---
    void OnBidClicked() { auctionPanel.SetActive(false); gameController.OnHumanAuctionAction(nextRequiredBid); }
    void OnPassClicked() { auctionPanel.SetActive(false); gameController.OnHumanAuctionAction(0); }
    void OnSuitSelected(Suit s) { suitSelectPanel.SetActive(false); gameController.OnHumanSuitSelected(s); }
}