using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-27 (yeniden yönlendirildi, kullanıcı vizyonu): PUBG tarzı daralan alan. Güvenli alan Ada Öfkesi seviyesi arttıkça küçülür ve merkezi
/// mağaraya doğru kayar; alanın DIŞI hastalıklı çiçeklerle dolar ve oyuncuyu zehirler, böylece oyuncular mağaraya doğru gitmek zorunda kalır.
/// Sunucu yarıçap/merkezi yumuşakça hareket ettirir (NetworkVariable ile herkese senkron) ve dışarıdaki hayattaki oyunculara zehir ekler
/// (PoisonSystem.AddPoison: sabit totem güvenli alanında otomatik yok sayılır). Görsel: ZoneBoundary (kenar halkası), ZoneFlowerField (çiçek tarlası).
/// Sahne içi NetworkObject. Seviye tabloları G-28'de gerçek harita boyutuna göre ayarlanacak.
/// </summary>
public class ShrinkingZone : NetworkBehaviour
{
    public static ShrinkingZone Instance { get; private set; }

    [Header("Merkez (başlangıçtan mağaraya doğru kayar)")]
    [Tooltip("Alanın Öfke 0'daki merkezi (boşsa dünya orijini).")]
    [SerializeField] private Transform startCenter;
    [Tooltip("Alanın son merkezi: mağara girişi.")]
    [SerializeField] private Transform endCenter;

    [Header("Öfke seviyesine göre (0-5)")]
    [Tooltip("Güvenli alan yarıçapı (m). Seviye 0'daki büyük değer = harita dışı, alan fiilen kapalı.")]
    [SerializeField] private float[] radiusByLevel = { 120f, 70f, 52f, 36f, 22f, 10f };
    [Tooltip("Merkezin başlangıçtan mağaraya ilerleme oranı (0-1).")]
    [SerializeField] private float[] centerProgressByLevel = { 0f, 0f, 0.25f, 0.55f, 0.85f, 1f };

    [Header("Hız ve zehir")]
    [SerializeField] private float shrinkSpeed = 1.5f;
    [SerializeField] private float centerSpeed = 1.2f;
    [Tooltip("Alanın dışındaki hayattaki oyuncuya saniyede eklenen zehir.")]
    [SerializeField] private float outsidePoisonPerSecond = 5f;
    [Tooltip("Alınmamış totemlerin çevresinde alanın en az bu kadar (m) payı kalır.")]
    [SerializeField] private float totemMargin = 12f;
    [Tooltip("Bu yüksekliğin altındaki oyuncular (mağaraya düşenler) alan zehrinden etkilenmez.")]
    [SerializeField] private float undergroundY = -8f;

    private readonly NetworkVariable<float> radius = new NetworkVariable<float>(
        120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> center = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float serverRadius;
    private Vector3 serverCenter;

    public float Radius => radius.Value;
    public Vector3 Center => center.Value;

    /// <summary>Yatay mesafeye göre: verilen konum güvenli alanın dışında mı?</summary>
    public bool IsOutside(Vector3 worldPosition)
    {
        Vector3 d = worldPosition - center.Value;
        d.y = 0f;
        return d.sqrMagnitude > radius.Value * radius.Value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    private Vector3 StartPosition => startCenter != null ? startCenter.position : Vector3.zero;
    private Vector3 EndPosition => endCenter != null ? endCenter.position : StartPosition;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer)
        {
            serverRadius = radiusByLevel[0];
            serverCenter = StartPosition;
            radius.Value = serverRadius;
            center.Value = serverCenter;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        float dt = Time.deltaTime;
        int level = Mathf.Clamp(GuidanceEvents.CurrentLevel, 0, Mathf.Min(radiusByLevel.Length, centerProgressByLevel.Length) - 1);

        float targetRadius = radiusByLevel[level];
        Vector3 targetCenter = Vector3.Lerp(StartPosition, EndPosition, centerProgressByLevel[level]);
        targetCenter.y = 0f;

        // KULLANICI KARARI: son totem alınana kadar alınmamış her totem (ve çevresi) güvenli alanın içinde kalır.
        foreach (Totem t in Totem.All)
        {
            if (t == null || t.IsTaken) continue;
            Vector3 d = t.transform.position - targetCenter;
            d.y = 0f;
            targetRadius = Mathf.Max(targetRadius, d.magnitude + totemMargin);
        }

        serverRadius = Mathf.MoveTowards(serverRadius, targetRadius, shrinkSpeed * dt);
        serverCenter = Vector3.MoveTowards(serverCenter, targetCenter, centerSpeed * dt);

        if (Mathf.Abs(radius.Value - serverRadius) > 0.02f) radius.Value = serverRadius;
        if ((center.Value - serverCenter).sqrMagnitude > 0.0004f) center.Value = serverCenter;

        // Dışarıdaki hayattaki oyuncular zehirlenir (sabit totem alanında otomatik korunur)
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive || !IsOutside(p.transform.position)) continue;
            if (p.transform.position.y < undergroundY) continue; // mağaraya düşenler (kuyu/tünel/Bölüm 2) etkilenmez
            PoisonSystem poison = p.GetComponent<PoisonSystem>();
            if (poison != null) poison.AddPoison(outsidePoisonPerSecond * dt);
        }
    }
}
