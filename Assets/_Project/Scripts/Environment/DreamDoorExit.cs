using UnityEngine;

/// <summary>B3-05: ev içindeki çıkış kapısı. E ile bağlı evin dış kapısının önüne döndürür (ışınlamayı DreamHouseDoor yapar).</summary>
public class DreamDoorExit : MonoBehaviour, IInteractable
{
    [SerializeField] private DreamHouseDoor door;

    public string InteractionPrompt => "Dışarı çık";
    public bool CanInteract(PlayerInteractor interactor) => door != null && door.IsSpawned;
    public void Interact(PlayerInteractor interactor) { if (door != null) door.RequestTravel(false); }
}
