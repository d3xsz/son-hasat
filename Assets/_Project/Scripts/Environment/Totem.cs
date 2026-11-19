using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-20/G-21: Sabit totem (GDD 6.5). Etrafında ~9 m güvenli alan: içindeki hayattaki oyuncuların zehri birikmez
/// (kullanıcı kararı: totem iyileştirmez; iyileştirme panzehir/iksir ile). Dinlenme noktası ve checkpoint.
/// G-21: E ile alınır (ağır eşya Totem olarak envantere girer); sabit olan yerinden kalkar (üst kısım gizlenir, alan ve checkpoint kapanır).
/// İlk alınışta statik TotemTaken olayı yayınlanır (G-22 Ada Öfkesi +20 bunu dinleyecek).
/// </summary>
[RequireComponent(typeof(Checkpoint))]
public class Totem : NetworkBehaviour, IInteractable
{
    public static readonly List<Totem> All = new List<Totem>();

    /// <summary>Yalnızca SUNUCUDA: sabit totem yerinden alındı.</summary>
    public static event Action<Totem> TotemTaken;

    [Header("Güvenli alan (GDD 6.5: ~8-10 m)")]
    [SerializeField] private float safeRadius = 9f;
    [Tooltip("Alanın zeminden yukarı/aşağı erişimi (m).")]
    [SerializeField] private float safeHeight = 4f;
    [Tooltip("KULLANICI KARARI: 0. Totem zehri iyileştirmez, yalnızca birikmesini engeller (iyileştirme panzehir/iksir ile).")]
    [SerializeField] private float healPerSecond = 0f;

    [Header("Alma (G-21)")]
    [SerializeField] private ItemData totemItem;
    [Tooltip("Totem alınınca gizlenecek parçalar (taban kütük olarak kalır).")]
    [SerializeField] private GameObject[] hideWhenTaken;

    [Header("Görsel")]
    [SerializeField] private Light glowLight;
    [SerializeField] private Renderer[] glowRenderers;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField] private float glowIntensity = 2.5f;
    [SerializeField] private float lightBaseIntensity = 12f;
    [SerializeField, Range(0f, 0.5f)] private float flicker = 0.12f;

    private readonly NetworkVariable<bool> active = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Checkpoint checkpoint;
    private MaterialPropertyBlock block;
    private float blend = 1f;
    private float phase;

    public bool IsActive => active.Value && !taken.Value;
    public bool IsTaken => taken.Value;
    public bool CanBeTaken => !taken.Value;
    public float SafeRadius => safeRadius;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { All.Clear(); TotemTaken = null; TakenCountChanged = null; }

    private void Awake()
    {
        checkpoint = GetComponent<Checkpoint>();
        block = new MaterialPropertyBlock();
        phase = UnityEngine.Random.value * 10f;
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public override void OnNetworkSpawn()
    {
        active.OnValueChanged += OnActiveChanged;
        taken.OnValueChanged += OnTakenChanged;
        ApplyActive(active.Value);
        ApplyTaken(taken.Value);
    }

    public override void OnNetworkDespawn()
    {
        active.OnValueChanged -= OnActiveChanged;
        taken.OnValueChanged -= OnTakenChanged;
    }

    private void OnActiveChanged(bool oldValue, bool newValue) { ApplyActive(newValue); }
    private void OnTakenChanged(bool oldValue, bool newValue)
    {
        ApplyTaken(newValue);
        // KULLANICI (cila): totem yerinden sökülürken taş sürtünmesi (herkes duyar)
        if (newValue && !oldValue && Sfx.Lib != null) Sfx.At(Sfx.Lib.totemTake, transform.position + Vector3.up, 0.9f, 0.85f, 0.04f, 3f, 35f);
        if (newValue && !oldValue)
        {
            int taken = 0, total = 0;
            foreach (Totem t in All) { if (t == null) continue; total++; if (t.IsTaken) taken++; }
            TakenCountChanged?.Invoke(taken, total);
        }
    }

    /// <summary>KULLANICI (cila): herhangi bir totem alınınca tüm kopyalarda (alınan, toplam). TotemCounterPopup dinler.</summary>
    public static event System.Action<int, int> TakenCountChanged;

    private void ApplyActive(bool isActive)
    {
        if (checkpoint != null) checkpoint.CanActivate = isActive && !taken.Value;
    }

    private void ApplyTaken(bool isTaken)
    {
        if (hideWhenTaken != null)
            foreach (GameObject g in hideWhenTaken) if (g != null) g.SetActive(!isTaken);
        ApplyActive(active.Value);
    }

    /// <summary>Sunucu: totemi söndürür/yakar (Öfke 80'de ışıklar sönünce G-22/G-25).</summary>
    public void ServerSetActive(bool value)
    {
        if (!IsServer) return;
        active.Value = value;
    }

    /// <summary>Sunucu: totem yerinden alındı (sabit totem kalkar, olay yayınlanır).</summary>
    public void ServerTake(ulong takerClientId)
    {
        if (!IsServer || taken.Value) return;
        taken.Value = true;
        active.Value = false;
        TotemTaken?.Invoke(this);
    }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 c = transform.position;
        float dx = worldPosition.x - c.x;
        float dz = worldPosition.z - c.z;
        return dx * dx + dz * dz <= safeRadius * safeRadius && Mathf.Abs(worldPosition.y - c.y) <= safeHeight;
    }

    // ---------- IInteractable: totemi al ----------

    public string InteractionPrompt => "Totemi al";

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (taken.Value || interactor == null) return false;
        PlayerLife life = interactor.GetComponent<PlayerLife>();
        return life == null || life.CanAct;
    }

    public void Interact(PlayerInteractor interactor)
    {
        InventorySystem inventory = interactor.GetComponent<InventorySystem>();
        InventoryNetworkSync net = interactor.GetComponent<InventoryNetworkSync>();
        if (inventory == null || net == null || totemItem == null || !IsSpawned) return;

        if (!inventory.CanAdd(totemItem, out string reason))
        {
            inventory.ShowMessage(reason);
            return;
        }
        net.RequestTotemPickup(this, totemItem);
    }

    // ---------- Güvenli alan ve görsel ----------

    private void Update()
    {
        UpdateVisual();
        if (IsServer && IsSpawned && IsActive) ProtectPlayers();
    }

    private void ProtectPlayers()
    {
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive || !Contains(p.transform.position)) continue;
            PoisonSystem poison = p.GetComponent<PoisonSystem>();
            if (poison == null) continue;
            poison.ServerProtect();
            if (healPerSecond > 0f) poison.RemovePoison(healPerSecond * Time.deltaTime);
        }
    }

    private void UpdateVisual()
    {
        blend = Mathf.MoveTowards(blend, IsActive ? 1f : 0f, 1.5f * Time.deltaTime);

        if (glowRenderers != null)
        {
            Color c = glowColor * (glowIntensity * blend);
            foreach (Renderer r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(block);
            }
        }

        if (glowLight != null)
        {
            float n = (Mathf.PerlinNoise(Time.time * 2f, phase) - 0.5f) * 2f;
            glowLight.intensity = lightBaseIntensity * blend * (1f + n * flicker);
            glowLight.enabled = blend > 0.01f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
        const int steps = 64;
        Vector3 prev = transform.position + new Vector3(safeRadius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float ang = i * Mathf.PI * 2f / steps;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(ang) * safeRadius, 0f, Mathf.Sin(ang) * safeRadius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
