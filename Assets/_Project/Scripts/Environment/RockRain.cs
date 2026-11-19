using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// KULLANICI: 5. (son) totem alındıktan sonra tüm ormana durmadan taş yağar; mağara girişinin çevresi dışında güvenli yer kalmaz.
/// - Sunucu: her korunmasız oyuncunun çevresine aralıklarla taş düşürür (bir kısmı oyuncunun gittiği yere), düşüş süresi sonunda
///   isabet alanındaki oyunculara hasar (zehir) ekler. Görsel tüm istemcilere Rpc ile gider (yerel, ağ nesnesi değil).
/// - İstemci: yerde büyüyen gölge uyarısı, yukarıdan hızlanarak düşen kaya, çarpmada toz + kırıntı, yakındaysa kamera sarsıntısı.
/// - Etkinken WeatherController.ForceStorm açık: yağmur/rüzgâr en şiddetli seviyede kalır.
/// Weather nesnesindeki NetworkObject'i kullanır (ayrı sahne ağ nesnesi gerekmez).
/// </summary>
public class RockRain : NetworkBehaviour
{
    [Header("Güvenli bölge (mağara ağzı)")]
    [SerializeField] private Transform safeCenter;
    [Tooltip("0 = mağara ağzı da korunmaz (KULLANICI: ağzın önüne de taş düşsün); yalnızca yer altı güvenli.")]
    [SerializeField] private float safeRadius = 0f;
    [SerializeField] private float undergroundY = -8f;

    [Header("Yağış")]
    [SerializeField] private float intervalMin = 0.7f;
    [SerializeField] private float intervalMax = 1.4f;
    [SerializeField] private float fallTime = 1.5f;
    [SerializeField] private float dropHeight = 32f;
    [Range(0f, 1f)] [SerializeField] private float targetedChance = 0.15f;
    [SerializeField] private float sizeMin = 0.45f;
    [SerializeField] private float sizeMax = 1.15f;

    [Header("Hasar (zehir olarak)")]
    [SerializeField] private float damageBase = 9f;
    [SerializeField] private float damagePerSize = 8f;
    [SerializeField] private float hitRadiusBase = 0.9f;
    [SerializeField] private float hitRadiusPerSize = 0.9f;

    [Header("Görsel")]
    [SerializeField] private Mesh[] rockMeshes;
    [SerializeField] private Material rockMaterial;
    [SerializeField] private Material warningMaterial;
    [SerializeField] private Material dustMaterial;
    [SerializeField] private float rockLifetime = 6f;

    private struct Impact { public float time; public Vector3 point; public float radius; public float damage; }
    private readonly List<Impact> pending = new List<Impact>();
    private readonly Dictionary<PlayerLife, float> nextDrop = new Dictionary<PlayerLife, float>();
    private readonly Dictionary<PlayerLife, Vector3> lastPos = new Dictionary<PlayerLife, Vector3>();
    private ParticleSystem dust, debris;
    private readonly RaycastHit[] hits = new RaycastHit[8];

    public static bool Active
    {
        get
        {
            if (Totem.All.Count == 0) return false;
            // çöküş sekansı (sarsıntı/kayma) boyunca dışarıda yağmaya devam eder; herkes düşüp sekans bitince durur
            CollapseSequence cs = CollapseSequence.Instance;
            if (cs != null && (cs.Phase == CollapsePhase.Finished || cs.Phase == CollapsePhase.Arrived)) return false;
            foreach (Totem t in Totem.All) if (t != null && !t.IsTaken) return false;
            return true;
        }
    }

    private bool Protected(Vector3 pos)
    {
        if (pos.y < undergroundY) return true;
        if (safeCenter != null)
        {
            Vector3 d = pos - safeCenter.position; d.y = 0f;
            if (d.sqrMagnitude < safeRadius * safeRadius) return true;
        }
        return false;
    }

    public override void OnNetworkDespawn() { WeatherController.ForceStorm = false; }

