using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B2-05 (GDD 7.1): Işık yansıtma bulmacası. Bir oyuncu fenerini KAYNAK kristale tuttuğu sürece kristalden bir ışın çıkar.
/// YÖNLENDİRİCİ kristaller (BeamMirror, E ile 45° döner) kendilerine çarpan ışını uçlarının baktığı yöne gönderir.
/// Işın kapıdaki RÜNE kesintisiz holdSeconds değince kapı kalıcı açılır (yere gömülür).
/// Durum SUNUCUDA: kaynak etkin mi, kristallerin yönü (adım 0-7), kapı açık mı. Işının yolu her kopyada aynı girdilerle
/// (sabit sahne + senkron yönler) yerel hesaplanıp çizilir; kapı kararını sunucu verir. Oyuncular ışını kesmez. Yazı yok.
/// </summary>
public class BeamPuzzle : NetworkBehaviour
{
    [Header("Parçalar")]
    [SerializeField] private Transform source;
    [Tooltip("Kaynaktan çıkan ışının yönü (dünya, yatay).")]
    [SerializeField] private Vector3 sourceDirection = Vector3.right;
    [SerializeField] private BeamMirror[] mirrors = new BeamMirror[0];
    [SerializeField] private Collider target;
    [SerializeField] private Transform doorPanel;
    [SerializeField] private Renderer[] runeRenderers = new Renderer[0];
    [SerializeField] private Renderer[] sourceRenderers = new Renderer[0];
    [SerializeField] private LineRenderer beam;
    [SerializeField] private Light hitLight;

    [Header("Ayar")]
    [SerializeField] private float beamHeight = 1.25f;
    [SerializeField] private float maxSegment = 45f;
    [SerializeField] private int maxBounces = 8;
    [SerializeField] private float lightRange = 16f;
    [SerializeField] private float lightHalfAngle = 26f;
    [Tooltip("Fener çekildikten sonra ışının sönmesi için geçen süre (sn).")]
    [SerializeField] private float sourceGrace = 0.5f;
    [SerializeField] private float holdSeconds = 1.5f;
    [SerializeField] private float doorSink = 5f;
    [SerializeField] private float doorSeconds = 3.5f;
    [SerializeField, ColorUsage(false, true)] private Color glow = new Color(0.3f, 1.2f, 1.5f);
    [SerializeField, ColorUsage(false, true)] private Color runeDim = new Color(0.04f, 0.1f, 0.12f);

    private NetworkList<int> steps;
    private readonly NetworkVariable<bool> sourceActive = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<bool> open = new NetworkVariable<bool>(false);

    private float graceTimer, holdTimer, doorT, shownRune;
    private Vector3 doorClosed;
    private MaterialPropertyBlock block;
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<BeamMirror> litMirrors = new List<BeamMirror>();

    public bool IsOpen => open.Value;

    private void Awake()
    {
        steps = new NetworkList<int>();
        block = new MaterialPropertyBlock();
        if (doorPanel != null) doorClosed = doorPanel.localPosition;
        for (int i = 0; i < mirrors.Length; i++) if (mirrors[i] != null) mirrors[i].Bind(this, i);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            for (int i = steps.Count; i < mirrors.Length; i++) steps.Add(mirrors[i] != null ? mirrors[i].StartStep : 0);
        doorT = open.Value ? 1f : 0f;
        open.OnValueChanged += OnOpenChanged;
    }

    public override void OnNetworkDespawn() { open.OnValueChanged -= OnOpenChanged; }

    public int StepOf(int index) => IsSpawned && index >= 0 && index < steps.Count ? steps[index] : (mirrors[index] != null ? mirrors[index].StartStep : 0);

    public void RequestRotate(int index) { if (IsSpawned) RotateRpc(index); }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RotateRpc(int index, RpcParams rpcParams = default)
    {
        if (open.Value || index < 0 || index >= steps.Count || mirrors[index] == null) return;
        PlayerLife p = null;
        foreach (PlayerLife pl in PlayerLife.All) if (pl != null && pl.OwnerClientId == rpcParams.Receive.SenderClientId) p = pl;
        if (p == null || !p.CanAct || Vector3.Distance(p.transform.position, mirrors[index].transform.position) > 4f) return;
        steps[index] = (steps[index] + 1) % 8;
        RotatedRpc(index);
    }

    [Rpc(SendTo.Everyone)]
    private void RotatedRpc(int index)
    {
        if (Sfx.Lib != null && mirrors[index] != null) Sfx.At(Sfx.Lib.rockHit, mirrors[index].transform.position + Vector3.up, 0.5f, 1.6f, 0.06f, 2f, 18f); // taş "tık"
    }

    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        if (!newValue || oldValue || Sfx.Lib == null || doorPanel == null) return;
        Vector3 p = doorPanel.position;
        Sfx.At(Sfx.Lib.totemTake, p, 1f, 0.7f, 0.02f, 4f, 40f);   // taş kapı sürtünmesi
        Sfx.At(Sfx.Lib.rumble, p, 0.8f, 0.8f, 0.02f, 4f, 40f);
        Sfx.At(Sfx.Lib.drips, p, 0.6f, 2.2f, 0.02f, 4f, 30f);      // rün "tın"ı
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (IsSpawned && IsServer) ServerTick(dt);

