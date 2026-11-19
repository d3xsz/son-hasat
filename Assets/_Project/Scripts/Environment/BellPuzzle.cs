using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05d (KULLANICI 2026-11-14): Çan bulmacası (kulakla çözülür). Üç çanın boyu boyut ışığıyla değişir; çan büyüdükçe sesi kalınlaşır.
/// Müzik kutusu (E) üç notalık ezgiyi çalar. Oyuncu çanları E ile çalarak sesini dinler, boylarını ezgideki notalara göre ayarlar ve
/// çanları SOLDAN SAĞA sırayla çalar: üç çan da doğru sesteyse sunucu bulmacayı çözer, çanları kilitler, yatağın kilidini açar (kalıcı).
/// Sahne içi NetworkObject.
/// </summary>
public class BellPuzzle : NetworkBehaviour
{
    [SerializeField] private ResizableObject[] bells = new ResizableObject[0];
    [Tooltip("Her çanın doğru boyu (0 = en küçük/ince, 1 = en büyük/kalın).")]
    [SerializeField] private float[] targets = new float[0];
    [SerializeField] private float tolerance = 0.11f;
    [SerializeField] private DreamBed bed;
    [SerializeField] private Transform musicBox;
    [SerializeField] private float highPitch = 1.7f;
    [SerializeField] private float lowPitch = 0.6f;
    [SerializeField] private float maxUseDistance = 5f;

    private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>(false);
    private int progress;                 // sunucu: soldan sağa kaç çan doğru çalındı
    private float[] swing = new float[0];
    private bool melodyPlaying;

    public bool IsSolved => IsSpawned && solved.Value;
    public bool CanUse => IsSpawned;

    private float PitchFor(float size01) => Mathf.Lerp(highPitch, lowPitch, Mathf.Clamp01(size01));

    public void RequestRing(int index) { if (IsSpawned) RingRpc(index); }
    public void RequestMelody() { if (IsSpawned) MelodyRpc(); }

    private bool SenderNear(ulong clientId, Vector3 point)
    {
        foreach (PlayerLife p in PlayerLife.All)
            if (p != null && p.OwnerClientId == clientId) return p.CanAct && Vector3.Distance(p.transform.position, point) <= maxUseDistance;
        return false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RingRpc(int index, RpcParams rpcParams = default)
    {
        if (index < 0 || index >= bells.Length || index >= targets.Length || bells[index] == null) return;
        if (!SenderNear(rpcParams.Receive.SenderClientId, bells[index].transform.position)) return;
        float size = bells[index].Size01;
        NoteRpc(index, size);
        if (solved.Value) return;

        // doğru sırayla (0, 1, 2) ve doğru sesle çalınan çanlar sayılır; yanlışta baştan
        bool right = Mathf.Abs(size - targets[index]) <= tolerance;
        if (index == progress && right) progress++;
        else progress = index == 0 && right ? 1 : 0;
        if (progress >= Mathf.Min(bells.Length, targets.Length))
        {
            solved.Value = true;
            for (int i = 0; i < bells.Length && i < targets.Length; i++) if (bells[i] != null) bells[i].ServerLock(bells[i].Size01);
            if (bed != null) bed.ServerUnlock();
            ChimeRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void MelodyRpc(RpcParams rpcParams = default)
    {
        if (musicBox != null && !SenderNear(rpcParams.Receive.SenderClientId, musicBox.position)) return;
        PlayMelodyRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void NoteRpc(int index, float size01)
    {
        if (index < 0 || index >= bells.Length || bells[index] == null) return;
        if (swing.Length != bells.Length) swing = new float[bells.Length];
        swing[index] = 1f;
        Play(bells[index].transform.position, PitchFor(size01), 0.9f);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayMelodyRpc() { if (!melodyPlaying) StartCoroutine(Melody(false)); }

    [Rpc(SendTo.Everyone)]
    private void ChimeRpc() { StartCoroutine(Melody(true)); }

    private IEnumerator Melody(bool fast)
    {
        melodyPlaying = true;
        // ezgi müzik kutusunda (köy meydanı) çalar; çözülünce çalan kutlama ise çanların yanında
        Vector3 pos = !fast && musicBox != null ? musicBox.position : transform.position;
        if (fast) yield return new WaitForSeconds(0.5f);
        for (int rep = 0; rep < (fast ? 2 : 1); rep++)
            for (int i = 0; i < targets.Length; i++)
            {
                Play(pos, PitchFor(targets[i]), 0.8f);
                yield return new WaitForSeconds(fast ? 0.22f : 0.75f);
            }
        melodyPlaying = false;
    }

    private static void Play(Vector3 pos, float pitch, float volume)
    {
        // üretilmiş çan sesi: tını sabit, yalnızca perde değişir
        Sfx.At(DreamSfx.Chime, pos, volume, pitch, 0f, 3f, 30f);
    }

    private void Update()
    {
        // çalınan çan sallanır (yerel görsel; ResizableObject yalnızca ölçeği yazar)
        if (swing.Length != bells.Length) swing = new float[bells.Length];
        for (int i = 0; i < bells.Length; i++)
        {
            if (bells[i] == null) continue;
            swing[i] = Mathf.MoveTowards(swing[i], 0f, Time.deltaTime * 0.7f);
            float a = Mathf.Sin(Time.time * 9f) * 16f * swing[i] * swing[i];
            bells[i].transform.localRotation = Quaternion.Euler(a, 0f, 0f);
        }
    }
}