    private void Update()
    {
        bool active = IsSpawned && Active;
        WeatherController.ForceStorm = active;
        if (!active || !IsServer) { pending.Clear(); return; }

        float now = Time.time;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            Vector3 pos = p.transform.position;
            Vector3 vel = lastPos.TryGetValue(p, out Vector3 lp) && Time.deltaTime > 0f ? (pos - lp) / Time.deltaTime : Vector3.zero;
            lastPos[p] = pos;
            if (Protected(pos)) continue;
            if (!nextDrop.TryGetValue(p, out float next)) next = now + Random.Range(0.5f, 1.5f);
            if (now < next) { nextDrop[p] = next; continue; }
            nextDrop[p] = now + Random.Range(intervalMin, intervalMax);
            ServerDrop(pos, vel);
        }

        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Impact im = pending[i];
            if (now < im.time) continue;
            pending.RemoveAt(i);
            foreach (PlayerLife p in PlayerLife.All)
            {
                if (p == null || p.State != PlayerLifeState.Alive || Protected(p.transform.position)) continue;
                Vector3 d = p.transform.position - im.point;
                if (Mathf.Abs(d.y) > 2.5f) continue;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > im.radius) continue;
                PoisonSystem poison = p.GetComponent<PoisonSystem>();
                if (poison != null) poison.AddPoison(im.damage * Mathf.Lerp(1f, 0.5f, dist / im.radius));
            }
        }
    }

    private void ServerDrop(Vector3 playerPos, Vector3 vel)
    {
        Vector3 target;
        float r = Random.value;
        vel.y = 0f;
        if (r < targetedChance) target = playerPos + vel * fallTime + Random.insideUnitSphere * 1.2f;             // oyuncunun gideceği yere
        else if (r < targetedChance + 0.35f) target = playerPos + Flat(Random.insideUnitCircle.normalized * Random.Range(2.5f, 8f));
        else target = playerPos + Flat(Random.insideUnitCircle.normalized * Random.Range(8f, 22f));         // ormanın her yerinde
        target.z = Mathf.Min(target.z, 81f); // dağ eteğinin içine/üstüne düşmesin
        if (!Ground(target, out Vector3 ground)) return;
        if (Protected(ground)) return;
        float size = Random.Range(sizeMin, sizeMax);
        int seed = Random.Range(0, 100000);
        pending.Add(new Impact
        {
            time = Time.time + fallTime,
            point = ground,
            radius = hitRadiusBase + size * hitRadiusPerSize,
            damage = damageBase + size * damagePerSize
        });
        DropRpc(ground, size, seed);
    }

    private static Vector3 Flat(Vector2 v) { return new Vector3(v.x, 0f, v.y); }

    private bool Ground(Vector3 p, out Vector3 ground)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 25f, p.z), Vector3.down, hits, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; ground = p;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.GetComponentInParent<PlayerLife>() != null) continue; // oyuncunun üstüne değil, zemine
            if (hits[i].distance < best) { best = hits[i].distance; ground = hits[i].point; }
        }
        return best < float.MaxValue;
    }

    // ---------- İstemci görseli ----------

    [Rpc(SendTo.Everyone)]
    private void DropRpc(Vector3 point, float size, int seed)
    {
        EnsureParticles();
        var rng = new System.Random(seed);
        var go = new GameObject("FallingRock");
        var fr = go.AddComponent<FallingRock>();
        Mesh mesh = rockMeshes != null && rockMeshes.Length > 0 ? rockMeshes[rng.Next(rockMeshes.Length)] : null;
        Vector3 drift = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 6f;
        fr.Begin(this, point, size, fallTime, dropHeight, drift, mesh, rockMaterial, warningMaterial, rockLifetime,
                 new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()) * 540f);
    }

    internal void OnRockLanded(Vector3 point, float size)
    {
        // KULLANICI (cila): çarpma sesi; büyük kaya daha tok ve yüksek
        if (Sfx.Lib != null) Sfx.At(Sfx.Lib.rockHit, point, Mathf.Lerp(0.55f, 1f, size), Mathf.Lerp(1.1f, 0.62f, Mathf.InverseLerp(0.45f, 1.15f, size)), 0.08f, 3f, 45f);
        if (dust != null)
        {
            dust.transform.position = point + Vector3.up * 0.2f;
            dust.Emit(new ParticleSystem.EmitParams { applyShapeToPosition = true }, Mathf.RoundToInt(10 + size * 14));
        }
        if (debris != null)
        {
            debris.transform.position = point + Vector3.up * 0.15f;
            debris.Emit(new ParticleSystem.EmitParams { applyShapeToPosition = true }, Mathf.RoundToInt(6 + size * 8));
        }
        // yerel oyuncu yakındaysa sarsıntı
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || !p.IsOwner) continue;
            float d = Vector3.Distance(p.transform.position, point);
            if (d > 12f) continue;
            float k = (1f - d / 12f) * (0.6f + size);
            PlayerFeel feel = p.GetComponent<PlayerFeel>();
            if (feel != null) feel.Kick(new Vector3(-1.5f * k, Random.Range(-1f, 1f) * k, Random.Range(-1.5f, 1.5f) * k));
        }
    }

    private void EnsureParticles()
    {
        if (dust != null) return;
        dust = MakePS("RockDust", dustMaterial, 0.8f, 1.8f, 0.6f, 2.2f, 0.6f, 1.6f, -0.05f, new Color(0.32f, 0.28f, 0.22f, 0.55f), ParticleSystemShapeType.Hemisphere, 0.4f);
        debris = MakePS("RockDebris", dustMaterial, 0.6f, 1.1f, 3f, 6f, 0.06f, 0.16f, 1.6f, new Color(0.2f, 0.18f, 0.16f, 1f), ParticleSystemShapeType.Hemisphere, 0.25f);
    }

    private static ParticleSystem MakePS(string name, Material mat, float lifeMin, float lifeMax, float spMin, float spMax, float szMin, float szMax, float gravity, Color color, ParticleSystemShapeType shape, float radius)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.playOnAwake = false; m.loop = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax); m.startSpeed = new ParticleSystem.MinMaxCurve(spMin, spMax);
        m.startSize = new ParticleSystem.MinMaxCurve(szMin, szMax); m.gravityModifier = gravity; m.startColor = color; m.maxParticles = 600;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.shapeType = shape; sh.radius = radius; go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.6f));
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        return ps;
    }
}

