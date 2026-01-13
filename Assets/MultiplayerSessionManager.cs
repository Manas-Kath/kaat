using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

public class MultiplayerSessionManager : MonoBehaviour
{
    public static MultiplayerSessionManager Instance { get; private set; }

    // Store the active session to leave it later
    public ISession ActiveSession { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task InitializeServices()
    {
        try
        {
            // 1. Initialize Unity Services
            await UnityServices.InitializeAsync();

            // 2. Authenticate the player (Anonymous for simplicity)
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"Signed in as: {AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Initialization Error: {e.Message}");
        }
    }

    public async Task<string> StartHostAsync(int maxPlayers = 4)
    {
        try
        {
            // Create Session Options
            // .WithRelayNetwork() tells it to use Relay (Internet) instead of Direct (LAN)
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = false,
                IsLocked = false
            }.WithRelayNetwork(); 

            // Create the session
            ActiveSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            
            Debug.Log($"Session Created! Join Code: {ActiveSession.Code}");

            // Note: The Multiplayer Service automatically configures the NetworkManager's transport.
            // You might still need to start the host if the service doesn't auto-hook (depends on version).
            // Usually, creating the session via this API sets up the connection, but for NGO:
            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartHost();
            }

            return ActiveSession.Code;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start host: {e.Message}");
            return null;
        }
    }

    public async Task JoinByCodeAsync(string sessionCode)
    {
        try
        {
            // Join the session using the code
            ActiveSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(sessionCode);
            Debug.Log($"Joined Session: {ActiveSession.Id}");

            // Start Client (NGO)
            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartClient();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join session: {e.Message}");
        }
    }

    private async void OnApplicationQuit()
    {
        if (ActiveSession != null)
        {
            await ActiveSession.LeaveAsync();
        }
    }
}