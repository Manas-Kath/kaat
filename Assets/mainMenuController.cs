using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // Required if you want to manipulate buttons via code

public class MainMenuController : MonoBehaviour
{
    [Header("Scene Config")]
    [SerializeField] private string gameSceneName = "GameScene";

    // Call this from the "Play vs AI" Button
    public void OnPlayAIButton()
    {
        StartGame(GameMode.SinglePlayerAI);
    }

    // Call this from the "LAN" Button
    public void OnPlayLANButton()
    {
        StartGame(GameMode.LocalLAN);
    }

    // Call this from the "Online" Button
    public void OnPlayOnlineButton()
    {
        StartGame(GameMode.OnlineRelay);
    }

    private void StartGame(GameMode mode)
    {
        GameSettings.CurrentMode = mode;
        Debug.Log($"Mode selected: {mode}. Loading Game...");
        SceneManager.LoadScene(gameSceneName);
    }
}