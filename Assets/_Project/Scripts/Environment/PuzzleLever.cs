using UnityEngine;

/// <summary>
/// G-32 (taslak): İki kollu kapının kolu. E ile çekilir; durum PuzzleDoor'da (ağ) tutulur, kısa süre sonra kendiliğinden kalkar.
/// </summary>
public class PuzzleLever : MonoBehaviour, IInteractable
{
    [SerializeField] private Transform handle;
    [SerializeField] private float upAngle = -40f;
    [SerializeField] private float downAngle = 40f;
    [SerializeField] private float rotateSpeed = 360f;

    private PuzzleDoor door;
    private int index;
    private float angle;

    public void Bind(PuzzleDoor owner, int leverIndex)
    {
        door = owner;
        index = leverIndex;
    }

    public string InteractionPrompt => "Kolu çek";

    public bool CanInteract(PlayerInteractor interactor) => door != null && !door.IsOpen && !door.IsLeverDown(index);

    public void Interact(PlayerInteractor interactor) { if (door != null) door.RequestLever(index); }

    private void Start() { angle = upAngle; }

    private void Update()
    {
        if (handle == null) return;
        float target = door != null && (door.IsLeverDown(index) || door.IsOpen) ? downAngle : upAngle;
        angle = Mathf.MoveTowards(angle, target, rotateSpeed * Time.deltaTime);
        handle.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }
}
