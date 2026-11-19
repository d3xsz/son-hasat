using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-15/G-19: Hastalık çiçeği. Görsel her kopyada çalışır; kesilme durumu sunucuda tutulur ve senkronlanır.
/// - Çiçek başı emissive materyalle nabız gibi parlar (GDD: ekstra ışık gerekmez).
/// - Yerdeki spor parçacıkları ışığa duyarlı (lit) materyalle çizilir: karanlıkta görünmez, fener tutulunca görünür.
/// - Yerel oyuncu yaklaşınca spor üretimi artar (spor bulutu).
/// - KULLANICI KARARI (G-32 sonrası): mızrak çiçeği KESMEZ (mızrak yalnızca yaratıklara). Kesilme durumu kodda duruyor ama tetiklenmiyor.
/// </summary>
[RequireComponent(typeof(PoisonZone))]
public class DiseaseFlower : NetworkBehaviour
{
    [Header("Parıltı")]
    [SerializeField] private Renderer headRenderer;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(0.55f, 0.15f, 1f);
    [SerializeField] private float glowBase = 2.2f;
    [SerializeField] private float glowPulse = 0.6f;
    [SerializeField] private float pulseSpeed = 1.5f;

    [Header("Sporlar")]
    [SerializeField] private ParticleSystem spores;
    [SerializeField] private float baseRate = 3f;
    [SerializeField] private float nearRate = 25f;
    [Tooltip("Yerel oyuncu bölge yarıçapının bu katı kadar yaklaşınca spor artar.")]
    [SerializeField] private float nearDistanceFactor = 1.6f;

    [Header("Kesilme (G-19)")]
    [SerializeField] private Transform stem;
    [Tooltip("Kesilince gövdenin kalan boy oranı.")]
    [SerializeField, Range(0.05f, 1f)] private float stumpHeightFactor = 0.25f;
    [Tooltip("Gerçek model kullanılırken sap çubuğu yalnızca kesilince (kütük olarak) görünsün.")]
    [SerializeField] private bool stemOnlyWhenCut = false;

    private readonly NetworkVariable<bool> cut = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PoisonZone zone;
    private MaterialPropertyBlock block;
    private float phase;
    private float nextCheck;
    private float targetRate;
    private float currentRate;
    private float lastRadius = -1f;
    private Vector3 stemStartScale;
    private Vector3 stemStartPosition;

    public bool IsCut => cut.Value;

    private void Awake()
    {
        zone = GetComponent<PoisonZone>();
        block = new MaterialPropertyBlock();
        phase = Random.value * Mathf.PI * 2f;
        targetRate = baseRate;
        currentRate = baseRate;
        if (stem != null)
        {
            stemStartScale = stem.localScale;
            stemStartPosition = stem.localPosition;
        }
    }

    public override void OnNetworkSpawn()
    {
        cut.OnValueChanged += OnCutChanged;
        ApplyCut(cut.Value);
    }

    public override void OnNetworkDespawn()
    {
        cut.OnValueChanged -= OnCutChanged;
    }

    private void OnCutChanged(bool oldValue, bool newValue) { ApplyCut(newValue); }

    private void ApplyCut(bool isCut)
    {
        if (zone != null) zone.Active = !isCut;
        if (headRenderer != null) headRenderer.enabled = !isCut;
        if (spores != null)
        {
            if (isCut) spores.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            else if (!spores.isPlaying) spores.Play();
        }
        if (stem != null)
        {
            float f = isCut ? stumpHeightFactor : 1f;
            stem.localScale = new Vector3(stemStartScale.x, stemStartScale.y * f, stemStartScale.z);
            stem.localPosition = new Vector3(stemStartPosition.x, stemStartPosition.y * f, stemStartPosition.z);
            if (stemOnlyWhenCut && stem.TryGetComponent(out Renderer sr)) sr.enabled = isCut;
        }
    }

    private void Update()
    {
        if (cut.Value) return;
        UpdateGlow();
        UpdateSpores();
    }

    private void UpdateGlow()
    {
        if (headRenderer == null) return;
        float k = glowBase + Mathf.Sin(Time.time * pulseSpeed + phase) * glowPulse;
        headRenderer.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", glowColor * k);
        headRenderer.SetPropertyBlock(block);
    }

    private void UpdateSpores()
    {
        if (spores == null) return;

        if (!Mathf.Approximately(lastRadius, zone.Radius))
        {
            lastRadius = zone.Radius;
            ParticleSystem.ShapeModule shape = spores.shape;
            shape.radius = lastRadius;
            // V-06: yere yakın zehir pusu (alt sistem) da bölgeyle birlikte büyür
            foreach (ParticleSystem child in spores.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (child == spores || child.name != "Miasma") continue;
                ParticleSystem.ShapeModule cs = child.shape;
                cs.radius = lastRadius * 0.9f;
            }
        }

        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.2f;
            Transform local = GetLocalPlayer();
            bool near = false;
            if (local != null)
            {
                Vector3 d = local.position - transform.position;
                d.y = 0f;
                near = d.magnitude <= zone.Radius * nearDistanceFactor;
            }
            targetRate = near ? nearRate : baseRate;
        }

        currentRate = Mathf.MoveTowards(currentRate, targetRate, 40f * Time.deltaTime);
        ParticleSystem.EmissionModule emission = spores.emission;
        emission.rateOverTime = currentRate;
    }

    private static Transform GetLocalPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.transform;
    }
}
