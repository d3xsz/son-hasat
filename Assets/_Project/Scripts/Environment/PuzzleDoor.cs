using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-32 (taslak, GDD 6.9): Bulmaca kapısı. Bağlı koşulların HEPSİ sağlanınca sunucu kapıyı açar (kalıcı):
/// - levers: iki kollu kapı: tüm kollar AYNI ANDA çekili olmalı (her kol çekilince kısa süre aşağıda kalır),
/// - runePuzzle: fener izleri sıra bulmacası.
/// Kapı paneli her kopyada yerel olarak yukarı kayar (çarpışmasıyla birlikte). Ekranda yazı yok.
/// </summary>
public class PuzzleDoor : NetworkBehaviour
{
    [Header("Panel")]
    [SerializeField] private Transform panel;
    [SerializeField] private float openHeight = 3.4f;
    [SerializeField] private float openSpeed = 1.2f;

    [Header("Koşullar")]
    [SerializeField] private PuzzleLever[] levers = new PuzzleLever[0];
    [Tooltip("Kol çekildikten sonra aşağıda kalma süresi (sn). Kollar arası mesafe ile birlikte tek oyuncunun yetişemeyeceği şekilde ayarlanır.")]
    [SerializeField] private float leverHoldSeconds = 1.5f;
    [SerializeField] private float maxUseDistance = 4f;
    [SerializeField] private RuneSequencePuzzle runePuzzle;
    [Tooltip("B2-10: labirent kristal yuvası bulmacası.")]
    [SerializeField] private CrystalSocketPuzzle socketPuzzle;
    [Tooltip("Taş kapı: tahta gıcırtısı yerine taş sürtünme/gürleme sesi.")]
    [SerializeField] private bool stoneDoor;
    [Tooltip("B2-11: kapalıyken yaratığın yolunu kesen NavMeshObstacle (carve); açılınca kapanır.")]
    [SerializeField] private UnityEngine.AI.NavMeshObstacle navBlocker;

    [Header("Menteşe (sandık kapağı vb.)")]
    [Tooltip("Sıfırdan farklıysa panel yukarı kaymaz, bu açıyla (yerel euler) döner.")]
    [SerializeField] private Vector3 openRotation = Vector3.zero;

    private readonly NetworkVariable<bool> open = new NetworkVariable<bool>(false);
    private NetworkList<double> leverDownUntil;
    private Vector3 panelClosedLocal;
    private Quaternion panelClosedRot;
    private float openBlend;

    public bool IsOpen => open.Value;

    private void Awake()
    {
        leverDownUntil = new NetworkList<double>();
        if (panel != null) { panelClosedLocal = panel.localPosition; panelClosedRot = panel.localRotation; }
        for (int i = 0; i < levers.Length; i++) if (levers[i] != null) levers[i].Bind(this, i);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            while (leverDownUntil.Count < levers.Length) leverDownUntil.Add(0);
        openBlend = open.Value ? 1f : 0f;
        open.OnValueChanged += OnOpenChanged;
    }

    public override void OnNetworkDespawn() { open.OnValueChanged -= OnOpenChanged; }

    /// <summary>KULLANICI (cila): açılırken önce sürgü/kilit, ardından tahta gıcırtısı (herkes duyar).</summary>
    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        if (!newValue || oldValue || Sfx.Lib == null) return;
        Vector3 pos = panel != null ? panel.position + Vector3.up * 1.5f : transform.position;
        if (stoneDoor)
        {
            Sfx.At(Sfx.Lib.rumble, pos, 0.9f, 0.7f, 0.03f, 3f, 40f);
            Sfx.At(Sfx.Lib.rockHit, pos, 0.6f, 0.6f, 0.05f, 3f, 30f);
            return;
        }
        Sfx.At(Sfx.Lib.doorUnlock, pos, 0.8f, 1f, 0.03f, 2f, 25f);
        StartCoroutine(CreakAfter(0.45f, pos));
    }

    private System.Collections.IEnumerator CreakAfter(float delay, Vector3 pos)
    {
        yield return new WaitForSeconds(delay);
        Sfx.At(Sfx.Lib.doorCreak, pos, 0.85f, 0.92f, 0.04f, 2f, 28f);
    }

    public bool IsLeverDown(int index)
    {
        if (!IsSpawned || index < 0 || index >= leverDownUntil.Count) return false;
        return NetworkManager.ServerTime.Time < leverDownUntil[index];
    }

    /// <summary>Yerel oyuncu kolu çeker (PuzzleLever.Interact).</summary>
    public void RequestLever(int index) { if (IsSpawned) LeverRpc(index); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void LeverRpc(int index, RpcParams rpcParams = default)
    {
        if (index < 0 || index >= levers.Length || index >= leverDownUntil.Count || levers[index] == null) return;
        PlayerLife p = FindPlayer(rpcParams.Receive.SenderClientId);
        if (p == null || !p.CanAct) return;
        if (Vector3.Distance(p.transform.position, levers[index].transform.position) > maxUseDistance) return;
        leverDownUntil[index] = NetworkManager.ServerTime.Time + leverHoldSeconds;
    }

    private static PlayerLife FindPlayer(ulong clientId)
    {
        foreach (PlayerLife p in PlayerLife.All) if (p != null && p.OwnerClientId == clientId) return p;
        return null;
    }

    private bool ConditionsMet()
    {
        bool any = false;
        if (levers.Length > 0)
        {
            any = true;
            for (int i = 0; i < levers.Length; i++) if (!IsLeverDown(i)) return false;
        }
        if (runePuzzle != null)
        {
            any = true;
            if (!runePuzzle.Solved) return false;
        }
        if (socketPuzzle != null)
        {
            any = true;
            if (!socketPuzzle.Solved) return false;
        }
        return any;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && !open.Value && ConditionsMet()) open.Value = true;

        if (navBlocker != null && navBlocker.enabled == open.Value) navBlocker.enabled = !open.Value;
        openBlend = Mathf.MoveTowards(openBlend, open.Value ? 1f : 0f, Time.deltaTime * openSpeed / Mathf.Max(0.1f, openHeight));
        if (panel == null) return;
        float k = Mathf.SmoothStep(0f, 1f, openBlend);
        if (openRotation != Vector3.zero) panel.localRotation = panelClosedRot * Quaternion.Euler(openRotation * k);
        else panel.localPosition = panelClosedLocal + Vector3.up * (openHeight * k);
    }
}
