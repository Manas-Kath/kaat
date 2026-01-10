using Unity.Netcode;
using Unity.Netcode.Transports.UTP; // Required for UnityTransport
using UnityEngine;
using UnityEngine.UI;
using TMPro; // Required for the Input Field

public class NetworkUI : MonoBehaviour
{
    [Header("References")]
    public Button hostButton;
    public Button joinButton;
    public TMP_InputField ipInput; // <--- THIS is the slot for your new object
    public GameObject menuPanel;

    void Start()
    {
        // HOST BUTTON LOGIC
        hostButton.onClick.AddListener(() => 
        {
            NetworkManager.Singleton.StartHost();
            HideMenu();
        });

        // JOIN BUTTON LOGIC
        joinButton.onClick.AddListener(() => 
        {
            // 1. Get the IP text the user typed
            string ipAddress = ipInput.text;

            // 2. If empty, default to localhost (playing on same PC)
            if (string.IsNullOrEmpty(ipAddress)) 
            {
                ipAddress = "127.0.0.1";
            }

            // 3. Tell the Network Manager where to connect
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.ConnectionData.Address = ipAddress;
            
            // 4. Connect
            NetworkManager.Singleton.StartClient();
            HideMenu();
        });
    }

    void HideMenu()
    {
        menuPanel.SetActive(false);
    }
}
//