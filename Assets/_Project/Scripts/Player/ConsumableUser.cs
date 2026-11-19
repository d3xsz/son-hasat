using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-26b: Tüketilebilir eşya kullanımı (panzehir/iksir). Yalnızca sahipte çalışır (NetworkPlayer sahibe özel listesinde).
/// Seçili slottaki eşya tüketilebilirse sol tık (Attack girdisi) ile kullanılır; sunucu doğrular, zehri düşürür, eşya envanterden düşer.
/// Mızrak seçiliyken sol tık dürtmedir (Spear), bu bileşen yalnızca tüketilebilir seçiliyken çalışır.
/// </summary>
public class ConsumableUser : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private float cooldown = 0.8f;

    private InputAction attackAction;
    private InventorySystem inventory;
    private InventoryNetworkSync net;
    private PlayerLife life;
    private float nextUse;
    private bool wasLocked;

    private void Awake()
    {
        attackAction = inputActions.FindActionMap("Player", true).FindAction("Attack", true);
        inventory = GetComponent<InventorySystem>();
        net = GetComponent<InventoryNetworkSync>();
        life = GetComponent<PlayerLife>();
    }

    private void OnEnable() { attackAction.Enable(); }
    private void OnDisable() { attackAction.Disable(); }

    private void Update()
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (attackAction.WasPressedThisFrame() && locked && wasLocked && (life == null || life.CanAct) && Time.time >= nextUse)
        {
            ItemData item = inventory != null ? inventory.SelectedItem : null;
            if (item != null && item.IsConsumable && net != null && net.IsSpawned)
            {
                nextUse = Time.time + cooldown;
                net.RequestUse(inventory.SelectedIndex);
            }
        }
        wasLocked = locked;
    }
}
