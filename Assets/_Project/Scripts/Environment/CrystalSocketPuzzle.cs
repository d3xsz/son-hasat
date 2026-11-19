using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B2-10 (KULLANICI, 2026-11-09): Labirent kristal yuvası bulmacası. Başlangıç mağarasındaki kristal parçaları,
/// labirentin çıkmaz sokaklarındaki 3 yuvaya fenerle görünen çizgi sırasıyla (1 → 2 → 3) takılmalı.
/// - Doğru sırada takılan parça yuvada kalır (oyuncunun envanterinden düşer).
/// - Yanlış sırada takılırsa yuvalar kırmızı titrer, o ana kadar takılmış parçalar + yanlış takılan parça yuvaların
///   önüne geri düşer (yerden tekrar alınır), sıra baştan.
/// - Üçü de doğru takılınca PuzzleDoor (labirent çıkışındaki taş kapı) açılır.
/// Sunucu yetkili; ilerleme NetworkVariable. Ekranda yazı yok.
/// </summary>
public class CrystalSocketPuzzle : NetworkBehaviour
{
    [SerializeField] private CrystalSocket[] sockets = new CrystalSocket[0];
    [SerializeField] private ItemData crystalItem;
    [SerializeField] private float maxUseDistance = 4f;

    // kaç yuva doğru sırayla doldu (order <= progress olan yuvalar dolu)
    private readonly NetworkVariable<int> progress = new NetworkVariable<int>(0);
    private readonly System.Collections.Generic.Dictionary<ulong, float> lastPlace = new System.Collections.Generic.Dictionary<ulong, float>();

    public ItemData CrystalItem => crystalItem;
    public int Progress => progress.Value;
    public bool Solved => sockets.Length > 0 && progress.Value >= sockets.Length;

    private void Awake()
    {
        for (int i = 0; i < sockets.Length; i++) if (sockets[i] != null) sockets[i].Bind(this, i);
    }

    public bool IsFilled(int socketIndex)
    {
        if (!IsSpawned || socketIndex < 0 || socketIndex >= sockets.Length || sockets[socketIndex] == null) return false;
        return sockets[socketIndex].Order <= progress.Value;
    }

    public void RequestPlace(int socketIndex) { if (IsSpawned) PlaceRpc(socketIndex); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlaceRpc(int socketIndex, RpcParams rpcParams = default)
    {
        if (Solved || socketIndex < 0 || socketIndex >= sockets.Length || sockets[socketIndex] == null) return;
        if (IsFilled(socketIndex)) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        // envanter senkronu gelmeden aynı parçayla iki kez takmayı engelle
        if (lastPlace.TryGetValue(sender, out float t) && Time.time - t < 0.8f) return;
        lastPlace[sender] = Time.time;
        PlayerLife p = null;
        foreach (PlayerLife pl in PlayerLife.All) if (pl != null && pl.OwnerClientId == sender) p = pl;
        if (p == null || !p.CanAct) return;
        if (Vector3.Distance(p.transform.position, sockets[socketIndex].transform.position) > maxUseDistance) return;
        var inv = p.GetComponent<InventoryNetworkSync>();
        if (inv == null || inv.ItemOf(inv.SelectedItemId) != crystalItem) return;

        inv.ServerTakeItem(crystalItem); // parça oyuncudan alınır (doğruysa yuvada kalır, yanlışsa geri düşer)
        int order = sockets[socketIndex].Order;
        if (order == progress.Value + 1)
        {
            progress.Value = order;
            FeedbackRpc(socketIndex, true, Solved);
        }
        else
        {
            // takılmış parçalar + bu parça geri düşer
            for (int i = 0; i < sockets.Length; i++)
                if (sockets[i] != null && sockets[i].Order <= progress.Value) EjectAt(sockets[i]);
            EjectAt(sockets[socketIndex]);
            progress.Value = 0;
            FeedbackRpc(socketIndex, false, false);
        }
    }

    private void EjectAt(CrystalSocket s)
    {
        if (crystalItem == null || crystalItem.WorldPrefab == null) return;
        Vector3 pos = s.transform.position + s.transform.forward * 0.9f + Vector3.up * 1.1f + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
        GameObject go = Instantiate(crystalItem.WorldPrefab, pos, Random.rotation);
        NetworkObject no = go.GetComponent<NetworkObject>();
        if (no == null) { Destroy(go); return; }
        no.Spawn(true);
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null) rb.AddForce(s.transform.forward * 1.5f + Vector3.up * 1.5f, ForceMode.VelocityChange);
    }

    [Rpc(SendTo.Everyone)]
    private void FeedbackRpc(int socketIndex, bool correct, bool solved)
    {
        CrystalSocket s = socketIndex >= 0 && socketIndex < sockets.Length ? sockets[socketIndex] : null;
        Vector3 pos = s != null ? s.transform.position + Vector3.up * 1.1f : transform.position;
        SfxLibrary lib = Sfx.Lib;
        if (lib == null) return;
        if (correct)
        {
            // kristal "tın"ı (damla sesini tiz çalarak) — ChargeableCrystal ile aynı dil
            Sfx.At(lib.drips, pos, 0.8f, 2.1f, 0.05f, 2f, 25f);
            Sfx.At(lib.drips, pos, 0.5f, 2.8f, 0.05f, 2f, 25f);
            if (solved) Sfx.At(lib.rumble, pos, 0.6f, 0.8f, 0.04f, 4f, 45f);
        }
        else
        {
            Sfx.At(lib.rumble, pos, 0.55f, 1.6f, 0.04f, 2f, 22f);
            Sfx.At(lib.rockHit, pos, 0.5f, 1.4f, 0.05f, 2f, 20f);
            foreach (CrystalSocket cs in sockets) if (cs != null) cs.FlashWrong();
        }
        Noise.Emit(pos, Noise.CrystalChime);
    }
}
