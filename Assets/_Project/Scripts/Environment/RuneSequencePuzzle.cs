using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-32 (GDD 6.9 "fener izleri"): Tahta direklerde yalnızca FENER IŞIĞINDA görünen çizgiler (1, 2, 3) var.
/// Direklere çizgi sayısı sırasıyla (1 → 2 → 3) dokunulursa bulmaca çözülür; yanlış direk ilerlemeyi sıfırlar.
/// İlerleme sunucuda (NetworkVariable). Ekranda yazı yok.
/// KULLANICI (cila): doğru/yanlış dokunuş herkese geri bildirim (ses + direk parıltısı / kırmızı titreme);
/// kafes kapısındaki rün tabelası (RuneGateSign) ilerlemeyi gösterir.
/// </summary>
public class RuneSequencePuzzle : NetworkBehaviour
{
    [SerializeField] private RunePillar[] pillars = new RunePillar[0];
    [SerializeField] private float maxUseDistance = 4f;

    private readonly NetworkVariable<int> progress = new NetworkVariable<int>(0);

    public int Progress => progress.Value;
    public int Count => pillars.Length;
    public bool Solved => pillars.Length > 0 && progress.Value >= pillars.Length;

    /// <summary>Tüm kopyalarda: yanlış dokunuş (ilerleme sıfırlandı).</summary>
    public event Action Wrong;

    private void Awake()
    {
        for (int i = 0; i < pillars.Length; i++) if (pillars[i] != null) pillars[i].Bind(this, i);
    }

    public void RequestTouch(int pillarIndex) { if (IsSpawned) TouchRpc(pillarIndex); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TouchRpc(int pillarIndex, RpcParams rpcParams = default)
    {
        if (Solved || pillarIndex < 0 || pillarIndex >= pillars.Length || pillars[pillarIndex] == null) return;
        PlayerLife p = null;
        foreach (PlayerLife pl in PlayerLife.All) if (pl != null && pl.OwnerClientId == rpcParams.Receive.SenderClientId) p = pl;
        if (p == null || !p.CanAct) return;
        if (Vector3.Distance(p.transform.position, pillars[pillarIndex].transform.position) > maxUseDistance) return;

        int order = pillars[pillarIndex].Order;
        if (order <= progress.Value) return;                 // zaten doğru basılmış direk
        if (order == progress.Value + 1)
        {
            progress.Value = order;
            FeedbackRpc(pillarIndex, true, Solved);
        }
        else
        {
            progress.Value = order == 1 ? 1 : 0;             // yanlış: sıfırla (1 ise yeni başlangıç)
            FeedbackRpc(pillarIndex, false, false);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void FeedbackRpc(int pillarIndex, bool correct, bool solved)
    {
        RunePillar pillar = pillarIndex >= 0 && pillarIndex < pillars.Length ? pillars[pillarIndex] : null;
        Vector3 pos = pillar != null ? pillar.transform.position + Vector3.up * 1.4f : transform.position;
        SfxLibrary lib = Sfx.Lib;
        if (correct)
        {
            if (lib != null) Sfx.At(lib.spearHit, pos, 0.55f, 1.5f, 0.03f, 2f, 20f);     // tok tahta "tık" (kapı sesi PuzzleDoor'da)
        }
        else
        {
            if (lib != null) Sfx.At(lib.rumble, pos, 0.5f, 1.6f, 0.04f, 2f, 20f);        // kısa, boğuk gürleme
            foreach (RunePillar pl in pillars) if (pl != null) pl.FlashWrong();
            Wrong?.Invoke();
        }
    }
}
