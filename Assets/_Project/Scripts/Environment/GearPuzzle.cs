using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-05c (KULLANICI 2026-11-14): Dişli bulmacası. Panoda sürekli dönen bir sürücü dişli, sabit milli üç boyutu değişen dişli
/// ve bir çıkış dişlisi vardır. Bir dişli ancak boyu, bir önceki dişliye TAM değecek kadarsa döner (küçükse değmez, büyükse sıkışır);
/// dönüş zincir hâlinde aktarılır. Üçü de doğru boyda olunca çıkış dişlisi döner, sunucu bulmacayı çözer, yatağın kilidini açar
/// ve dişlileri doğru boyda KİLİTLER (artık değiştirilemez). Doğru boylar çizili değildir.
/// Diş sayıları yarıçapla orantılıdır (aynı diş aralığı) ve açılar tek bir ana açıdan türetilir → doğru boyda dişler birbirine geçer.
/// Sahne içi NetworkObject.
/// </summary>
public class GearPuzzle : NetworkBehaviour
{
    [SerializeField] private ResizableObject[] gears = new ResizableObject[0];
    [Tooltip("Her dişlinin doğru boyu (0..1).")]
    [SerializeField] private float[] targets = new float[0];
    [SerializeField] private float tolerance = 0.05f;
    [SerializeField] private float holdSeconds = 1f;
    [SerializeField] private DreamBed bed;

    [Header("Zincir: sürücü, dişli 1..3, çıkış (5 eleman)")]
    [SerializeField] private Transform driver;
    [SerializeField] private Transform output;
    [Tooltip("Doğru (birbirine oturan) yarıçaplar: sürücü, 3 dişli, çıkış.")]
    [SerializeField] private float[] radii = new float[0];
    [SerializeField] private int[] teeth = new int[0];
    [Tooltip("Dişli çevre hızı (m/sn).")]
    [SerializeField] private float rimSpeed = 0.5f;

    private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>(false);
    private float held;
    private bool wasSolved;
    private float master;                 // sürücünün açısı (radyan)
    private float[] phase = new float[0]; // her zincir elemanının faz kayması (radyan)
    private float[] shown = new float[0]; // gösterilen açı (derece)
    private float nextClack;

    public bool IsSolved => IsSpawned && solved.Value;

    private void Awake() { BuildPhases(); }

    public override void OnNetworkSpawn() { wasSolved = solved.Value; }

    // Yan yana (x ekseni boyunca) dizili dişlilerin dişleri birbirine geçsin diye faz kaymaları.
    // Dişli ağı: yerel 0 açısı diş ARASI ortası, yarım adım sonrası diş ortası.
    private void BuildPhases()
    {
        int n = Mathf.Min(radii.Length, teeth.Length);
        phase = new float[n];
        shown = new float[n];
        for (int i = 1; i < n; i++)
        {
            float stepA = Mathf.PI * 2f / Mathf.Max(1, teeth[i - 1]);
            float stepB = Mathf.PI * 2f / Mathf.Max(1, teeth[i]);
            float rhoA = Mathf.Repeat(-phase[i - 1] / stepA, 1f);       // A'nın sağ temas noktasındaki diş fazı
            phase[i] = Mathf.PI - stepB * (0.5f - rhoA);                // B'nin sol temas noktasında tamamlayıcı faz
        }
        for (int i = 0; i < n; i++) shown[i] = phase[i] * Mathf.Rad2Deg;
    }

    private float ChainAngle(int i)
    {
        // komşular ters yöne döner; açısal hız yarıçapla ters orantılı
        float sign = i % 2 == 0 ? 1f : -1f;
        return (phase[i] + sign * master * radii[0] / Mathf.Max(0.05f, radii[i])) * Mathf.Rad2Deg;
    }

    private void Update()
    {
        int n = Mathf.Min(gears.Length, targets.Length);
        if (phase.Length < n + 2) return;
        float dt = Time.deltaTime;
        master += rimSpeed / Mathf.Max(0.05f, radii[0]) * dt;

        shown[0] = ChainAngle(0);
        if (driver != null) driver.localRotation = Quaternion.Euler(0f, 0f, shown[0]);

        bool s = IsSolved;
        bool chain = true, allOk = n > 0;
        for (int i = 0; i < n; i++)
        {
            ResizableObject g = gears[i];
            if (g == null) { allOk = false; chain = false; continue; }
            float err = g.Size01 - targets[i];
            bool fit = Mathf.Abs(err) <= tolerance;
            bool jam = err > tolerance;                                   // fazla büyük: komşusuna biniyor
            int c = i + 1;
            if (s || (chain && fit)) shown[c] = Mathf.MoveTowardsAngle(shown[c], ChainAngle(c), 720f * dt);
            float shake = !s && jam ? Mathf.Sin(Time.time * 38f + i) * 1.6f : 0f;   // sıkışan dişli titrer
            g.transform.localRotation = Quaternion.Euler(0f, 0f, shown[c] + shake);
            if (!fit) { allOk = false; chain = false; }
        }
        bool turning = s || allOk;
        int last = n + 1;
        if (turning) shown[last] = Mathf.MoveTowardsAngle(shown[last], ChainAngle(last), 720f * dt);
        if (output != null) output.localRotation = Quaternion.Euler(0f, 0f, shown[last]);

        if (IsSpawned && IsServer && !solved.Value)
        {
            held = allOk ? held + dt : 0f;
            if (held >= holdSeconds)
            {
                solved.Value = true;
                for (int i = 0; i < n; i++) if (gears[i] != null) gears[i].ServerLock(targets[i]);   // çözülünce boylar sabitlenir
                if (bed != null) bed.ServerUnlock();
            }
        }

        if (turning && Time.time >= nextClack && Sfx.Lib != null)
        {
            nextClack = Time.time + 0.35f;
            Sfx.At(DreamSfx.Tick, transform.position, 0.35f, 1f, 0.08f, 2f, 18f);
        }
        if (s && !wasSolved && Sfx.Lib != null) Sfx.At(DreamSfx.Chime, transform.position, 0.9f, 1.2f, 0.03f, 3f, 25f);
        wasSolved = s;
    }
}
