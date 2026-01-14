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

    [Header("Phase A & Late: Suit Selection")]
    public GameObject suitSelectPanel;
    public Button btnSpades, btnHearts, btnClubs, btnDiamonds;
    public TextMeshProUGUI suitPanelHeader; // Optional: To say "Select Trump" vs "Change Trump"

    [Header("Phase B: Final Prediction")]
    public GameObject finalBidPanel;
    public TextMeshProUGUI finalBidValueText; 
    public Button btnIncrease, btnDecrease, btnConfirm;
    
    [Header("KAAT 3.0 Special")]
    public Button btnLateTrumpChange; // DRAG YOUR "BID 10" BUTTON HERE

    private GameController gameController;
    private int nextRequiredBid; 
    private int currentFinalPrediction = 2; 
    private int minAllowedPrediction = 2;

    // Flag to distinguish between Auction-Win Suit Select vs Late-Game Trump Change
    private bool isLateTrumpChangeMode = false;

    void Start()
    {
        gameController = FindFirstObjectByType<GameController>();
        
        // --- LISTENERS ---
        bidButton.onClick.AddListener(OnBidClicked);
        passButton.onClick.AddListener(OnPassClicked);

        // Unified Suit Selection Listeners
        btnSpades.onClick.AddListener(() => OnSuitButton(Suit.Spades));
        btnHearts.onClick.AddListener(() => OnSuitButton(Suit.Hearts));
        btnClubs.onClick.AddListener(() => OnSuitButton(Suit.Clubs));
        btnDiamonds.onClick.AddListener(() => OnSuitButton(Suit.Diamonds));

        // Final Bid Listeners
        btnIncrease.onClick.AddListener(() => AdjustFinalBid(1));
        btnDecrease.onClick.AddListener(() => AdjustFinalBid(-1));
        btnConfirm.onClick.AddListener(OnConfirmFinalBid);
        
        // KAAT Special Listener
        if(btnLateTrumpChange) btnLateTrumpChange.onClick.AddListener(OnLateTrumpChangeClicked);

        HideAll();
    }

    // --- PHASE A: AUCTION ---
    public void ShowHumanTurn(int currentHighestBid, int playerWhoBidLast)
    {
        HideAll();
        auctionPanel.SetActive(true);
        nextRequiredBid = (currentHighestBid < 5) ? 5 : currentHighestBid + 1;
        
        string status = (playerWhoBidLast == -1) ? "Auction Start" : $"Player {playerWhoBidLast} bid {currentHighestBid}";
        infoText.text = $"{status}. Min to change suit is 5.";
        bidButtonLabel.text = $"Bid {nextRequiredBid}";
    }

    public void ShowSuitSelection() 
    {
        HideAll();
        isLateTrumpChangeMode = false; // Standard mode
        if(suitPanelHeader) suitPanelHeader.text = "Select Trump Suit";
        suitSelectPanel.SetActive(true);
    }

    // --- PHASE B: FINAL BIDS ---
    public void ShowFinalBidSelector(int minBid)
    {
        HideAll();
        finalBidPanel.SetActive(true);
        minAllowedPrediction = minBid;
        currentFinalPrediction = minBid; 
        
        // KAAT Rule: Can only change trump if you bid 10.
        // If minBid is already > 10 (rare), you can't reduce to 10.
        if (btnLateTrumpChange)
        {
            btnLateTrumpChange.gameObject.SetActive(minAllowedPrediction <= 10);
        }

        UpdateFinalBidText();
    }

    // --- INTERNAL LOGIC ---

    // 1. Click "Change Trump (Bid 10)"
    void OnLateTrumpChangeClicked()
    {
        finalBidPanel.SetActive(false); // Hide slider
        isLateTrumpChangeMode = true;   // Set flag
        if(suitPanelHeader) suitPanelHeader.text = "Select NEW Trump (Bidding 10)";
        suitSelectPanel.SetActive(true); // Show suits
    }

    // 2. Click a Suit
    void OnSuitButton(Suit s)
    {
        suitSelectPanel.SetActive(false);

        if (isLateTrumpChangeMode)
        {
            // KAAT 3.0: Late Change Logic
            gameController.OnHumanLateTrumpChange(s);
        }
        else
        {
            // Standard Auction Logic
            gameController.OnHumanSuitSelected(s);
        }
    }

    void AdjustFinalBid(int change)
    {
        currentFinalPrediction += change;
        if (currentFinalPrediction < minAllowedPrediction) currentFinalPrediction = minAllowedPrediction;
        if (currentFinalPrediction > 13) currentFinalPrediction = 13;
        UpdateFinalBidText();
    }

    void UpdateFinalBidText() => finalBidValueText.text = currentFinalPrediction.ToString();
    void OnConfirmFinalBid()
    {
        finalBidPanel.SetActive(false);
        gameController.OnHumanFinalBidSubmitted(currentFinalPrediction);
    }

    public void HideAll()
    {
        if(auctionPanel) auctionPanel.SetActive(false);
        if(suitSelectPanel) suitSelectPanel.SetActive(false);
        if(finalBidPanel) finalBidPanel.SetActive(false);
    }

    void OnBidClicked() { auctionPanel.SetActive(false); gameController.OnHumanAuctionAction(nextRequiredBid); }
    void OnPassClicked() { auctionPanel.SetActive(false); gameController.OnHumanAuctionAction(0); }
}