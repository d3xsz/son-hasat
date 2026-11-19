using UnityEngine;

/// <summary>B3-05b: resimli şifrenin E ile çevrilen tekeri (durum SymbolPuzzle'da, ağda).</summary>
public class SymbolWheel : MonoBehaviour, IInteractable
{
    [SerializeField] private SymbolPuzzle puzzle;
    [SerializeField] private int index;

    public string InteractionPrompt => "Çevir";
    public bool CanInteract(PlayerInteractor interactor) => puzzle != null && puzzle.CanTurn;
    public void Interact(PlayerInteractor interactor) { if (puzzle != null) puzzle.RequestTurn(index); }
}
