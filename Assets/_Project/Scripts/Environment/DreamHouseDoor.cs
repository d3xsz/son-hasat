using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-04 (KULLANICI 2026-11-14): Rüya köyünde açılabilen evin kapısı. Kapı, dışarıdaki bir boyut bulmacası çözülünce açılır:
/// bağlı <see cref="ResizableObject"/> istenen boyuta (tam büyük ya da tam küçük) gelince sunucu kapıyı KALICI olarak açar
/// (nesne sonradan yeniden değiştirilse de kapı açık kalır). Açılınca kanat döner, içerideki geçit parlar.
/// Evin içi (ayrı oda, bulmacalar, yataklar) B3-05'te bu kapıya bağlanacak. Sahne içi NetworkObject.
/// </summary>
public class DreamHouseDoor : NetworkBehaviour, IInteractable
{
    public static readonly List<DreamHouseDoor> All = new List<DreamHouseDoor>();

    [Header("Açılma koşulu")]
    [SerializeField] private ResizableObject condition;
    [Tooltip("true: nesne tam BÜYÜK olunca açılır. false: tam KÜÇÜK olunca açılır.")]
    [SerializeField] private bool requireBig = true;

    [Header("Görsel")]
    [SerializeField] private Transform leafPivot;
    [SerializeField] private float openAngle = -110f;
    [SerializeField] private Renderer[] portalRenderers = new Renderer[0];
    [SerializeField, ColorUsage(false, true)] private Color portalGlow = new Color(1.6f, 1.25f, 2.2f);

    [Header("Geçiş (B3-05)")]
    [Tooltip("Evin içindeki varış noktası (ayrı oda).")]
    [SerializeField] private Transform insidePoint;
    [Tooltip("Dışarı çıkınca varış noktası (kapının önü).")]
    [SerializeField] private Transform outsidePoint;
    [SerializeField] private float maxUseDistance = 6f;

    private readonly NetworkVariable<bool> open = new NetworkVariable<bool>(false);

    // ---------- E ile girme (dış kapı) ----------
    public string InteractionPrompt => "İçeri gir";
    public bool CanInteract(PlayerInteractor interactor) => IsOpen && insidePoint != null;
    public void Interact(PlayerInteractor interactor) { RequestTravel(true); }

    /// <summary>Yerel oyuncu: içeri (true) ya da dışarı (false) geçmek ister. Dışarı çıkışı DreamDoorExit çağırır.</summary>
    public void RequestTravel(bool inside) { if (IsSpawned) TravelRpc(inside); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TravelRpc(bool inside, RpcParams rpcParams = default)
    {
        if (!open.Value) return;
        Transform from = inside ? transform : insidePoint;
        Transform to = inside ? insidePoint : outsidePoint;
        if (to == null || from == null) return;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.OwnerClientId != rpcParams.Receive.SenderClientId || !p.CanAct) continue;
            if (Vector3.Distance(p.transform.position, from.position) > maxUseDistance) return;
            p.ServerTeleport(to.position, to.rotation);
            return;
        }
    }
    private float shown;
    private bool wasOpen;
    private Quaternion leafClosed;
    private MaterialPropertyBlock block;

    public bool IsOpen => IsSpawned && open.Value;

    private void Awake()
    {
        if (leafPivot != null) leafClosed = leafPivot.localRotation;
        block = new MaterialPropertyBlock();
        Apply(0f);
    }

    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public override void OnNetworkSpawn()
    {
        wasOpen = open.Value;
        shown = open.Value ? 1f : 0f;
        Apply(shown);
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && !open.Value && condition != null && (requireBig ? condition.IsBig : condition.IsSmall))
        {
            open.Value = true;
            condition.ServerLock(requireBig ? 1f : 0f);      // kapı açılınca boyut sabitlenir
        }

        if (open.Value && !wasOpen && Sfx.Lib != null)
        {
            // açılış: alçak gıcırtı + ince tınlama
            Sfx.At(DreamSfx.Chime, transform.position + Vector3.up, 0.9f, 0.45f, 0.03f, 3f, 30f);
            Sfx.At(DreamSfx.Chime, transform.position + Vector3.up, 0.5f, 1.9f, 0.03f, 3f, 30f);
        }
        wasOpen = open.Value;

        shown = Mathf.MoveTowards(shown, open.Value ? 1f : 0f, Time.deltaTime * 0.7f);
        Apply(shown);
    }

    private void Apply(float k)
    {
        float s = Mathf.SmoothStep(0f, 1f, k);
        if (leafPivot != null) leafPivot.localRotation = leafClosed * Quaternion.Euler(0f, openAngle * s, 0f);
        Color e = portalGlow * (s * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f)));
        foreach (Renderer r in portalRenderers)
        {
            if (r == null) continue;
            r.enabled = k > 0.01f;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(block);
        }
    }
}
