using UnityEngine;
using UnityEngine.EventSystems;

public class CardInteraction : MonoBehaviour, IPointerClickHandler
{
    // 0=Bottom (Human), 1=Left, 2=Top, 3=Right
    public int ownerIndex; 
    private GameController gameController;

    void Start()
    {
        gameController = FindFirstObjectByType<GameController>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (gameController != null)
        {
            // Pass 'this' script so the controller can read the ownerIndex
            gameController.OnCardClicked(this); 
        }
    }
}