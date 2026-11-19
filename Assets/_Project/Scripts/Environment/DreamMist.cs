using UnityEngine;

/// <summary>
/// B3 (KULLANICI 2026-11-15: "rüyalara özgü bir sis efekti lazım, görüş biraz daha kısılsın"): Rüya köyünde yerde süzülen sis katmanları.
/// Kameranın çevresinde, yerin 0.2–1.6 m üstünde, yere paralel yatan iri yumuşak sis tabakaları doğar; ağır ağır sürüklenir, belirip söner.
/// Renk mor ile soluk pembe arasında değişir. Düz tabakalar yere değmediği için zeminle kesişme çizgisi oluşmaz.
/// Yalnızca yerel görsel (ağ yok); yalnızca köy zeminindeyken çalışır (düşüşte ve ev içlerinde değil).
/// </summary>
public class DreamMist : MonoBehaviour
{
    [SerializeField] private Material material;
    [Tooltip("Aynı anda yaklaşık kaç sis tabakası.")]
    [SerializeField] private int targetCount = 70;
    [SerializeField] private float innerRadius = 2.5f;
    [SerializeField] private float outerRadius = 30f;
    [SerializeField] private Vector2 height = new Vector2(0.04f, 0.55f);      // KULLANICI: yere daha yakın
    [SerializeField] private Vector2 size = new Vector2(7f, 16f);
    [SerializeField] private Vector2 lifetime = new Vector2(14f, 20f);
    [SerializeField] private float drift = 0.35f;
    [SerializeField] private Color colorA = new Color(0.55f, 0.45f, 0.8f, 0.2f);
    [SerializeField] private Color colorB = new Color(0.82f, 0.52f, 0.76f, 0.15f);

    private ParticleSystem ps;
    private float pending;
    private bool wasHere;

    private void Build()
    {
        GameObject go = new GameObject("DreamMistParticles");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation3D = true;
        main.maxParticles = targetCount * 2;
        var em = ps.emission; em.enabled = false;           // elle doğuruluyor
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.85f, 1f, 1.15f));   // yavaşça yayılır
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.alignment = ParticleSystemRenderSpace.World;   // yere paralel tabakalar (rotation3D ile yatırılır)
        rend.sortMode = ParticleSystemSortMode.Distance;
        rend.maxParticleSize = 100f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        if (material != null) rend.sharedMaterial = material;
        ps.Play();
    }

    private void Update()
    {
        Camera cam = Camera.main;
        if (FreefallSequence.Instance == null || cam == null) return;
        Vector3 c = cam.transform.position;
        float ground = FreefallSequence.GroundY;
        bool here = FreefallSequence.InColumn(c) && c.y > ground - 3f && c.y < ground + 25f;
        if (!here) { wasHere = false; return; }
        if (ps == null) Build();

        if (!wasHere) { wasHere = true; pending = targetCount; }      // köye ilk gelişte alan dolsun
        pending += Time.deltaTime * targetCount / Mathf.Max(1f, (lifetime.x + lifetime.y) * 0.5f);
        while (pending >= 1f) { pending -= 1f; EmitOne(c, ground); }
    }

    private void EmitOne(Vector3 center, float ground)
    {
        float a = Random.value * Mathf.PI * 2f;
        float r = Mathf.Lerp(innerRadius, outerRadius, Mathf.Sqrt(Random.value));
        var ep = new ParticleSystem.EmitParams
        {
            position = new Vector3(center.x + Mathf.Cos(a) * r, ground + Random.Range(height.x, height.y), center.z + Mathf.Sin(a) * r),
            velocity = new Vector3(Random.Range(-drift, drift), 0f, Random.Range(-drift, drift)),
            startSize = Random.Range(size.x, size.y),
            startLifetime = Random.Range(lifetime.x, lifetime.y),
            rotation3D = new Vector3(90f, Random.value * 360f, 0f),
            startColor = Color.Lerp(colorA, colorB, Random.value),
        };
        ps.Emit(ep, 1);
    }
}
