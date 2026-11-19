using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-19: Mızrak vuruşunun ağ tarafı. Oyuncu kökünde durur, sahibi olmayan kopyalarda da AÇIK kalır.
/// Sahip vuruş isteğini sunucuya yollar; sunucu doğrular (mızrak envanterde, ağır eşya yok, hayatta, mesafe) ve hedefin OnSpearHit'ini çağırır.
/// </summary>
public class SpearNetworkSync : NetworkBehaviour
{
    [SerializeField] private ItemData spearItem;
    [SerializeField] private float reach = 3f;
    [Tooltip("Gecikme ve nesne merkezi farkı için ek mesafe payı (m).")]
    [SerializeField] private float extraTolerance = 2f;

    private InventoryNetworkSync inventorySync;
    private PlayerLife life;

    private void Awake()
    {
        inventorySync = GetComponent<InventoryNetworkSync>();
        life = GetComponent<PlayerLife>();
    }

    public ItemData SpearItem => spearItem;

    /// <summary>G-35: sahibi olmayan kopyalarda: bu oyuncu mızrakla dürttü (görsel).</summary>
    public event System.Action JabReceived;

    /// <summary>Sahip: dürtme hareketini diğer oyunculara bildirir (yalnızca görsel).</summary>
    public void BroadcastJab() { if (IsOwner && IsSpawned) JabRpc(); }

    [Rpc(SendTo.NotOwner)]
    private void JabRpc()
    {
        JabReceived?.Invoke();
        // KULLANICI (cila): diğer oyuncular da mızrak savurma sesini duysun
        if (Sfx.Lib != null) Sfx.At(Sfx.Lib.spearWhoosh, transform.position + Vector3.up * 1.4f + transform.forward * 0.6f, 0.45f, 1.15f, 0.1f, 1f, 15f);
        Noise.Emit(transform.position + Vector3.up * 1.4f, Noise.SpearSwing); // sunucu (Dinleyen) uzak oyuncunun savurmasını duyar
    }

    /// <summary>Sahip: vurulan hedefi sunucuya bildirir.</summary>
    public void RequestHit(NetworkObject target, Vector3 point, Vector3 direction)
    {
        if (!IsOwner || target == null) return;
        HitRpc(new NetworkObjectReference(target), point, direction);
    }

    [Rpc(SendTo.Server)]
    private void HitRpc(NetworkObjectReference targetRef, Vector3 point, Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (life != null && !life.CanAct) return;
        if (inventorySync == null || spearItem == null || !inventorySync.HasItem(spearItem) || inventorySync.CarriesHeavy()) return;
        if (!targetRef.TryGet(out NetworkObject no) || !no.IsSpawned) return;
        if (Vector3.Distance(transform.position + Vector3.up * 1.6f, no.transform.position) > reach + extraTolerance) return;

        ISpearTarget target = no.GetComponentInChildren<ISpearTarget>();
        if (target != null) target.OnSpearHit(OwnerClientId, point, direction);
    }
}
