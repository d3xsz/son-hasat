using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05b (KULLANICI 2026-11-14): Yüksekteki anahtar bulmacası. Anahtar tavana yakın asılıdır; yerden erişilemez (etkileşim menzili).
/// Oyuncu mantar basamakları büyütüp tırmanır ve anahtarı E ile alır → sunucu bulmacayı çözer, anahtar yatağın kilidine uçar,
/// yatağın kilidi açılır (kalıcı). Anahtar envantere girmez (yeni eşya türü eklenmedi). Sahne içi NetworkObject; anahtarın üstünde durur.
/// </summary>
public class HighKeyPuzzle : NetworkBehaviour, IInteractable
{
    [SerializeField] private DreamBed bed;
    [SerializeField] private Transform keyVisual;
    [SerializeField] private Renderer[] glowRenderers = new Renderer[0];
    [SerializeField, ColorUsage(false, true)] private Color glow = new Color(1.8f, 1.3f, 0.35f);
    [SerializeField] private float maxUseDistance = 4f;
    [SerializeField] private float flySeconds = 1.6f;

    private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>(false);
    private Vector3 homePos;
    private Quaternion homeRot;
    private float fly;
    private bool wasSolved;
    private float pendingUnlock = -1f;
    private MaterialPropertyBlock block;
    private Collider col;

    public bool IsSolved => IsSpawned && solved.Value;

    public string InteractionPrompt => "Anahtarı al";
    public bool CanInteract(PlayerInteractor interactor) => IsSpawned && !solved.Value;
    public void Interact(PlayerInteractor interactor) { if (IsSpawned) TakeRpc(); }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        col = GetComponent<Collider>();
        if (keyVisual != null) { homePos = keyVisual.position; homeRot = keyVisual.rotation; }
    }

    public override void OnNetworkSpawn()
    {
        wasSolved = solved.Value;
        if (solved.Value) fly = 1f;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TakeRpc(RpcParams rpcParams = default)
    {
        if (solved.Value) return;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.OwnerClientId != rpcParams.Receive.SenderClientId || !p.CanAct) continue;
            if (Vector3.Distance(p.transform.position, transform.position) > maxUseDistance) return;
            solved.Value = true;
            pendingUnlock = Time.time + flySeconds;      // kilit, anahtar yatağa varınca açılır
            return;
        }
    }

    private void Update()
    {
        bool s = IsSolved;
        if (IsSpawned && IsServer && pendingUnlock >= 0f && Time.time >= pendingUnlock)
        {
            pendingUnlock = -1f;
            if (bed != null) bed.ServerUnlock();
        }
        if (s && !wasSolved && Sfx.Lib != null)
            Sfx.At(Sfx.Lib.drips, transform.position, 0.9f, 2.3f, 0.03f, 3f, 25f);
        wasSolved = s;
        if (col != null) col.enabled = !s;

        if (keyVisual != null)
        {
            if (!s)
            {
                // asılı dururken hafifçe sallanır ve döner
                keyVisual.position = homePos + Vector3.up * (Mathf.Sin(Time.time * 1.6f) * 0.06f);
                keyVisual.rotation = homeRot * Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.9f) * 35f, 0f);
            }
            else
            {
                fly = Mathf.MoveTowards(fly, 1f, Time.deltaTime / Mathf.Max(0.1f, flySeconds));
                float k = Mathf.SmoothStep(0f, 1f, fly);
                Vector3 target = bed != null ? bed.transform.position + Vector3.up * 1.2f : homePos;
                Vector3 p = Vector3.Lerp(homePos, target, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.2f);
                keyVisual.position = p;
                keyVisual.rotation = homeRot * Quaternion.Euler(0f, k * 720f, 0f);
                keyVisual.gameObject.SetActive(fly < 0.999f);
            }
        }

        Color e = glow * (s ? 1.6f : 0.8f + 0.25f * Mathf.Sin(Time.time * 2.2f));
        foreach (Renderer r in glowRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", e);
            r.SetPropertyBlock(block);
        }
    }
}