/// <summary>Tek bir düşen kaya (yalnızca görsel): uyarı gölgesi büyür, kaya hızlanarak düşer, çarpar, bir süre kalıp toprağa gömülür.</summary>
public class FallingRock : MonoBehaviour
{
    private RockRain owner;
    private Vector3 point, start;
    private float size, fall, life, t;
    private Vector3 spin;
    private Transform rock, warn;
    private Material warnMat;
    private bool landed;

    public void Begin(RockRain rain, Vector3 groundPoint, float rockSize, float fallTime, float height, Vector3 drift, Mesh mesh, Material mat, Material warningMat, float lifetime, Vector3 spinDeg)
    {
        owner = rain; point = groundPoint; size = rockSize; fall = fallTime; life = lifetime; spin = spinDeg;
        start = groundPoint + Vector3.up * height + drift;
        var r = new GameObject("Rock"); r.transform.SetParent(transform, false); rock = r.transform;
        r.AddComponent<MeshFilter>().sharedMesh = mesh != null ? mesh : Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        var mr = r.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On;
        rock.position = start; rock.localScale = Vector3.one * size * 1.3f; rock.rotation = Random.rotation;
        if (warningMat != null)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(w.GetComponent<Collider>());
            w.name = "Warning"; w.transform.SetParent(transform, false); warn = w.transform;
            warn.position = point + Vector3.up * 0.04f; warn.rotation = Quaternion.Euler(90f, Random.value * 360f, 0f); warn.localScale = Vector3.zero;
            var wr = w.GetComponent<MeshRenderer>(); warnMat = new Material(warningMat); wr.sharedMaterial = warnMat; wr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private void OnDestroy() { if (warnMat != null) Destroy(warnMat); }

    private void Update()
    {
        t += Time.deltaTime;
        if (!landed)
        {
            float k = Mathf.Clamp01(t / fall);
            rock.position = Vector3.Lerp(start, point + Vector3.up * size * 0.35f, k * k); // hızlanarak düşer
            rock.Rotate(spin * Time.deltaTime, Space.Self);
            if (warn != null)
            {
                float s = Mathf.Lerp(0.3f, 1.9f + size * 1.6f, k);
                warn.localScale = new Vector3(s, s, 1f);
                SetWarnAlpha(Mathf.Lerp(0.15f, 0.75f, k));
            }
            if (k >= 1f) { landed = true; t = 0f; owner?.OnRockLanded(point, size); }
            return;
        }
        if (warn != null) SetWarnAlpha(Mathf.Lerp(0.75f, 0f, t / 0.6f));
        if (t > life) rock.position += Vector3.down * (size * 0.6f * Time.deltaTime); // toprağa gömülür
        if (t > life + 2f) Destroy(gameObject);
    }

    private void SetWarnAlpha(float a)
    {
        if (warnMat == null) return;
        Color c = warnMat.GetColor("_BaseColor"); c.a = a; warnMat.SetColor("_BaseColor", c);
    }
}
