using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Threading.Tasks;
using Unity.Collections; // Required for FixedString32Bytes

public class ConnectionManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField nameInputField; // <--- NEW: Drag Name Input here
    public TMP_InputField inputField;     // IP/Code Input
    public Button hostButton;
    public Button joinButton;
    public Button startMatchButton;
    public TextMeshProUGUI statusText;

    // Static variable to pass name to the Game Scene
    public static string LocalPlayerName = "Player";

    private bool isLobbyActive = false;
    private string activeJoinCode = "";

    private void Start()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        // Load saved name if available
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            nameInputField.text = PlayerPrefs.GetString("PlayerName");
        }

        SetupUIForMode();

        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        startMatchButton.onClick.AddListener(OnStartMatchClicked);
        startMatchButton.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (isLobbyActive && NetworkManager.Singleton.IsListening)
        {
            int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
            string message = "";
            if (!string.IsNullOrEmpty(activeJoinCode))
                message += $"<size=150%>Code: <color=yellow>{activeJoinCode}</color></size>\n\n";

            message += $"Lobby: {count}/4 Player(s) Joined.\n";

            if (NetworkManager.Singleton.IsServer)
            {
                if (count == 4)
                {
                    message += "<color=green>Lobby Full! Ready to Start.</color>";
                    statusText.text = message;
                    startMatchButton.gameObject.SetActive(true);
                }
                else
                {
                    message += "Waiting for full lobby...";
                    statusText.text = message;
                    startMatchButton.gameObject.SetActive(false);
                }
            }
            else
            {
                message += "Waiting for Host to start...";
                statusText.text = message;
            }
        }
    }

    private void SavePlayerName()
    {
        string n = nameInputField.text;
        if (string.IsNullOrEmpty(n)) n = "Guest";
        LocalPlayerName = n;
        PlayerPrefs.SetString("PlayerName", n);
    }

    private void SetupUIForMode()
    {
        activeJoinCode = "";
        if (GameSettings.CurrentMode == GameMode.LocalLAN)
        {
            statusText.text = "LAN Mode";
            inputField.placeholder.GetComponent<TextMeshProUGUI>().text = "Enter IP...";
        }
        else
        {
            statusText.text = "Online Mode";
            inputField.placeholder.GetComponent<TextMeshProUGUI>().text = "Enter Code...";
            _ = MultiplayerSessionManager.Instance.InitializeServices();
        }
    }

    private async void OnHostClicked()
    {
        SavePlayerName(); // Save name before starting
        statusText.text = "Starting Host...";
        bool success = false;

        if (GameSettings.CurrentMode == GameMode.LocalLAN)
        {
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", 7777);
            success = NetworkManager.Singleton.StartHost();
        }
        else
        {
            string code = await MultiplayerSessionManager.Instance.StartHostAsync();
            if (!string.IsNullOrEmpty(code))
            {
                activeJoinCode = code;
                GUIUtility.systemCopyBuffer = code;
                success = true;
            }
        }

        if (success) OnConnectionSuccess();
    }

    private async void OnJoinClicked()
    {
        SavePlayerName(); // Save name before joining
        statusText.text = "Joining...";
        string input = inputField.text;
        bool success = false;

        if (GameSettings.CurrentMode == GameMode.LocalLAN)
        {
            if (string.IsNullOrEmpty(input)) input = "127.0.0.1";
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(input, 7777);
            success = NetworkManager.Singleton.StartClient();
        }
        else
        {
            if (!string.IsNullOrEmpty(input))
            {
                await MultiplayerSessionManager.Instance.JoinByCodeAsync(input);
                success = true;
            }
        }

        if (success) OnConnectionSuccess();
    }

    private void OnConnectionSuccess()
    {
        isLobbyActive = true;
        hostButton.gameObject.SetActive(false);
        joinButton.gameObject.SetActive(false);
        inputField.gameObject.SetActive(false);
        nameInputField.gameObject.SetActive(false); // Hide name input too
    }

    private void OnStartMatchClicked()
    {
        if (GameController.Instance != null)
            GameController.Instance.StartMultiplayerMatch();
    }
}