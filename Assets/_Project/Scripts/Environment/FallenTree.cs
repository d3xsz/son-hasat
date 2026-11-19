using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-27: Düşen ağaç (PROJECT.md: Öfke 40'ta düşen ağaçlar geri yolları kapatır). Sahne içi NetworkObject.
/// Sunucu, seviye triggerLevel'e ulaşınca ve çevresinde oyuncu yokken ağacı düşürür (forceAfterSeconds beklenirse zorla);
/// durum herkese senkron, düşme animasyonu her kopyada yerel oynar (kök obje yerel +Z yönüne devrilir).
/// Devrilen gövde çarpışma olarak yolu kapatır.
/// KULLANICI (cila): ormandaki gerçek ağaçlar da hastalıkla devrilir. Seviye gelince yapraklar önce hastalık rengine
/// döner (çürür), ardından gıcırdayarak devrilir ve yere çarpınca gümbürder. Ağaç statik olmamalı (statik batch hareket etmez).
/// </summary>
public class FallenTree : NetworkBehaviour
{
    [SerializeField] private int triggerLevel = 2;
    [SerializeField] private float fallDuration = 2.2f;
    [SerializeField] private float fallAngle = 86f;
    [Tooltip("Düşmeden önce çevresinde (yatay) oyuncu olmaması gereken yarıçap (m).")]
    [SerializeField] private float clearRadius = 6f;
    [Tooltip("Seviye gelince oyuncu çevresinde olsa bile bu kadar sn bekledikten sonra zorla düşer.")]
    [SerializeField] private float forceAfterSeconds = 45f;

    [Header("Çürüme (KULLANICI)")]
    [Tooltip("Rengi hastalığa dönen parçalar (boşsa adı 'Canopy' ile başlayan çocuklar).")]
    [SerializeField] private Renderer[] witherRenderers;
    [SerializeField] private Color witherColor = new Color(0.32f, 0.22f, 0.3f);
    [Tooltip("Seviye geldikten sonra çürümenin sürdüğü süre (sn); düşme bundan önce başlamaz.")]
    [SerializeField] private float witherSeconds = 6f;
    [Tooltip("Aynı anda hepsi düşmesin: seviye geldikten sonra ek bekleme (sn).")]
    [SerializeField] private float extraDelay = 0f;

    [Tooltip("Yalnızca ağaç devrilince açılan çarpışmalar (ör. yukarı kalkan kökler); ayaktayken yolu kapatmasın.")]
    [SerializeField] private Collider[] enableWhenFallen;

    private readonly NetworkVariable<bool> fallen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Quaternion startRotation;
    private float progress;
    private float waited;
    private float wither;
    private bool animating;
    private Color[] baseColors;
    private MaterialPropertyBlock block;

    public bool HasFallen => fallen.Value;

    private void Awake()
    {
        startRotation = transform.rotation;
        if (witherRenderers == null || witherRenderers.Length == 0)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.name.StartsWith("Canopy")) list.Add(r);
            witherRenderers = list.ToArray();
        }
        baseColors = new Color[witherRenderers.Length];
        for (int i = 0; i < witherRenderers.Length; i++)
        {
            Material m = witherRenderers[i] != null ? witherRenderers[i].sharedMaterial : null;
            baseColors[i] = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
        }
        block = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        if (fallen.Value) { progress = 1f; wither = 1f; ApplyFall(1f); ApplyWither(1f); }
    }

    private void Update()
    {
        bool levelReached = GuidanceEvents.CurrentLevel >= triggerLevel;
        if (levelReached && wither < 1f)
        {
            wither = Mathf.MoveTowards(wither, 1f, Time.deltaTime / Mathf.Max(0.1f, witherSeconds));
            ApplyWither(wither);
        }

        if (IsServer && IsSpawned && !fallen.Value && levelReached)
        {
            waited += Time.deltaTime;
            bool ready = waited >= witherSeconds + extraDelay;
            if (ready && (IsClear() || waited >= forceAfterSeconds)) fallen.Value = true;
        }

        float target = fallen.Value ? 1f : 0f;
        if (!Mathf.Approximately(progress, target))
        {
            if (!animating && progress <= 0f && target > 0f) OnFallStart();
            animating = true;
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.1f, fallDuration));
            ApplyFall(progress);
            if (progress >= 1f) { animating = false; OnFallImpact(); }
        }
    }

    private bool IsClear()
    {
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null) continue;
            Vector3 d = p.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < clearRadius * clearRadius) return false;
        }
        return true;
    }

    private void ApplyFall(float t)
    {
        float eased = t * t * t; // önce yavaş yatar, sonra hızlanır
        transform.rotation = startRotation * Quaternion.AngleAxis(fallAngle * eased, Vector3.right);
        if (enableWhenFallen != null)
            foreach (Collider c in enableWhenFallen) if (c != null) c.enabled = t > 0.6f;
    }

    private void ApplyWither(float w)
    {
        for (int i = 0; i < witherRenderers.Length; i++)
        {
            Renderer r = witherRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Color.Lerp(baseColors[i], witherColor, Mathf.SmoothStep(0f, 1f, w)));
            r.SetPropertyBlock(block);
        }
    }

    private float Height()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        return Mathf.Max(3f, b.max.y - transform.position.y);
    }

    private void OnFallStart()
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib == null) return;
        // gövde çatırdar (tahta gıcırtısı kalın perdeden)
        Sfx.At(lib.doorCreak, transform.position + Vector3.up * 2f, 0.9f, 0.5f, 0.05f, 4f, 45f);
    }

    private void OnFallImpact()
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib == null) return;
        Vector3 hit = transform.position + (startRotation * Vector3.forward) * (Height() * 0.6f);
        Sfx.At(lib.treeCrash, hit, 1f, Random.Range(0.9f, 1.05f), 0.03f, 5f, 60f);
        Sfx.At(lib.rumble, hit, 0.5f, 0.9f, 0.05f, 5f, 50f);
    }
}
