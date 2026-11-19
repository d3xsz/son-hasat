using UnityEngine;

/// <summary>
/// B2-03 (GDD 7.1): Kristal parçası seçili slottayken oyuncunun elinde parlar ve çevresini her yöne hafifçe aydınlatır
/// (fener gibi ısınmaz, yönsüz; karanlık geçitte yol ışığı). Her kopyada yerel çalışır: elindeki eşya ağda zaten
/// senkron (InventoryNetworkSync.SelectedItemId). Sahip birinci şahıs görünümünü (kamera altı), diğerleri gövdedeki
/// görünümü görür. Işık hafifçe titrer.
/// </summary>
public class HeldCrystal : MonoBehaviour
{
    [SerializeField] private ItemData crystalItem;
    [Tooltip("Sahibin gördüğü (kamera altındaki) görünüm.")]
    [SerializeField] private GameObject viewVisual;
    [Tooltip("Diğer oyuncuların gördüğü (eldeki) görünüm.")]
    [SerializeField] private GameObject bodyVisual;
    [SerializeField] private Light glow;
    [SerializeField] private float intensity = 2.4f;

    private InventorySystem inventory;
    private InventoryNetworkSync net;
    private Unity.Netcode.NetworkObject netObj;
    private PlayerLife life;
    private float phase;
    private float shown;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
        net = GetComponent<InventoryNetworkSync>();
        netObj = GetComponent<Unity.Netcode.NetworkObject>();
        life = GetComponent<PlayerLife>();
        phase = Random.value * 10f;
    }

    private bool IsLocal => netObj == null || !netObj.IsSpawned || netObj.IsOwner;

    private bool Holding()
    {
        if (crystalItem == null) return false;
        if (life != null && life.State == PlayerLifeState.Dead) return false;
        if (IsLocal) return inventory != null && inventory.SelectedItem == crystalItem;
        return net != null && net.IsSpawned && net.ItemOf(net.SelectedItemId) == crystalItem;
    }

    private void Update()
    {
        bool holding = Holding();
        bool local = IsLocal;
        if (viewVisual != null && viewVisual.activeSelf != (holding && local)) viewVisual.SetActive(holding && local);
        if (bodyVisual != null && bodyVisual.activeSelf != (holding && !local)) bodyVisual.SetActive(holding && !local);

        shown = Mathf.MoveTowards(shown, holding ? 1f : 0f, Time.deltaTime * 3f);
        if (glow != null)
        {
            float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 2.5f, phase);
            glow.intensity = intensity * shown * flicker;
            glow.enabled = shown > 0.01f;
            // ışık sahipte kameraya, diğerlerinde ele bağlı görünümün yanında dursun
            Transform anchor = local ? (viewVisual != null ? viewVisual.transform : null) : (bodyVisual != null ? bodyVisual.transform : null);
            if (anchor != null) glow.transform.position = anchor.position;
        }
    }
}
