using UnityEngine;

/// <summary>
/// V-01 (görsel cila): Totem çevresindeki taş halka. Totem yerindeyken rünler kehribar renkte, sırayla nefes alır gibi
/// parlar ve korlar yükselir. Totem alınınca (mühür kırılır) rünler bir an parlar, sonra soluk, kalp atışı gibi atan
/// kızıla döner; korlar kesilir. Yalnızca görsel: her kopyada Totem.IsActive okunur, ağ/oynanış yok.
/// </summary>
public class TotemShrine : MonoBehaviour
{
    [SerializeField] private Totem totem;
    [Tooltip("Rün ve mühür halkası çizimleri (Goblin/FogResistUnlit).")]
    [SerializeField] private Renderer[] runeRenderers = new Renderer[0];
    [SerializeField] private ParticleSystem embers;

    [SerializeField, ColorUsage(false, true)] private Color aliveColor = new Color(2.6f, 1.25f, 0.28f);
    [SerializeField, ColorUsage(false, true)] private Color brokenColor = new Color(0.75f, 0.05f, 0.03f);
    [SerializeField] private float breatheSpeed = 0.55f;
    [SerializeField] private float fadeSeconds = 2.5f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private float alive = 1f;
    private float surge;
    private bool wasAlive = true;
    private float phase;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
    }

    private void Update()
    {
        bool isAlive = totem == null || totem.IsActive;
        if (wasAlive && !isAlive) surge = 1f;                 // mühür kırıldı: kısa parlama
        if (isAlive != wasAlive && embers != null)
        {
            if (isAlive) embers.Play(); else embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        wasAlive = isAlive;

        float dt = Time.deltaTime;
        alive = Mathf.MoveTowards(alive, isAlive ? 1f : 0f, dt / Mathf.Max(0.1f, fadeSeconds));
        surge = Mathf.MoveTowards(surge, 0f, dt / 0.9f);

        float t = Time.time;
        int n = runeRenderers.Length;
        for (int i = 0; i < n; i++)
        {
            Renderer r = runeRenderers[i];
            if (r == null) continue;

            // canlı: halka boyunca dolaşan nefes dalgası
            float wave = 0.5f + 0.5f * Mathf.Sin((t * breatheSpeed + phase) * Mathf.PI * 2f - i * 0.9f);
            Color a = aliveColor * (0.45f + 0.55f * wave);

            // kırık: iki vuruşlu kalp atışı + ara sıra titreme
            float beat = Mathf.Repeat(t * 0.8f + phase, 1f);
            float pulse = Mathf.Exp(-beat * 9f) + 0.6f * Mathf.Exp(-Mathf.Abs(beat - 0.22f) * 14f);
            float crackle = Mathf.PerlinNoise(t * 7f, i * 3.7f + phase) > 0.72f ? 0.35f : 0f;
            Color b = brokenColor * (0.12f + 0.9f * pulse + crackle);

            Color c = Color.Lerp(b, a, alive) + aliveColor * (surge * surge * 2.5f);
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(Mathf.Min(1f, c.r), Mathf.Min(1f, c.g), Mathf.Min(1f, c.b), 1f));
            block.SetColor(EmissionId, c);
            r.SetPropertyBlock(block);
        }
    }
}
