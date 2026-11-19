using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-04/G-17: Kameradan ileri raycast atar, bakılan IInteractable'ı bulur, E ile Interact çağırır.
/// IHoldInteractable ise E basılı tutulur, HoldDuration dolunca Interact çağrılır (HoldProgress01 HUD'a gider).
/// Bayılan/ölü oyuncu etkileşemez. Nişan ve ipucu yazısı HudController'da.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float interactDistance = 2.5f;
    [SerializeField] private LayerMask interactMask = ~0;

    private InputAction interactAction;
    private PlayerLife life;
    private IInteractable holdTarget;
    private float holdTimer;
    private bool holdConsumed;

    public IInteractable CurrentTarget { get; private set; }

    /// <summary>Basılı tutma ilerlemesi (0-1); basılı tutma yoksa 0.</summary>
    public float HoldProgress01 { get; private set; }

    private void Awake()
    {
        interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
        life = GetComponent<PlayerLife>();
    }

    private void OnEnable() { interactAction.Enable(); }

    private void OnDisable()
    {
        interactAction.Disable();
        CurrentTarget = null;
        ResetHold();
    }

    private void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked || (life != null && !life.CanAct) || NoteReader.IsOpen || NoteReader.ClosedFrame == Time.frameCount)
        {
            CurrentTarget = null;
            ResetHold();
            return;
        }

        FindTarget();
        if (CurrentTarget == null)
        {
            ResetHold();
            return;
        }

        if (CurrentTarget is IHoldInteractable hold && hold.HoldDuration > 0f)
        {
            if (!ReferenceEquals(holdTarget, CurrentTarget))
            {
                holdTarget = CurrentTarget;
                holdTimer = 0f;
                HoldProgress01 = 0f;
                holdConsumed = false;
            }

            if (interactAction.IsPressed() && !holdConsumed)
            {
                holdTimer += Time.deltaTime;
                HoldProgress01 = Mathf.Clamp01(holdTimer / hold.HoldDuration);
                if (holdTimer >= hold.HoldDuration)
                {
                    CurrentTarget.Interact(this);
                    holdConsumed = true;
                    holdTimer = 0f;
                    HoldProgress01 = 0f;
                }
            }
            else
            {
                holdTimer = 0f;
                HoldProgress01 = 0f;
                if (!interactAction.IsPressed()) holdConsumed = false;
            }
        }
        else
        {
            ResetHold();
            if (interactAction.WasPressedThisFrame()) CurrentTarget.Interact(this);
        }
    }

    private void ResetHold()
    {
        holdTarget = null;
        holdTimer = 0f;
        holdConsumed = false;
        HoldProgress01 = 0f;
    }

    private void FindTarget()
    {
        CurrentTarget = null;
        if (Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            IInteractable target = hit.collider.GetComponentInParent<IInteractable>();
            if (target != null && target.CanInteract(this)) CurrentTarget = target;
        }
    }
}
