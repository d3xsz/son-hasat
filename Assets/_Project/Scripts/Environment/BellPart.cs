using UnityEngine;

/// <summary>B3-05d: çan bulmacasının E ile kullanılan parçası: bir çan (index ≥ 0, "Çanı çal") ya da müzik kutusu (index &lt; 0, "Ezgiyi dinle").</summary>
public class BellPart : MonoBehaviour, IInteractable
{
    [SerializeField] private BellPuzzle puzzle;
    [SerializeField] private int index = -1;

    public string InteractionPrompt => index < 0 ? "Ezgiyi dinle" : "Çanı çal";
    public bool CanInteract(PlayerInteractor interactor) => puzzle != null && puzzle.CanUse;
    public void Interact(PlayerInteractor interactor)
    {
        if (puzzle == null) return;
        if (index < 0) puzzle.RequestMelody(); else puzzle.RequestRing(index);
    }
}
