using UnityEngine;
using UnityEngine.EventSystems;

public class CardInteraction : MonoBehaviour, IPointerClickHandler
{
    private GameController gameController;
    void Start() { gameController = FindFirstObjectByType<GameController>(); }
    public void OnPointerClick(PointerEventData eventData) {
        if (gameController != null) gameController.OnCardClicked(this); 
    }
}