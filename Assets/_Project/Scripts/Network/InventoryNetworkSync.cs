using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-12: Envanterin ağ tarafı. Oyuncu kökünde durur, sahibi olmayan kopyalarda da AÇIK kalır.
/// - Envanter içeriği sahipte tutulur; slot kimlikleri NetworkList ile herkese (özellikle sunucuya) görünür.
/// - Alma: sahip isteği sunucuya yollar, sunucu doğrular ve eşyayı despawn eder, sahibe onay döner.
/// - Bırakma: sahip sunucudan eşyanın dünyada doğmasını ister (sunucu spawn eder).
/// </summary>
public class InventoryNetworkSync : NetworkBehaviour
{
    [SerializeField] private ItemDatabase database;
    [Tooltip("Sunucunun kabul ettiği en fazla alma/bırakma mesafesi (hile ve gecikme payı)."), SerializeField]
    private float maxInteractDistance = 4f;

    private NetworkList<int> slotIds;
    // G-35: sahibin seçili slotu (diğer oyuncular elindeki eşyayı görsün)
    private readonly NetworkVariable<int> selectedSlot = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private InventorySystem inventory;

    /// <summary>Herkes okuyabilir: bu oyuncunun elindeki (seçili slottaki) eşyanın kimliği (-1 boş).</summary>
    public int SelectedItemId => GetSlotItemId(selectedSlot.Value);

    /// <summary>Herkes okuyabilir: kimlikten eşya.</summary>
    public ItemData ItemOf(int id) => database != null ? database.Get(id) : null;

    /// <summary>Herkes okuyabilir: eşyanın kimliği.</summary>
    public int IdOf(ItemData item) => database != null ? database.IdOf(item) : -1;

    private void Update()
    {
        if (IsSpawned && IsOwner && inventory != null && selectedSlot.Value != inventory.SelectedIndex)
            selectedSlot.Value = inventory.SelectedIndex;
    }
    private bool pendingPickup;
    private ItemData pendingItem;

    /// <summary>Herkes okuyabilir: bu oyuncunun verilen slotundaki eşyanın kimliği (-1 boş).</summary>
    public int GetSlotItemId(int slot)
    {
        return (slotIds != null && slot >= 0 && slot < slotIds.Count) ? slotIds[slot] : -1;
    }

    /// <summary>Herkes okuyabilir: bu oyuncu ağır eşya taşıyor mu? (totem/diriltme kuralları için)</summary>
    public bool CarriesHeavy()
    {
        if (slotIds == null || database == null) return false;
        for (int i = 0; i < slotIds.Count; i++)
        {
            ItemData item = database.Get(slotIds[i]);
            if (item != null && item.IsHeavy) return true;
        }
        return false;
    }

    /// <summary>Herkes okuyabilir: bu oyuncunun envanterinde verilen eşya var mı? (sunucu doğrulaması için)</summary>
    public bool HasItem(ItemData item)
    {
        if (slotIds == null || database == null || item == null) return false;
        int id = database.IdOf(item);
        if (id < 0) return false;
        for (int i = 0; i < slotIds.Count; i++) if (slotIds[i] == id) return true;
        return false;
    }

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
        slotIds = new NetworkList<int>(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        inventory.Changed += OnInventoryChanged;
        SyncSlots();
    }

    public override void OnNetworkDespawn()
    {
        if (inventory != null) inventory.Changed -= OnInventoryChanged;
    }

    private void OnInventoryChanged() { SyncSlots(); }

    private void SyncSlots()
    {
        if (!IsOwner || database == null) return;
        while (slotIds.Count < InventorySystem.SlotCount) slotIds.Add(-1);
        for (int i = 0; i < InventorySystem.SlotCount; i++)
        {
            int id = database.IdOf(inventory.GetItem(i));
            if (slotIds[i] != id) slotIds[i] = id;
        }
    }

    // ---------- ALMA ----------

    /// <summary>Sahip: yerdeki eşyayı almak için sunucudan izin ister.</summary>
    public void RequestPickup(PickupItem item)
    {
        if (!IsOwner || pendingPickup || item == null || !item.IsSpawned) return;
        pendingPickup = true;
        pendingItem = item.Item;
        PickupRpc(new NetworkObjectReference(item.NetworkObject));
    }

    [Rpc(SendTo.Server)]
    private void PickupRpc(NetworkObjectReference itemRef, RpcParams rpcParams = default)
    {
        bool ok = false;
        if (rpcParams.Receive.SenderClientId == OwnerClientId
            && itemRef.TryGet(out NetworkObject no)
            && no.IsSpawned
            && no.GetComponent<PickupItem>() != null
            && Vector3.Distance(transform.position, no.transform.position) <= maxInteractDistance)
        {
            // sahne içi nesne (ör. Bölüm 2 kristal parçaları) yok edilmez: despawn + gizle (NGO önerisi; istemcilerde zaten gizlenir)
            if (no.InScenePlaced) { no.Despawn(false); no.gameObject.SetActive(false); }
            else no.Despawn(true);
            ok = true;
        }
        PickupResultRpc(ok);
    }

