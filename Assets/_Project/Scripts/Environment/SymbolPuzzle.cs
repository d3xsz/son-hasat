using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05b (KULLANICI 2026-11-14: yüksekteki anahtarın yerine): Resimli şifre. Yatağın yanındaki panoda E ile çevrilen simge tekerleri
/// vardır. Doğru simgeler odada DEĞİL: köydeki üç evin kapısına kazınmıştır (altındaki çentik sayısı = kaçıncı teker). Fenerle bulunur,
/// akılda tutulur ya da dışarıdaki arkadaş söyler. Doğru dizilim girilince sunucu bulmacayı çözer, tekerler kilitlenir,
/// yatağın kilidi açılır (kalıcı). Sahne içi NetworkObject.
/// </summary>
public class SymbolPuzzle : NetworkBehaviour
{
    [SerializeField] private DreamBed bed;
    [Tooltip("Her tekerin doğru simge sırası.")]
    [SerializeField] private int[] solution = new int[0];
    [SerializeField] private Renderer[] wheels = new Renderer[0];
    [Tooltip("Simge malzemeleri (teker bu malzemelerden birini gösterir).")]
    [SerializeField] private Material[] symbols = new Material[0];
    [SerializeField] private Renderer indicator;
    [SerializeField, ColorUsage(false, true)] private Color idleColor = new Color(0.5f, 0.2f, 0.1f);
    [SerializeField, ColorUsage(false, true)] private Color solvedColor = new Color(0.4f, 2.2f, 0.7f);
    [SerializeField] private float maxUseDistance = 5f;

    // tekerlerin durumu tek sayıda paketli: teker i = (state / symbolCount^i) % symbolCount
    private readonly NetworkVariable<int> state = new NetworkVariable<int>(0);
    private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>(false);
    private int shownState = -1;
    private bool wasSolved;
    private float[] spin = new float[0];
    private Quaternion[] rest = new Quaternion[0];
    private MaterialPropertyBlock block;

    public bool IsSolved => IsSpawned && solved.Value;
    public bool CanTurn => IsSpawned && !solved.Value;

    private int Count => Mathf.Max(1, symbols.Length);

    private int WheelValue(int packed, int index)
    {
        for (int i = 0; i < index; i++) packed /= Count;
        return packed % Count;
    }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        spin = new float[wheels.Length];
        rest = new Quaternion[wheels.Length];
        for (int i = 0; i < wheels.Length; i++) if (wheels[i] != null) rest[i] = wheels[i].transform.localRotation;
    }

    public override void OnNetworkSpawn() { wasSolved = solved.Value; }

    public void RequestTurn(int index) { if (IsSpawned) TurnRpc(index); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TurnRpc(int index, RpcParams rpcParams = default)
    {
        if (solved.Value || index < 0 || index >= wheels.Length || index >= solution.Length) return;
        bool near = false;
        foreach (PlayerLife p in PlayerLife.All)
            if (p != null && p.OwnerClientId == rpcParams.Receive.SenderClientId)
                near = p.CanAct && Vector3.Distance(p.transform.position, transform.position) <= maxUseDistance;
        if (!near) return;

        int mul = 1;
        for (int i = 0; i < index; i++) mul *= Count;
        int cur = WheelValue(state.Value, index);
        state.Value += ((cur + 1) % Count - cur) * mul;

        bool ok = true;
        for (int i = 0; i < solution.Length && i < wheels.Length; i++) if (WheelValue(state.Value, i) != solution[i]) ok = false;
        if (ok)
        {
            solved.Value = true;
            if (bed != null) bed.ServerUnlock();
        }
    }

    private void Update()
    {
        int st = IsSpawned ? state.Value : 0;
        if (st != shownState)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null || symbols.Length == 0) continue;
                int v = WheelValue(st, i);
                if (shownState >= 0 && WheelValue(shownState, i) != v)
                {
                    spin[i] = 1f;      // değişen teker bir tur döner
                    if (Sfx.Lib != null) Sfx.At(DreamSfx.Chime, wheels[i].transform.position, 0.5f, 1.3f, 0.05f, 2f, 15f);
                }
                wheels[i].sharedMaterial = symbols[v];
            }
            shownState = st;
        }
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            spin[i] = Mathf.MoveTowards(spin[i], 0f, Time.deltaTime * 3.5f);
            wheels[i].transform.localRotation = rest[i] * Quaternion.Euler(0f, 0f, 360f * Mathf.SmoothStep(0f, 1f, spin[i]));
        }

        bool s = IsSolved;
        if (s && !wasSolved && Sfx.Lib != null) Sfx.At(DreamSfx.Chime, transform.position, 0.9f, 1.7f, 0.03f, 3f, 25f);
        wasSolved = s;
        if (indicator != null)
        {
            indicator.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", s ? solvedColor : idleColor);
            indicator.SetPropertyBlock(block);
        }
    }
}
