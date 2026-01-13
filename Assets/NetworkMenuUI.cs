using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetworkMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField joinCodeInput;
    public Button hostButton;
    public Button joinButton;
    public TextMeshProUGUI statusText; // To show the Join Code to the host

    private async void Start()
    {
        // Initialize services as soon as the menu starts
        await MultiplayerSessionManager.Instance.InitializeServices();
        
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
    }

    private async void OnHostClicked()
    {
        statusText.text = "Creating Session...";
        string code = await MultiplayerSessionManager.Instance.StartHostAsync();
        
        if (!string.IsNullOrEmpty(code))
        {
            statusText.text = $"Session Code: {code}";
            // Copy code to clipboard for easy sharing
            GUIUtility.systemCopyBuffer = code; 
        }
        else
        {
            statusText.text = "Failed to create session.";
        }
    }

    private async void OnJoinClicked()
    {
        string code = joinCodeInput.text;
        if (string.IsNullOrEmpty(code))
        {
            statusText.text = "Please enter a code.";
            return;
        }

        statusText.text = "Joining...";
        await MultiplayerSessionManager.Instance.JoinByCodeAsync(code);
    }
}