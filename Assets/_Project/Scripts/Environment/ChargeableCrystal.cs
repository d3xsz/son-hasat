using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B2-02 (GDD 7.1): Fenerle şarj olan kristal kümesi. Herhangi bir oyuncunun açık feneri kümeye tutulunca birkaç saniyede
/// dolar; dolunca parlak ışık verir, bir süre tam kalır, sonra yavaşça söner. Şarjsızken çok soluk parlar (ipucu).
/// Şarjı SUNUCU hesaplar (fener durumu ve bakış ağda senkron), NetworkVariable ile herkese gider; görsel/ses yerel.
/// Ekranda yazı yok. Sahne içi NetworkObject (Bölüm 2 sahnesi NGO ile yüklenir).
/// </summary>
public class ChargeableCrystal : NetworkBehaviour
{
    [Header("Şarj")]
    [SerializeField] private float chargeSeconds = 2f;
    [Tooltip("Fener çekildikten sonra tam parlak kalma süresi (sn).")]
    [SerializeField] private float holdSeconds = 20f;
    [Tooltip("Tamamen sönme süresi (sn).")]
    [SerializeField] private float fadeSeconds = 15f;
    [SerializeField] private float lightRange = 16f;
    [SerializeField] private float lightHalfAngle = 24f;

    [Header("Görsel")]
    [SerializeField] private Renderer[] shards = new Renderer[0];
    [SerializeField] private Light glowLight;
    [SerializeField, ColorUsage(false, true)] private Color emission = new Color(0.18f, 0.75f, 0.9f);
    [SerializeField] private float dimEmission = 0.12f;
    [SerializeField] private float fullEmission = 2.6f;
    [SerializeField] private float dimLight = 0.25f;
    [SerializeField] private float fullLight = 3.6f;
    [SerializeField] private float dimRange = 4f;
    [SerializeField] private float fullRange = 12f;

    private readonly NetworkVariable<float> charge = new NetworkVariable<float>(0f);

    private float hold;
    private float shown;
    private float phase;
    private bool wasFull;
    private MaterialPropertyBlock block;

    /// <summary>0..1 şarj (herkes okuyabilir; B2-03/B2-05 kullanabilir).</summary>
    public float Charge => charge.Value;
    public bool IsCharged => charge.Value > 0.5f;

    private void Awake()
    {
        if (shards == null || shards.Length == 0) shards = GetComponentsInChildren<Renderer>();
        if (glowLight == null) glowLight = GetComponentInChildren<Light>();
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
        Apply(0f, false);
    }

    private Vector3 Center()
    {
        Vector3 c = transform.position + Vector3.up * 0.8f;
        return c;
    }

    private void Update()
    {
        if (IsServer && IsSpawned) ServerTick(Time.deltaTime);

        float target = IsSpawned ? charge.Value : 0f;
        shown = Mathf.MoveTowards(shown, target, Time.deltaTime * 1.5f);
        bool charging = IsSpawned && target > shown + 0.02f;
        Apply(shown, charging);

        bool full = target >= 0.99f;
        if (full && !wasFull && Sfx.Lib != null)
        {
            // dolunca ince kristal "tın"ı (damla sesini tiz çalarak)
            Sfx.At(Sfx.Lib.drips, Center(), 0.7f, 2.1f, 0.05f, 2f, 25f);
            Sfx.At(Sfx.Lib.drips, Center(), 0.4f, 2.8f, 0.05f, 2f, 25f);
        }
        if (full && !wasFull) Noise.Emit(Center(), Noise.CrystalChime); // B2-04: dolan kristal Dinleyen'i çekebilir
        wasFull = full;
    }

    private void ServerTick(float dt)
    {
        float c = charge.Value;
        if (FlashlightReveal.IsLit(Center(), lightRange, lightHalfAngle, transform))
        {
            c = Mathf.Min(1f, c + dt / Mathf.Max(0.1f, chargeSeconds));
            hold = holdSeconds;
        }
        else if (hold > 0f) hold -= dt;
        else c = Mathf.Max(0f, c - dt / Mathf.Max(0.1f, fadeSeconds));
        // ağ trafiği için küçük adımlarla yaz
        if (Mathf.Abs(c - charge.Value) >= 0.02f || (c == 0f && charge.Value != 0f) || (c == 1f && charge.Value != 1f)) charge.Value = c;
    }

    private void Apply(float k, bool charging)
    {
        float s = Mathf.SmoothStep(0f, 1f, k);
        float pulse = 1f + 0.07f * Mathf.Sin(Time.time * 2.2f + phase) + (charging ? 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 14f)) : 0f);
        Color e = emission * (Mathf.Lerp(dimEmission, fullEmission, s) * pulse);
        foreach (Renderer r in shards)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(block);
        }
        if (glowLight != null)
        {
            glowLight.intensity = Mathf.Lerp(dimLight, fullLight, s) * pulse;
            glowLight.range = Mathf.Lerp(dimRange, fullRange, s);
        }
    }
}
