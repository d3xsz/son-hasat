using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-03: 1. şahıs hareket ve bakış (WASD, fare, Shift ile koşu).
/// Basit FPS kamera: cameraPivot yatay dönüşü gövdeden, dikey dönüşü kendinden alır.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Kamera")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float lookSensitivity = 0.1f;
    [SerializeField] private float maxPitch = 85f;

    [Header("Hareket")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 6.5f;
    [SerializeField] private float gravity = -20f;
    [Tooltip("Zıplama yüksekliği (m). Ağır eşya taşırken zıplanmaz.")]
    [SerializeField] private float jumpHeight = 1.1f;
    [Tooltip("Ağır eşya taşırken hız çarpanı (GDD: ~%40 düşer).")]
    [SerializeField, Range(0.1f, 1f)] private float heavySpeedMultiplier = 0.6f;

    [Header("Eğilme (C: aç/kapat, Sol Ctrl: basılı tut)")]
    [SerializeField] private float crouchSpeed = 2f;
    [SerializeField] private float crouchHeight = 1.15f;

    private CharacterController controller;
    private InventorySystem inventory;
    private PoisonSystem poison;
    private PlayerLife life;
    private InputActionMap map;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private float pitch;
    private float verticalVelocity;

    public bool IsSprinting { get; private set; }
    /// <summary>Yerel oyuncu eğiliyor mu (ağdaki durum NetworkPlayer.IsCrouching).</summary>
    public bool IsCrouching { get; private set; }
    private bool crouchToggle;
    private float standHeight;
    private Vector3 standCenter;
    private NetworkPlayer netPlayer;

    /// <summary>G-16: zehir evre 3 ekran titremesi (yaw/roll, derece). PoisonEffects yazar; eğim (x) etkilenmez, ağ senkronu bozulmaz.</summary>
    public Vector3 TremorEuler { get; set; }

    /// <summary>G-17: bayılma pozunda kameranın yana yatması (derece, roll). PlayerLife yazar.</summary>
    public float ExtraRoll { get; set; }

    /// <summary>G-29: kayma sekansında kamera sarsıntısı (pitch/yaw/roll eklemesi, derece). CollapseSequence yazar.</summary>
    public Vector3 ShakeEuler { get; set; }

    /// <summary>C-03: his efektleri (yürüme sallanması, iniş, mızrak/darbe tepmesi). PlayerFeel yazar.</summary>
    public Vector3 FeelEuler { get; set; }

    /// <summary>M-12: dış darbe ile geri savrulma (yatay hız, m/sn); hızla söner. Yalnızca sahip kopyada anlamlı.</summary>
    public void AddKnockback(Vector3 velocity)
    {
        velocity.y = 0f;
        knockback += velocity;
        if (velocity.sqrMagnitude > 0.01f && controller != null && controller.isGrounded) verticalVelocity = 2.2f; // hafif havalanma
    }
    private Vector3 knockback;

    public bool IsGrounded => controller != null && controller.isGrounded;
    public float VerticalVelocity => verticalVelocity;

    /// <summary>G-29: true iken yürüme/yerçekimi kapalı; konumu dış bir sistem (CollapseSequence) yönetir. Bakış çalışır.</summary>
    public bool ExternalMotion { get; set; }

    /// <summary>G-33: true iken yerel oyuncu hareket edemez ve bakamaz (giriş sinematiği). IntroCinematic yazar.</summary>
    public static bool InputLocked { get; set; }

    /// <summary>Bayılan/ölü oyuncu hareket edemez ve bakamaz.</summary>
    private bool CanAct => life == null || life.CanAct;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inventory = GetComponent<InventorySystem>();
        poison = GetComponent<PoisonSystem>();
        life = GetComponent<PlayerLife>();
        map = inputActions.FindActionMap("Player", true);
        moveAction = map.FindAction("Move", true);
        lookAction = map.FindAction("Look", true);
        sprintAction = map.FindAction("Sprint", true);
        jumpAction = map.FindAction("Jump", false);
        netPlayer = GetComponent<NetworkPlayer>();
        standHeight = controller.height;
        standCenter = controller.center;
    }

    // ---------- Eğilme ----------
    private void UpdateCrouch()
    {
        var kb = Keyboard.current;
        bool canAct = CanAct && !PauseMenu.IsOpen && !InputLocked && !NoteReader.IsOpen && !ExternalMotion;
        if (kb != null && canAct && kb.cKey.wasPressedThisFrame) crouchToggle = !crouchToggle;
        bool want = canAct && (crouchToggle || (kb != null && kb.leftCtrlKey.isPressed));
        if (!CanAct) { want = false; crouchToggle = false; }
        if (!want && IsCrouching && !HeadroomToStand()) want = true; // tavan altında ayağa kalkamaz
        if (want != IsCrouching)
        {
            IsCrouching = want;
            if (netPlayer != null) netPlayer.SetCrouch(want);
        }
        // çarpışma boyu yumuşakça değişir (ayak yerde kalsın: merkez de iner)
        float targetH = IsCrouching ? crouchHeight : standHeight;
        float h = Mathf.MoveTowards(controller.height, targetH, Time.deltaTime * 4f);
        if (!Mathf.Approximately(h, controller.height))
        {
            controller.height = h;
            controller.center = new Vector3(standCenter.x, standCenter.y - (standHeight - h) * 0.5f, standCenter.z);
        }
    }

    private bool HeadroomToStand()
    {
        float radius = controller.radius * 0.95f;
        Vector3 bottom = transform.position + Vector3.up * (controller.height - radius);
        float dist = standHeight - controller.height + 0.05f;
        return !Physics.SphereCast(bottom, radius, Vector3.up, out _, dist, ~0, QueryTriggerInteraction.Ignore);
    }

    private void OnEnable()
    {
        map.Enable();
        SetCursorLocked(true);
    }

    private void OnDisable()
    {
        map.Disable();
        SetCursorLocked(false);
    }

    private void Update()
    {
        bool paused = PauseMenu.IsOpen; // KULLANICI (cila): menü açıkken bakış/hareket girdisi yok (zaman durmaz)
        if (!paused) HandleCursor();
        UpdateCrouch();
        if (InputLocked || NoteReader.IsOpen) { IsSprinting = false; return; } // G-33: giriş sinematiği / not okunuyor
        if (!paused && Cursor.lockState == CursorLockMode.Locked) Look();
        if (ExternalMotion) { IsSprinting = false; verticalVelocity = 0f; return; }
        Move();
    }

    private void Look()
    {
        Vector2 delta = CanAct ? lookAction.ReadValue<Vector2>() * (lookSensitivity * GameSettings.Sensitivity) : Vector2.zero;
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch + ShakeEuler.x + FeelEuler.x, TremorEuler.y + ShakeEuler.y + FeelEuler.y, TremorEuler.z + ExtraRoll + ShakeEuler.z + FeelEuler.z);
    }

    private void Move()
    {
        Vector2 input = CanAct && !PauseMenu.IsOpen ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        bool heavy = inventory != null && inventory.IsCarryingHeavy;
        IsSprinting = !heavy && !IsCrouching && sprintAction.IsPressed() && input.y > 0.1f;
        float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
        if (heavy) speed *= heavySpeedMultiplier;
        if (poison != null) speed *= poison.SpeedMultiplier;

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f) * speed;

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        else verticalVelocity += gravity * Time.deltaTime;

        // zıplama (Space): yerdeyken, hareket edebiliyorken ve ağır eşya yokken
        if (controller.isGrounded && CanAct && !heavy && !PauseMenu.IsOpen && jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            // eğilirken Boşluk: önce ayağa kalkar (zıplamaz)
            if (IsCrouching) { if (HeadroomToStand()) crouchToggle = false; }
            else verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
        }
        move.y = verticalVelocity;
        move += knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 14f * Time.deltaTime);

        controller.Move(move * Time.deltaTime);
    }

    // Esc artık duraklatma menüsünü açar (PauseMenu). İmleç bir şekilde serbest kaldıysa sol tık tekrar kilitler.
    private void HandleCursor()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) SetCursorLocked(true);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
