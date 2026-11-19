using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-06/G-07/G-12: Yerdeki eşya. E ile envantere alınır (IInteractable, G-04).
/// Ağda: alma isteği sunucuya gider (InventoryNetworkSync), sunucu doğrularsa eşya herkesten kaybolur.
/// Prefab'da Collider + Rigidbody + NetworkObject + NetworkTransform + NetworkRigidbody + PickupItem olmalı; itemData atanmalı.
/// </summary>
public class PickupItem : NetworkBehaviour, IInteractable
{
    [SerializeField] private ItemData itemData;

    public ItemData Item => itemData;
    public string InteractionPrompt => itemData != null ? "Al: " + itemData.DisplayName : "Al";

    public bool CanInteract(PlayerInteractor interactor) { return itemData != null; }

    public void Interact(PlayerInteractor interactor)
    {
        InventorySystem inventory = interactor.GetComponent<InventorySystem>();
        if (inventory == null) return;

        if (!inventory.CanAdd(itemData, out string reason))
        {
            inventory.ShowMessage(reason);
            return;
        }

        InventoryNetworkSync netSync = interactor.GetComponent<InventoryNetworkSync>();
        if (netSync != null && netSync.IsSpawned && IsSpawned)
        {
            netSync.RequestPickup(this);
            return;
        }

        // Ağ dışı (test) yolu
        if (inventory.TryAdd(itemData, out _)) Destroy(gameObject);
    }
}
