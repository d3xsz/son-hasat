using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05a (KULLANICI 2026-11-14; zorlaştırma turunda ÜÇ TUR oldu): Terazi bulmacası. Bir kefede taş, diğerinde boyutu değişen
/// kabak (ResizableObject) durur. Kabak taşla DENGEYE gelecek ara boyda kısa süre kalınca o tur biter; kefedeki taş değişir
/// (üç farklı ağırlık). Taşların görünen boyu ağırlığını GÖSTERMEZ (ipucu odadaki notta); denge kol izlenerek bulunur.
/// Üç tur da bitince sunucu bulmacayı çözer, kabağı sabitler ve bağlı yatağın kilidini açar (kalıcı).
/// Sahne içi NetworkObject.
/// </summary>
public class ScalePuzzle : NetworkBehaviour
{
    [SerializeField] private ResizableObject weight;
    [SerializeField] private DreamBed bed;

    [Header("Denge")]
    [Tooltip("Her turun dengeyi veren boyutu (0..1).")]
    [SerializeField] private float[] targets = { 0.62f };
    [SerializeField] private float tolerance = 0.04f;
    [Tooltip("Dengede bu kadar saniye kalınca tur biter.")]
    [SerializeField] private float holdSeconds = 1.2f;

    [Header("Görsel")]
    [Tooltip("Her turun taşı (yalnızca o turdaki görünür).")]
    [SerializeField] private GameObject[] stones = new GameObject[0];
    [SerializeField] private Transform beam;
    [SerializeField] private Transform panLeft;
    [SerializeField] private Transform panRight;
    [SerializeField] private float maxTilt = 20f;
    [SerializeField] private Renderer indicator;
    [Tooltip("Tur lambaları: biten her tur için biri yanar.")]
    [SerializeField] private Renderer[] roundLamps = new Renderer[0];
    [SerializeField, ColorUsage(false, true)] private Color idleColor = new Color(0.5f, 0.2f, 0.1f);
    [SerializeField, ColorUsage(false, true)] private Color solvedColor = new Color(0.4f, 2.2f, 0.7f);

    private readonly NetworkVariable<int> round = new NetworkVariable<int>(0);
    private float held;
    private float tilt;
    private int lastRound;
    private MaterialPropertyBlock block;

    public bool IsSolved => IsSpawned && round.Value >= targets.Length;

    private void Awake() { block = new MaterialPropertyBlock(); }

    public override void OnNetworkSpawn() { lastRound = round.Value; }

    private void Update()
    {
        int r = IsSpawned ? round.Value : 0;
        bool s = r >= targets.Length;
        int cur = Mathf.Clamp(r, 0, Mathf.Max(0, targets.Length - 1));
        float size = weight != null ? weight.Size01 : 0f;
        float error = targets.Length > 0 ? size - targets[cur] : 0f;

        if (IsSpawned && IsServer && !s)
        {
            if (Mathf.Abs(error) <= tolerance) held += Time.deltaTime;
            else held = 0f;
            if (held >= holdSeconds)
            {
                held = 0f;
                round.Value = r + 1;
                if (r + 1 >= targets.Length)
                {
                    if (weight != null) weight.ServerLock(targets[cur]);      // son turda kabak dengede sabitlenir
                    if (bed != null) bed.ServerUnlock();
                }
            }
        }

        if (r != lastRound && Sfx.Lib != null)
            Sfx.At(DreamSfx.Chime, transform.position + Vector3.up * 1.5f, 0.9f, s ? 1.7f : 1.25f, 0.03f, 3f, 25f);
        lastRound = r;

        for (int i = 0; i < stones.Length; i++)
            if (stones[i] != null && stones[i].activeSelf != (i == cur)) stones[i].SetActive(i == cur);

        // kabak ağırsa sağ kefe iner; tolerans içinde kol neredeyse düz durur
        float targetTilt = s ? 0f : Mathf.Clamp(error / Mathf.Max(0.01f, tolerance * 5f), -1f, 1f) * maxTilt;
        tilt = Mathf.Lerp(tilt, targetTilt, 1f - Mathf.Exp(-4f * Time.deltaTime));
        float sway = s ? 0f : Mathf.Sin(Time.time * 1.3f) * 0.6f;
        if (beam != null) beam.localRotation = Quaternion.Euler(0f, 0f, -(tilt + sway));
        if (panLeft != null) panLeft.localRotation = Quaternion.Euler(0f, 0f, tilt + sway);
        if (panRight != null) panRight.localRotation = Quaternion.Euler(0f, 0f, tilt + sway);

        if (indicator != null)
        {
            float near = 1f - Mathf.Clamp01(Mathf.Abs(error) / Mathf.Max(0.01f, tolerance * 3f));
            SetGlow(indicator, s ? solvedColor : Color.Lerp(idleColor, solvedColor * 0.5f, near * near));
        }
        for (int i = 0; i < roundLamps.Length; i++)
            if (roundLamps[i] != null) SetGlow(roundLamps[i], i < r ? solvedColor : idleColor * 0.3f);
    }

    private void SetGlow(Renderer rend, Color c)
    {
        rend.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", c);
        rend.SetPropertyBlock(block);
    }
}
