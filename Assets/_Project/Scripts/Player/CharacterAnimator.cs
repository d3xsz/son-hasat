using UnityEngine;

/// <summary>
/// G-35: Goblin modelinin Idle/Walk/Run animasyonunu seçer. Oyuncunun yatay hızını konum değişiminden hesaplar
/// (her kopyada çalışır; uzak oyuncuların konumu zaten ağdan gelir, ağ gerekmez). Bayılmış/ölü oyuncuda hız 0.
/// driveFromMovement kapalıysa (ör. giriş sinematiğindeki goblinler) hep Idle oynar.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private bool driveFromMovement = true;
    [Tooltip("Hızı ölçülecek nesne (boşsa en üstteki ebeveyn: oyuncu kökü).")]
    [SerializeField] private Transform movementRoot;
    [SerializeField] private float smoothing = 8f;
    [Tooltip("Koşu animasyonunun doğal hızı (m/sn). Bundan hızlı giderken animasyon orantılı hızlanır (ayak kaymasın).")]
    [SerializeField] private float runClipSpeed = 3.7f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int AirborneId = Animator.StringToHash("Airborne");
    private static readonly int CrouchId = Animator.StringToHash("Crouch");
    private bool hasAirborne, hasCrouch;
    private NetworkPlayer netPlayer;
    private float airTime;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private Animator animator;
    private PlayerLife life;
    private Vector3 lastPos;
    private float speed;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        if (movementRoot == null) movementRoot = transform.root;
        life = movementRoot.GetComponent<PlayerLife>();
        netPlayer = movementRoot.GetComponent<NetworkPlayer>();
        lastPos = movementRoot.position;
    }

    private bool paramsChecked;

    /// <summary>Animator hazır olunca (Awake'te olmayabilir) "Airborne" parametresi var mı bakar.</summary>
    private void CheckParams()
    {
        if (paramsChecked || !animator.isInitialized) return;
        paramsChecked = true;
        foreach (var p in animator.parameters)
        {
            if (p.nameHash == AirborneId) hasAirborne = true;
            if (p.nameHash == CrouchId) hasCrouch = true;
        }
    }

    /// <summary>G-36+: zıplama: ayağın altında zemin var mı (her kopyada yerel ışın; kendi çarpıştırıcıları atlanır).</summary>
    private bool IsGrounded()
    {
        Vector3 origin = movementRoot.position + Vector3.up * 0.3f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 0.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!hits[i].collider.transform.IsChildOf(movementRoot)) return true;
        return false;
    }

    private void Update()
    {
        float target = 0f;
        if (driveFromMovement && Time.deltaTime > 0f)
        {
            Vector3 d = movementRoot.position - lastPos;
            d.y = 0f;
            target = d.magnitude / Time.deltaTime;
            if (target > 20f) target = 0f; // ışınlanma
            if (life != null && life.State != PlayerLifeState.Alive) target = 0f;
        }
        lastPos = movementRoot.position;
        speed = Mathf.Lerp(speed, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        animator.SetFloat(SpeedId, speed);
        CheckParams();
        if (hasAirborne && driveFromMovement)
        {
            bool alive = life == null || life.State == PlayerLifeState.Alive;
            airTime = alive && !IsGrounded() ? airTime + Time.deltaTime : 0f;
            animator.SetBool(AirborneId, airTime > 0.08f); // kısa basamak/sarsıntıda tetiklenmesin
        }
        if (hasCrouch && driveFromMovement)
            animator.SetBool(CrouchId, netPlayer != null && netPlayer.IsCrouching && (life == null || life.State == PlayerLifeState.Alive));
        // blend eşikleri klip hızlarına eşit (Idle 0, Walk ~2.4, Run ~6.5); koşudan hızlıysa oynatma hızlanır
        animator.speed = speed > runClipSpeed ? speed / runClipSpeed : 1f;
    }
}
