using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-21: Taşınabilir güvenli alan (GDD 6.5: totemi almak daha küçük yarıçaplı taşınabilir alan verir).
/// - Oyuncu üzerinde: requiredItem (Totem) envanterdeyse etkin; taşıyanın çevresini korur.
/// - Yerdeki totem (Pickup_Totem) üzerinde: requiredItem boş, her zaman etkin.
/// Sunucuda alandaki hayattaki oyuncuların zehir birikmesini engeller (iyileştirmez, kullanıcı kararı). Işık her kopyada görünür.
/// </summary>
public class PortableSafeZone : MonoBehaviour
{
    public static readonly List<PortableSafeZone> All = new List<PortableSafeZone>();

    [SerializeField] private float radius = 4.5f;
    [SerializeField] private float height = 3f;
    [Tooltip("Doluysa: bu eşya yalnızca üstteki oyuncunun envanterinde varsa alan etkindir.")]
    [SerializeField] private ItemData requiredItem;
    [SerializeField] private Light glowLight;
    [SerializeField] private float lightIntensity = 6f;
    [Tooltip("KULLANICI KARARI: kapalı (false). Taşınan/yerdeki totem çiçek zehrini ENGELLEMEZ; yalnızca ışık verir, yaratıkların girmediği alan olur ve ölü diriltme için güvenli alan sayılır. Sabit totem ise zehri engellemeye devam eder.")]
    [SerializeField] private bool protectsFromPoison = false;

    private InventoryNetworkSync inventorySync;

    public float Radius => radius;

    public bool IsActive
    {
        get
        {
            if (requiredItem == null) return true;
            return inventorySync != null && inventorySync.HasItem(requiredItem);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { All.Clear(); }

    private void Awake() { inventorySync = GetComponentInParent<InventoryNetworkSync>(); }
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 c = transform.position;
        float dx = worldPosition.x - c.x;
        float dz = worldPosition.z - c.z;
        return dx * dx + dz * dz <= radius * radius && Mathf.Abs(worldPosition.y - c.y) <= height;
    }

    /// <summary>G-29: mağara çöküşünde tüm totem ışıkları söner (yalnızca görsel ışık).</summary>
    public static bool LightsOut { get; set; }

    private void Update()
    {
        bool active = IsActive;
        if (glowLight != null)
        {
            if (LightsOut) active = false;
            glowLight.enabled = active;
            if (active) glowLight.intensity = lightIntensity * (1f + (Mathf.PerlinNoise(Time.time * 2f, 3.7f) - 0.5f) * 0.2f);
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (!protectsFromPoison || !active || nm == null || !nm.IsServer) return;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive || !Contains(p.transform.position)) continue;
            PoisonSystem poison = p.GetComponent<PoisonSystem>();
            if (poison != null) poison.ServerProtect();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
