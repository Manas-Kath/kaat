using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Source")]
    public AudioSource effectSource;

    [Header("Audio Clips")]
    public AudioClip clickSound;  // Drag 'click_01' here
    public AudioClip dealSound;   // Drag 'hover_01' or 'select_01' here (reused as a card "flick")
    public AudioClip winSound;    // Drag 'win_01' here

    void Awake()
    {
        // Singleton Pattern: Ensures only one AudioManager exists
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Optional: Keeps music playing between scenes
        }
        else
        {
            Destroy(gameObject);
        }

        // Auto-add AudioSource if missing
        if (effectSource == null)
        {
            effectSource = gameObject.AddComponent<AudioSource>();
        }
    }

    public void PlayClick()
    {
        if (clickSound != null) effectSource.PlayOneShot(clickSound);
    }

    // We allow passing a pitch to make dealing sound less repetitive
    public void PlayDeal()
    {
        if (dealSound != null)
        {
            effectSource.pitch = Random.Range(0.9f, 1.1f); // Slight variation
            effectSource.PlayOneShot(dealSound);
            effectSource.pitch = 1.0f; // Reset
        }
    }

    public void PlayWin()
    {
        if (winSound != null) effectSource.PlayOneShot(winSound);
    }
}