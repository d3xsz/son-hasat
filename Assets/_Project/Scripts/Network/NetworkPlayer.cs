using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-10: Oyuncunun ağ davranışı. Konum ve yatay dönüş NetworkTransform ile (sahip yetkili) gider;
/// dikey bakış (pitch) burada NetworkVariable ile senkronlanır.
/// Sahibi olmayan kopyalarda girdi, kamera, ses ve CharacterController kapalı kalır.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Sadece sahibinde açılır")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;

    [Header("Bakış")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private float pitchSmoothing = 15f;

    [Header("Görünüm (placeholder gövde)")]
    [Tooltip("Başkalarına görünür, sahibinde gizlenir (birinci şahıs).")]
    [SerializeField] private Renderer[] bodyRenderers;
    // KULLANICI (2026-11-09): tüm oyuncular aynı görünür (renk farkı yok); kim olduğu isim etiketinden anlaşılır.

    private readonly NetworkVariable<float> pitch = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CharacterController controller;
    private float shownPitch;
    private bool applied;

    // oyuncu adı (lobide girilir; sahip yazar, herkes okur)
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> playerName = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>Herkes okuyabilir: oyuncunun görünen adı (boşsa "Oyuncu N").</summary>
    public string DisplayName
    {
        get
        {
            string n = playerName.Value.ToString();
            return string.IsNullOrWhiteSpace(n) ? "Oyuncu " + (OwnerClientId + 1) : n;
        }
    }

    /// <summary>G-35: uzak kopyada gösterilen dikey bakış açısı (derece, + = aşağı). Kafa/gövde pozu ve mızrak eğimi için.</summary>
    public float ShownPitch => shownPitch;
    private bool localOwner;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        // Ağ dışı test: NetworkManager çalışmıyorsa yerel oyuncu gibi davran.
        if (!applied && !IsSpawned && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            Apply(true);
    }

    public override void OnNetworkSpawn()
    {
        Apply(IsOwner);
        gameObject.name = "Player_" + OwnerClientId + (IsOwner ? " (sen)" : "");
        if (IsOwner)
        {
            string n = PlayerIdentity.LocalName;
            if (!string.IsNullOrWhiteSpace(n)) playerName.Value = new Unity.Collections.FixedString64Bytes(n);
        }
    }

    private void Apply(bool local)
    {
        applied = true;
        localOwner = local;
        if (ownerOnlyBehaviours != null)
            foreach (var b in ownerOnlyBehaviours) if (b != null) b.enabled = local;
        if (playerCamera != null) playerCamera.enabled = local;
        if (audioListener != null) audioListener.enabled = local;
        controller.enabled = local;
        if (bodyRenderers != null)
            foreach (var r in bodyRenderers) if (r != null) r.enabled = !local;
    }

    /// <summary>G-17: izleyici modunda bu oyuncunun gözünden bakılırken gövde/burun gizlenir (kameranın içinde görünmesin).</summary>
    public void SetSpectated(bool spectated)
    {
        if (localOwner || bodyRenderers == null) return;
        foreach (var r in bodyRenderers) if (r != null) r.enabled = !spectated;
    }

    /// <summary>B3-01: üçüncü şahıs düşüşte sahip kendi gövdesini görür; bitince yine gizlenir. PlayerFreefall çağırır.</summary>
    public void SetOwnerBodyVisible(bool visible)
    {
        if (!localOwner || bodyRenderers == null) return;
        foreach (var r in bodyRenderers) if (r != null) r.enabled = visible;
    }

    // ---------- Eğilme (KULLANICI, 2026-11-09) ----------
    private readonly NetworkVariable<bool> crouch = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool localCrouch;   // ağ dışı test için
    private float crouchBlend;
    private PlayerLife lifeRef;
    [SerializeField] private float crouchCameraDrop = 0.65f;

    /// <summary>Herkes okuyabilir: oyuncu eğiliyor mu (animasyon, adım sesi, gürültü).</summary>
    public bool IsCrouching => IsSpawned ? crouch.Value : localCrouch;
    /// <summary>0..1 yumuşatılmış eğilme (görsel).</summary>
    public float CrouchBlend => crouchBlend;

    /// <summary>Sahip: eğilme durumunu yazar (PlayerController).</summary>
    public void SetCrouch(bool value)
    {
        localCrouch = value;
        if (IsSpawned && IsOwner && crouch.Value != value) crouch.Value = value;
    }

    private void LateUpdate()
    {
        crouchBlend = Mathf.MoveTowards(crouchBlend, IsCrouching ? 1f : 0f, Time.deltaTime * 4f);
        if (lifeRef == null) lifeRef = GetComponent<PlayerLife>();
        if (lifeRef != null) lifeRef.CrouchDrop = Mathf.SmoothStep(0f, 1f, crouchBlend) * crouchCameraDrop;
    }

    private void Update()
    {
        if (!IsSpawned || cameraPivot == null) return;

        if (IsOwner)
        {
            float p = NormalizeAngle(cameraPivot.localEulerAngles.x);
            if (Mathf.Abs(p - pitch.Value) > 0.25f) pitch.Value = p;
        }
        else
        {
            float k = 1f - Mathf.Exp(-pitchSmoothing * Time.deltaTime);
            shownPitch = Mathf.Lerp(shownPitch, pitch.Value, k);
            cameraPivot.localRotation = Quaternion.Euler(shownPitch, 0f, 0f);
        }
    }

    private static float NormalizeAngle(float a)
    {
        return a > 180f ? a - 360f : a;
    }
}
