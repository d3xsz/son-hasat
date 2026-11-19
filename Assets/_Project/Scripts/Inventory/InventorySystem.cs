using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-06/G-07: 4 slotlu envanter. 1-4 ile slot seçimi, Q ile seçili eşyayı bırakma.
/// Başlangıç eşyaları sırayla slotlara yerleşir (1: fener, kilitli; 2: mızrak).
/// Ağır eşya: aynı anda en fazla 1; taşırken IsCarryingHeavy true (hız/koşu/mızrak kısıtları buna bakar).
/// Gösterim HudController'da (G-08): Changed ve MessageRaised event'leri.
/// </summary>
public class InventorySystem : MonoBehaviour
{
    public const int SlotCount = 4;

    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private float dropDistance = 1.2f;
    [SerializeField] private float dropForce = 2f;
    [Tooltip("Oyun başında sırayla 1., 2., ... slota yerleşir.")]
    [SerializeField] private ItemData[] startingItems;

    private readonly ItemData[] items = new ItemData[SlotCount];
    private readonly InputAction[] slotActions = new InputAction[SlotCount];
    private InputAction dropAction;
    private InventoryNetworkSync netSync;
    private PlayerLife life;

    public event Action Changed;
    public event Action<string> MessageRaised;
    public int SelectedIndex { get; private set; }
    public ItemData SelectedItem => items[SelectedIndex];

    public bool HasFreeSlot
    {
        get
        {
            for (int i = 0; i < SlotCount; i++) if (items[i] == null) return true;
            return false;
        }
    }

    /// <summary>Envanterde ağır bir eşya varsa true (GDD: hız ~%40 düşer, koşu ve mızrak yok).</summary>
    public bool IsCarryingHeavy
    {
        get
        {
            for (int i = 0; i < SlotCount; i++) if (items[i] != null && items[i].IsHeavy) return true;
            return false;
        }
    }

    public ItemData GetItem(int index)
    {
        return (index >= 0 && index < SlotCount) ? items[index] : null;
    }

    private void Awake()
    {
        var map = inputActions.FindActionMap("Player", true);
        for (int i = 0; i < SlotCount; i++) slotActions[i] = map.FindAction("Slot" + (i + 1), true);
        dropAction = map.FindAction("Drop", true);
        netSync = GetComponent<InventoryNetworkSync>();
        life = GetComponent<PlayerLife>();

        if (startingItems != null)
            for (int i = 0; i < startingItems.Length && i < SlotCount; i++) items[i] = startingItems[i];
    }

    private void OnEnable()
    {
        if (slotActions != null) foreach (var a in slotActions) a.Enable();
        dropAction?.Enable();
    }

    private void OnDisable()
    {
        if (slotActions != null) foreach (var a in slotActions) a.Disable();
        dropAction?.Disable();
    }

    private void Update()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (life != null && !life.CanAct) return;
        for (int i = 0; i < SlotCount; i++)
            if (slotActions[i].WasPressedThisFrame()) Select(i);
        if (dropAction.WasPressedThisFrame()) DropSelected();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= SlotCount || index == SelectedIndex) return;
        SelectedIndex = index;
        Changed?.Invoke();
    }

    /// <summary>Slottaki eşyayı kaldırır (tüketilince). Kilitli eşya kaldırılamaz.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= SlotCount || items[index] == null || items[index].IsLocked) return false;
        items[index] = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Eşya eklenebilir mi? Eklenemezse reason ekranda gösterilecek metni verir.</summary>
    public bool CanAdd(ItemData item, out string reason)
    {
        reason = null;
        if (item == null) { reason = "Geçersiz eşya"; return false; }
        if (item.IsHeavy && IsCarryingHeavy) { reason = "Zaten ağır bir eşya taşıyorsun"; return false; }
        if (!HasFreeSlot) { reason = "Envanter dolu"; return false; }
        return true;
    }

    /// <summary>İlk boş slota ekler.</summary>
    public bool TryAdd(ItemData item, out int slot)
    {
        slot = -1;
        if (!CanAdd(item, out _)) return false;
        for (int i = 0; i < SlotCount; i++)
        {
            if (items[i] == null)
            {
                items[i] = item;
                slot = i;
                Changed?.Invoke();
                return true;
            }
        }
        return false;
    }

    /// <summary>Seçili slottaki eşyayı önüne bırakır.</summary>
    public bool DropSelected()
    {
        ItemData item = items[SelectedIndex];
        if (item == null) return false;
        if (item.IsLocked)
        {
            ShowMessage(item.DisplayName + " bırakılamaz");
            return false;
        }
        if (item.WorldPrefab == null)
        {
            ShowMessage("Bu eşyanın dünya prefab'ı yok");
            return false;
        }

        Vector3 origin = dropOrigin.position;
        Vector3 dir = dropOrigin.forward;
        float dist = dropDistance;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dropDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(0.3f, hit.distance - 0.3f);

        Vector3 pos = origin + dir * dist + Vector3.down * 0.3f;
        Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (netSync != null && netSync.IsSpawned)
        {
            // Ağda: eşyayı sunucu doğurur (herkes görür)
            netSync.RequestDrop(item, pos, rot, dir * dropForce);
        }
        else
        {
            GameObject go = Instantiate(item.WorldPrefab, pos, rot);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce(dir * dropForce, ForceMode.VelocityChange);
        }

        items[SelectedIndex] = null;
        Changed?.Invoke();
        return true;
    }

    public void ShowMessage(string text)
    {
        MessageRaised?.Invoke(text);
    }
}