        bool active = IsSpawned && sourceActive.Value;
        bool hitTarget = active && TracePath();
        DrawBeam(active);
        // ışının değdiği yönlendiriciler parlar
        for (int i = 0; i < mirrors.Length; i++)
            if (mirrors[i] != null) mirrors[i].SetLit(active && litMirrors.Contains(mirrors[i]));
        UpdateHum(active, dt);

        // kaynak ve rün parıltısı
        SetEmission(sourceRenderers, active ? glow * 1.4f : glow * 0.12f);
        holdTimerShown = hitTarget ? Mathf.Min(1f, holdTimerShown + dt / Mathf.Max(0.1f, holdSeconds)) : Mathf.Max(0f, holdTimerShown - dt);
        shownRune = Mathf.MoveTowards(shownRune, open.Value ? 1f : holdTimerShown, dt * 3f);
        SetEmission(runeRenderers, Color.Lerp(runeDim, glow * 2f, shownRune));

        // kapı: yere gömülür
        doorT = Mathf.MoveTowards(doorT, open.Value ? 1f : 0f, dt / Mathf.Max(0.1f, doorSeconds));
        if (doorPanel != null)
        {
            float k = Mathf.SmoothStep(0f, 1f, doorT);
            float shake = doorT > 0f && doorT < 1f ? Mathf.Sin(Time.time * 45f) * 0.02f : 0f;
            doorPanel.localPosition = doorClosed + Vector3.down * (doorSink * k) + Vector3.right * shake;
        }
    }

    private float holdTimerShown;
    private AudioSource hum;

    /// <summary>B2-07: ışın açıkken kaynak kristalden ince, titreşen uğultu (3B).</summary>
    private void UpdateHum(bool active, float dt)
    {
        if (hum == null)
        {
            if (source == null) return;
            hum = source.gameObject.AddComponent<AudioSource>();
            hum.clip = PlaceholderSounds.CreateDroneLoop();
            hum.loop = true; hum.spatialBlend = 1f; hum.minDistance = 2f; hum.maxDistance = 28f; hum.rolloffMode = AudioRolloffMode.Logarithmic;
            hum.pitch = 2.2f; hum.volume = 0f; hum.playOnAwake = false; hum.dopplerLevel = 0f;
            hum.Play();
        }
        hum.volume = Mathf.MoveTowards(hum.volume, active ? 0.35f : 0f, dt * 1.5f);
        hum.pitch = 2.2f + (active ? 0.15f * Mathf.Sin(Time.time * 3f) : 0f) + holdTimerShown * 0.6f; // rün dolarken yükselir
    }

    private void ServerTick(float dt)
    {
        if (open.Value) { if (sourceActive.Value) sourceActive.Value = false; return; }
        bool lit = source != null && FlashlightReveal.IsLit(source.position + Vector3.up * beamHeight, lightRange, lightHalfAngle, source);
        graceTimer = lit ? sourceGrace : Mathf.Max(0f, graceTimer - dt);
        bool active = graceTimer > 0f;
        if (sourceActive.Value != active) sourceActive.Value = active;

        bool hit = active && TracePath();
        holdTimer = hit ? holdTimer + dt : 0f;
        if (holdTimer >= holdSeconds) open.Value = true;
    }

    /// <summary>Işın yolunu hesaplar (points'e yazar); hedefe ulaşırsa true.</summary>
    private bool TracePath()
    {
        points.Clear();
        litMirrors.Clear();
        if (source == null) return false;
        Vector3 pos = source.position + Vector3.up * beamHeight;
        Vector3 dir = sourceDirection; dir.y = 0f; dir.Normalize();
        points.Add(pos);
        Collider ignore = null;
        for (int b = 0; b <= maxBounces; b++)
        {
            RaycastHit[] hits = Physics.RaycastAll(pos, dir, maxSegment, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            bool stopped = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == ignore) continue;
                if (h.collider.GetComponentInParent<PlayerLife>() != null) continue; // oyuncular ışını kesmez
                if (h.collider.isTrigger && h.collider != target) continue;
                if (target != null && h.collider == target) { points.Add(h.point); return true; }
                BeamMirror m = h.collider.GetComponentInParent<BeamMirror>();
                if (m != null && m.Owner == this)
                {
                    Vector3 c = m.transform.position + Vector3.up * beamHeight;
                    points.Add(c);
                    litMirrors.Add(m);
                    pos = c; dir = m.DirectionForStep(StepOf(m.Index)); ignore = h.collider;
                    stopped = true;
                    break;
                }
                points.Add(h.point); // duvar/kaya: ışın burada biter
                return false;
            }
            if (!stopped) { points.Add(pos + dir * maxSegment); return false; }
        }
        return false;
    }

    private void DrawBeam(bool active)
    {
        if (beam == null) return;
        beam.enabled = active && points.Count >= 2;
        if (!beam.enabled) { if (hitLight != null) hitLight.enabled = false; return; }
        beam.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) beam.SetPosition(i, points[i]);
        float w = 0.11f + 0.025f * Mathf.Sin(Time.time * 9f);
        beam.widthMultiplier = w;
        if (hitLight != null) { hitLight.enabled = true; hitLight.transform.position = points[points.Count - 1] - (points[points.Count - 1] - points[points.Count - 2]).normalized * 0.3f; }
    }

    private void SetEmission(Renderer[] rs, Color c)
    {
        foreach (Renderer r in rs)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