    [Rpc(SendTo.Owner)]
    private void PickupResultRpc(bool granted)
    {
        ItemData item = pendingItem;
        pendingItem = null;
        pendingPickup = false;

        if (granted && Sfx.Lib != null) Sfx.At(Sfx.Lib.pickup, transform.position + Vector3.up * 0.9f, 0.5f, 1f, 0.08f, 1f, 12f); // KULLANICI (cila)
        if (!granted)
        {
            inventory.ShowMessage("Eşya alınamadı");
            return;
        }
        if (item != null && !inventory.TryAdd(item, out _))
        {
            // Nadir durum: yer kalmadı. Eşya kaybolmasın diye yere geri bırak.
            inventory.ShowMessage("Envanter doluydu, eşya yere bırakıldı");
            RequestDrop(item, transform.position + transform.forward * 0.8f + Vector3.up * 0.5f, Quaternion.identity, Vector3.zero);
        }
    }

    // ---------- SABİT TOTEMİ ALMA (G-21) ----------

    /// <summary>Sahip: sahnedeki sabit totemi almak için sunucudan izin ister.</summary>
    public void RequestTotemPickup(Totem totem, ItemData item)
    {
        if (!IsOwner || pendingPickup || totem == null || !totem.IsSpawned) return;
        pendingPickup = true;
        pendingItem = item;
        TotemPickupRpc(new NetworkObjectReference(totem.NetworkObject));
    }

    [Rpc(SendTo.Server)]
    private void TotemPickupRpc(NetworkObjectReference totemRef, RpcParams rpcParams = default)
    {
        bool ok = false;
        if (rpcParams.Receive.SenderClientId == OwnerClientId
            && totemRef.TryGet(out NetworkObject no)
            && no.IsSpawned
            && no.TryGetComponent(out Totem totem)
            && totem.CanBeTaken
            && Vector3.Distance(transform.position, no.transform.position) <= maxInteractDistance)
        {
            totem.ServerTake(OwnerClientId);
            ok = true;
        }
        PickupResultRpc(ok);
    }

    // ---------- TÜKETME (G-26b) ----------

    /// <summary>Sahip: slottaki tüketilebilir eşyayı (panzehir) kullanmak için sunucudan izin ister.</summary>
    public void RequestUse(int slot)
    {
        if (!IsOwner || database == null) return;
        UseRpc(slot);
    }

    [Rpc(SendTo.Server)]
    private void UseRpc(int slot, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        PlayerLife playerLife = GetComponent<PlayerLife>();
        if (playerLife != null && !playerLife.CanAct) return;
        if (slotIds == null || slot < 0 || slot >= slotIds.Count) return;
        ItemData item = database != null ? database.Get(slotIds[slot]) : null;
        PoisonSystem poison = GetComponent<PoisonSystem>();
        if (item == null || !item.IsConsumable || poison == null) return;

        if (poison.Value <= 0.01f)
        {
            UseResultRpc(slot, false);
            return;
        }
        poison.RemovePoison(item.PoisonReduction);
        UseResultRpc(slot, true);
    }

    [Rpc(SendTo.Owner)]
    private void UseResultRpc(int slot, bool consumed)
    {
        ItemData item = inventory.GetItem(slot);
        if (!consumed)
        {
            inventory.ShowMessage("Zehirlenmedin");
            return;
        }
        inventory.RemoveAt(slot);
        inventory.ShowMessage((item != null ? item.DisplayName : "Eşya") + " kullanıldı");
    }

    // ---------- SUNUCU EŞYA ALIR (B2-10 kristal yuvası) ----------

    /// <summary>Sunucu: sahibin envanterinden bu eşyayı (öncelikle seçili slottakini) bir tane düşürür.</summary>
    public void ServerTakeItem(ItemData item)
    {
        if (!IsServer || database == null) return;
        int id = database.IdOf(item);
        if (id >= 0) TakeItemRpc(id);
    }

    [Rpc(SendTo.Owner)]
    private void TakeItemRpc(int itemId)
    {
        ItemData item = database != null ? database.Get(itemId) : null;
        if (item == null) return;
        int slot = inventory.SelectedItem == item ? inventory.SelectedIndex : -1;
        for (int i = 0; slot < 0 && i < InventorySystem.SlotCount; i++) if (inventory.GetItem(i) == item) slot = i;
        if (slot >= 0) inventory.RemoveAt(slot);
    }

    // ---------- BIRAKMA ----------

    /// <summary>Sahip: eşyanın dünyada belirtilen yerde doğmasını ister.</summary>
    public void RequestDrop(ItemData item, Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        if (!IsOwner || database == null) return;
        int id = database.IdOf(item);
        if (id < 0) return;
        DropRpc(id, position, rotation, velocity);
    }

    [Rpc(SendTo.Server)]
    private void DropRpc(int itemId, Vector3 position, Quaternion rotation, Vector3 velocity, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        ItemData item = database != null ? database.Get(itemId) : null;
        if (item == null || item.WorldPrefab == null) return;

        // Oyuncudan çok uzağa eşya doğurmayı engelle
        if (Vector3.Distance(transform.position, position) > maxInteractDistance)
            position = transform.position + Vector3.up;

        GameObject go = Instantiate(item.WorldPrefab, position, rotation);
        NetworkObject no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[Ağ] Eşya prefab'ında NetworkObject yok: " + item.DisplayName);
            Destroy(go);
            return;
        }
        no.Spawn(true);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.AddForce(velocity, ForceMode.VelocityChange);
    }
}
