using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-03 (KULLANICI 2026-11-14): Boyut ışığıyla büyüyüp küçülen rüya nesnesi. Herhangi bir oyuncunun feneri
/// "büyüten" moddayken tutulursa büyür, "küçülten" moddayken küçülür; ışık çekilince O BOYUTTA KALIR (kalıcı).
/// Boyutu SUNUCU hesaplar (fener durumu, modu ve bakış ağda senkron), NetworkVariable ile herkese gider;
/// ölçek, parıltı ve ses yereldir. Sahne içi NetworkObject (Bölüm 3 sahnesi NGO ile yüklenir).
/// Bulmacalar <see cref="Size01"/>, <see cref="IsBig"/>, <see cref="IsSmall"/> değerlerini okur.
/// </summary>
public class ResizableObject : NetworkBehaviour
{
    public static readonly List<ResizableObject> All = new List<ResizableObject>();

    [Header("Boyut")]
    [Tooltip("En küçük ve en büyük ölçek (başlangıç localScale'in çarpanı).")]
    [SerializeField] private float minScale = 0.35f;
    [SerializeField] private float maxScale = 2.2f;
    [Tooltip("Başlangıç boyutu: 0 = en küçük, 1 = en büyük.")]
    [SerializeField, Range(0f, 1f)] private float startSize = 0f;
    [Tooltip("En küçükten en büyüğe geçiş süresi (sn).")]
    [SerializeField] private float changeSeconds = 2.5f;

    [Header("Işık algısı")]
    [SerializeField] private float lightRange = 16f;
    [SerializeField] private float lightHalfAngle = 20f;
    [Tooltip("Işığın hedef aldığı nokta (yerel, ölçeklenmemiş metre; nesnenin gövdesi).")]
    [SerializeField] private Vector3 aimOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Görsel")]
    [SerializeField] private Renderer[] glowRenderers = new Renderer[0];
    [SerializeField, ColorUsage(false, true)] private Color idleGlow = new Color(0.5f, 0.45f, 0.75f);
    [SerializeField] private float idleGlowStrength = 0.35f;
    [SerializeField] private float activeGlowStrength = 2.4f;

    private readonly NetworkVariable<float> size = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<bool> locked = new NetworkVariable<bool>(false);

    /// <summary>Kilitli: bulmacası çözüldü, boyut ışığı artık etkilemez (soluk parıltısı da söner).</summary>
    public bool IsLocked => IsSpawned && locked.Value;

    /// <summary>Sunucu: nesneyi verilen boyda sabitler (bağlı bulmaca çözülünce çağrılır; sonradan bozulamasın).</summary>
    public void ServerLock(float size01)
    {
        if (!IsServer || !IsSpawned) return;
        size.Value = Mathf.Clamp01(size01);
        locked.Value = true;
    }

    private Vector3 baseScale;
    private float shown;
    private float activity;      // 0..1 değişiyor parıltısı
    private int direction;       // yerel görsel: +1 büyüyor, -1 küçülüyor
    private float lastTarget;
    private float nextTick;
    private MaterialPropertyBlock block;
    private float phase;

    /// <summary>0 = en küçük, 1 = en büyük (herkes okuyabilir).</summary>
    public float Size01 => IsSpawned ? size.Value : startSize;
    public bool IsBig => Size01 >= 0.98f;
    public bool IsSmall => Size01 <= 0.02f;

    private void Awake()
    {
        baseScale = transform.localScale;
        if (glowRenderers == null || glowRenderers.Length == 0) glowRenderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
        shown = lastTarget = startSize;
        ApplyScale(shown);
    }

    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public override void OnNetworkSpawn()
    {
        if (IsServer) size.Value = startSize;
        shown = lastTarget = size.Value;
        ApplyScale(shown);
    }

    private Vector3 AimPoint()
    {
        // hedef nokta nesneyle birlikte büyür
        float k = Mathf.Lerp(minScale, maxScale, Mathf.SmoothStep(0f, 1f, shown));
        return transform.position + transform.rotation * Vector3.Scale(aimOffset, baseScale) * k;
    }

    private void Update()
    {
        if (IsServer && IsSpawned && !locked.Value)
        {
            int dir = FlashlightReveal.SizeDirection(AimPoint(), lightRange, lightHalfAngle, transform);
            if (dir != 0)
            {
                float s = Mathf.Clamp01(size.Value + dir * Time.deltaTime / Mathf.Max(0.1f, changeSeconds));
                if (!Mathf.Approximately(s, size.Value)) size.Value = s;
            }
        }

        float target = Size01;
        bool changing = Mathf.Abs(target - lastTarget) > 0.0005f;
        if (changing) direction = target > lastTarget ? 1 : -1;
        lastTarget = target;
        shown = Mathf.MoveTowards(shown, target, Time.deltaTime * 1.2f);
        ApplyScale(shown);

        activity = Mathf.MoveTowards(activity, changing ? 1f : 0f, Time.deltaTime * (changing ? 6f : 2f));
        Color tint = direction >= 0 ? FlashlightController.GrowColor : FlashlightController.ShrinkColor;
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 2f + phase);
        Color e = Color.Lerp(idleGlow * (IsLocked ? 0f : idleGlowStrength * pulse), tint * activeGlowStrength, activity);
        foreach (Renderer r in glowRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(block);
        }

        // değişirken ince, boyuta göre tizleşen tınlama; uca varınca bitiş sesi
        if (changing && Time.time >= nextTick && Sfx.Lib != null)
        {
            nextTick = Time.time + 0.22f;
            Sfx.At(DreamSfx.Chime, AimPoint(), 0.22f, Mathf.Lerp(1.8f, 0.8f, target), 0.02f, 2f, 22f);
        }
    }

    private void ApplyScale(float s01)
    {
        transform.localScale = baseScale * Mathf.Lerp(minScale, maxScale, Mathf.SmoothStep(0f, 1f, s01));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 1f, 0.6f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + transform.rotation * Vector3.Scale(aimOffset, transform.localScale), 0.2f);
    }
}
