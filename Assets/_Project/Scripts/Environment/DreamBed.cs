using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05 / B3-06 (KULLANICI 2026-11-14): Rüya evindeki yatak. Başta KİLİTLİ (yatağı saran kuru kökler; köklerde nabız gibi atan tomurcuklar); bu yatağın bulmacası çözülünce
/// sunucu <see cref="ServerUnlock"/> ile açar, kafes çekilir. Açık yatağa E ile YATILIR (bir yatakta bir oyuncu), yine E ile kalkılır.
/// Yatan oyuncu hareket edemez (etrafa bakabilir). Hayattaki HERKES bir yatağa yatınca DreamWakeWatcher bölümü bitirir (uyanış).
/// Sahne içi NetworkObject.
/// </summary>
[DefaultExecutionOrder(230)] // yatış pozu: Animator, PlayerLife ve PlayerFreefall'dan (220) sonra
public class DreamBed : NetworkBehaviour, IInteractable
{
    public static readonly List<DreamBed> All = new List<DreamBed>();
    private const ulong None = ulong.MaxValue;

    [SerializeField] private Transform lockRoot;
    [SerializeField] private Renderer[] lockRenderers = new Renderer[0];
    [SerializeField, ColorUsage(false, true)] private Color lockGlow = new Color(1.4f, 0.35f, 0.9f);
    [SerializeField] private float maxUseDistance = 5f;

    private readonly NetworkVariable<bool> unlocked = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<ulong> occupant = new NetworkVariable<ulong>(None);
    private float shown;
    private bool wasUnlocked;
    private Vector3 lockScale = Vector3.one;
    private MaterialPropertyBlock block;

    // yatış pozu (her kopyada yerel)
    private PlayerLife posed;
    private Transform posedModel;
    private Vector3 modelPos;
    private Quaternion modelRot;
    private PlayerController lockedController;

    public bool IsUnlocked => IsSpawned && unlocked.Value;
    public bool IsOccupied => IsSpawned && occupant.Value != None;
    public ulong OccupantId => IsSpawned ? occupant.Value : None;

    private Vector3 LiePoint => transform.position + Vector3.up * 0.75f;
    private Vector3 StandPoint => transform.position + transform.right * 1.3f + Vector3.up * 1.1f;

    private void Awake()
    {
        if (lockRoot != null) lockScale = lockRoot.localScale;
        block = new MaterialPropertyBlock();
    }

    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); ReleasePose(); }

    public override void OnNetworkSpawn()
    {
        wasUnlocked = unlocked.Value;
        shown = unlocked.Value ? 1f : 0f;
    }

    /// <summary>Sunucu: yatağın kilidini açar (bu yatağın bulmacası çözülünce çağrılır).</summary>
    public void ServerUnlock()
    {
        if (IsServer && IsSpawned && !unlocked.Value) unlocked.Value = true;
    }

    // ---------- E: yat / kalk ----------
    private static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : None;

    public string InteractionPrompt => IsOccupied && occupant.Value == LocalId ? "Kalk" : "Yat";
    public bool CanInteract(PlayerInteractor interactor) => IsUnlocked && (!IsOccupied || occupant.Value == LocalId);
    public void Interact(PlayerInteractor interactor) { if (IsSpawned) ToggleRpc(); }

    private static PlayerLife FindPlayer(ulong clientId)
    {
        foreach (PlayerLife p in PlayerLife.All) if (p != null && p.OwnerClientId == clientId) return p;
        return null;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleRpc(RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        PlayerLife p = FindPlayer(id);
        if (p == null || !p.CanAct || !unlocked.Value) return;
        if (occupant.Value == id)
        {
            occupant.Value = None;
            p.ServerTeleport(StandPoint, transform.rotation);
            return;
        }
        if (occupant.Value != None) return;
        foreach (DreamBed b in All) if (b != null && b != this && b.OccupantId == id) return;   // zaten başka yatakta
        if (Vector3.Distance(p.transform.position, transform.position) > maxUseDistance) return;
        occupant.Value = id;
        p.ServerTeleport(LiePoint, transform.rotation);
    }

    private void Update()
    {
        // sunucu: yatan oyuncu öldüyse / bayıldıysa / ayrıldıysa yatak boşalır
        if (IsSpawned && IsServer && occupant.Value != None)
        {
            PlayerLife p = FindPlayer(occupant.Value);
            if (p == null || p.State != PlayerLifeState.Alive) occupant.Value = None;
        }

        bool u = IsUnlocked;
        if (u && !wasUnlocked && Sfx.Lib != null)
        {
            Sfx.At(DreamSfx.Chime, transform.position + Vector3.up, 0.9f, 1f, 0.02f, 3f, 25f);
            Sfx.At(DreamSfx.Chime, transform.position + Vector3.up, 0.7f, 1.5f, 0.02f, 3f, 25f);
        }
        wasUnlocked = u;

        shown = Mathf.MoveTowards(shown, u ? 1f : 0f, Time.deltaTime * 0.8f);
        float s = Mathf.SmoothStep(0f, 1f, shown);
        if (lockRoot != null)
        {
            // kökler yere doğru çekilip kaybolur (önce yassılaşır, sonra büzülür)
            float down = Mathf.Max(0.001f, 1f - s);
            float spread = Mathf.Max(0.001f, 1f - Mathf.SmoothStep(0.3f, 1f, s));
            lockRoot.localScale = new Vector3(lockScale.x * spread, lockScale.y * down * down, lockScale.z * spread);
            lockRoot.gameObject.SetActive(shown < 0.999f);
        }
        Color e = lockGlow * ((1f - s) * (0.8f + 0.2f * Mathf.Sin(Time.time * 2.4f)));
        foreach (Renderer r in lockRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(block);
        }
    }

    // yatan oyuncu: sahibinde hareket kilidi + her kopyada gövde yatağa uzanır
    private void LateUpdate()
    {
        PlayerLife p = IsOccupied ? FindPlayer(occupant.Value) : null;
        if (p != posed)
        {
            ReleasePose();
            posed = p;
            if (posed != null)
            {
                Animator anim = posed.GetComponentInChildren<Animator>(true);
                posedModel = anim != null ? anim.transform : null;
                if (posedModel != null) { modelPos = posedModel.localPosition; modelRot = posedModel.localRotation; }
                if (posed.IsOwner)
                {
                    lockedController = posed.GetComponent<PlayerController>();
                    if (lockedController != null) lockedController.ExternalMotion = true;
                }
            }
        }
        if (posed == null) return;
        if (posed.IsOwner) posed.transform.position = LiePoint;      // yatakta sabit dur (bakış serbest)
        if (posedModel != null)
        {
            // gövde sırtüstü: baş yastık tarafında (-Z), ayaklar ayak ucunda (+Z). Yatak dünyada sabit olduğu için dünya uzayında yerleştirilir.
            posedModel.rotation = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            posedModel.position = transform.position + transform.rotation * new Vector3(0f, 0.74f, 0.9f);
        }
    }

    private void ReleasePose()
    {
        if (posedModel != null) { posedModel.localPosition = modelPos; posedModel.localRotation = modelRot; }
        if (lockedController != null) lockedController.ExternalMotion = false;
        posed = null; posedModel = null; lockedController = null;
    }
}
