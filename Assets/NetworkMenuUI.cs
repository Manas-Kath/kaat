using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkMenuUI : MonoBehaviour
{
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button joinBtn;
    [SerializeField] private Button startMatchBtn;
    
    // Reference to your GameController to start the actual round
    [SerializeField] private GameController gameController;

    void Start()
    {
        // 1. Host Button Click
        hostBtn.onClick.AddListener(() => 
        {
            NetworkManager.Singleton.StartHost();
            SetupHostUI();
        });

        // 2. Join Button Click
        joinBtn.onClick.AddListener(() => 
        {
            NetworkManager.Singleton.StartClient();
            SetupClientUI();
        });

        // 3. Start Match Button (Host Only)
        // Hidden by default, shown only after hosting
        startMatchBtn.gameObject.SetActive(false); 
        startMatchBtn.onClick.AddListener(() => 
        {
            gameController.StartGame();
            // Hide the connection panel entirely once game starts
            gameObject.SetActive(false);
        });
    }

    void SetupHostUI()
    {
        Debug.Log("Host Started. Waiting for players...");
        // Hide Host/Join buttons
        hostBtn.gameObject.SetActive(false);
        joinBtn.gameObject.SetActive(false);
        
        // Show the "Start Match" button so the Host can decide when to begin
        startMatchBtn.gameObject.SetActive(true);
    }

    void SetupClientUI()
    {
        Debug.Log("Client Started. Waiting for Host to start game...");
        // Hide everything and show a waiting text if you have one
        gameObject.SetActive(false); 
        // Or keep it active with a text saying "Waiting for Host..."
    }
}