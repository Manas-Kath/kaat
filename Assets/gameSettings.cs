public enum GameMode
{
    SinglePlayerAI, // 3 Bots, 1 Human
    LocalLAN,       // Netcode for GameObjects (LAN)
    OnlineRelay     // Unity Relay Service
}

public static class GameSettings
{
    // Default to AI so we don't crash if we test the GameScene directly
    public static GameMode CurrentMode = GameMode.SinglePlayerAI;
}