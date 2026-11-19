using UnityEngine;
using Unity.Netcode;

/// <summary>
/// B3-08 (KULLANICI 2026-11-14, engel): Karanlık evlerden izleyen gözler. Işıksız bir evin pencerelerinde gözler belirir;
/// eve fazla yaklaşan oyuncunun uykusu (zehir göstergesi) hızla artar. Pencerelere fener tutulunca gözler bir süre kapanır
/// ve ev zararsız olur. Gözlerin açık/kapalı olması her kopyada yerel hesaplanır (fener durumu ve bakış ağda senkron);
/// uykuyu yalnızca sunucu ekler. Ağ nesnesi değildir; evin üstünde durur.
/// </summary>
public class WatchingEyes : MonoBehaviour
{
    [Tooltip("Göz çiftlerinin kökleri (her pencerede bir tane).")]
    [SerializeField] private Transform[] eyes = new Transform[0];
    [SerializeField] private Renderer[] eyeRenderers = new Renderer[0];
    [Tooltip("Bu yarıçapın içindeki oyuncunun uykusu artar (m, evin merkezinden).")]
    [SerializeField] private float dangerRadius = 7.5f;
    [Tooltip("Tehlike alanında saniyede eklenen uyku.")]
    [SerializeField] private float sleepPerSecond = 5f;
    [Tooltip("Işık tutulunca gözlerin kapalı kaldığı süre (sn).")]
    [SerializeField] private float closedSeconds = 9f;
    [Tooltip("Gözler bu uzaklıktan itibaren belirir (m).")]
    [SerializeField] private float showDistance = 24f;
    [SerializeField, ColorUsage(false, true)] private Color calmColor = new Color(1.6f, 1.3f, 0.5f);
    [SerializeField, ColorUsage(false, true)] private Color angryColor = new Color(3f, 0.35f, 0.2f);

    private float closedUntil;
    private float open;          // 0 kapalı, 1 açık (görsel)
    private float anger;
    private float nextBlink, blinkUntil, nextHiss;
    private Vector3[] baseScale = new Vector3[0];
    private MaterialPropertyBlock block;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        baseScale = new Vector3[eyes.Length];
        for (int i = 0; i < eyes.Length; i++) if (eyes[i] != null) baseScale[i] = eyes[i].localScale;
        nextBlink = Time.time + Random.Range(2f, 6f);
    }

    private void Update()
    {
        if (FreefallSequence.Instance == null) return;

        // ışık tutulan pencere → gözler kapanır
        for (int i = 0; i < eyes.Length; i++)
            if (eyes[i] != null && FlashlightReveal.IsLit(eyes[i].position, 18f, 22f, transform)) { closedUntil = Time.time + closedSeconds; break; }
        bool awake = Time.time >= closedUntil;

        // en yakın oyuncu (görsel) + tehlike alanındakiler (sunucu: uyku)
        NetworkManager nm = NetworkManager.Singleton;
        bool server = nm != null && nm.IsServer;
        Vector3 center = transform.position;
        bool anyInside = false;
        Vector3 nearest = center; float best = float.MaxValue;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            Vector3 d = p.transform.position - center;
            if (Mathf.Abs(d.y) > 15f) continue;         // ev içi odalar köyün çok altında
            d.y = 0f;
            float m = d.magnitude;
            if (m < best) { best = m; nearest = p.transform.position; }
            if (!awake || m > dangerRadius) continue;
            anyInside = true;
            if (server)
            {
                PoisonSystem poison = p.GetComponent<PoisonSystem>();
                if (poison != null) poison.AddPoisonIgnoringProtection(sleepPerSecond * Time.deltaTime);
            }
        }

        // ---------- görsel ----------
        Camera cam = Camera.main;
        bool near = cam != null && Vector3.Distance(cam.transform.position, center) < showDistance;
        if (Time.time >= nextBlink) { blinkUntil = Time.time + 0.14f; nextBlink = Time.time + Random.Range(2.5f, 7f); }
        bool blink = Time.time < blinkUntil;
        open = Mathf.MoveTowards(open, awake && near && !blink ? 1f : 0f, Time.deltaTime * (blink ? 14f : 3f));
        anger = Mathf.MoveTowards(anger, anyInside ? 1f : 0f, Time.deltaTime * 2f);
        Color c = Color.Lerp(calmColor, angryColor, anger) * open;
        for (int i = 0; i < eyes.Length; i++)
        {
            if (eyes[i] == null) continue;
            Vector3 s = baseScale[i];
            eyes[i].localScale = new Vector3(s.x, s.y, s.z * Mathf.Max(0.02f, open));     // göz kapağı gibi dikeyde kapanır
        }
        foreach (Renderer r in eyeRenderers)
        {
            if (r == null) continue;
            r.enabled = open > 0.02f;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", c);
            r.SetPropertyBlock(block);
        }

        // tehlike alanındayken alçak, tekinsiz bir uğultu
        if (anyInside && Time.time >= nextHiss)
        {
            nextHiss = Time.time + Random.Range(1.6f, 2.6f);
            Sfx.At(DreamSfx.Chime, center + Vector3.up * 2f, 0.45f, 0.3f, 0.03f, 3f, 22f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, dangerRadius);
    }
}
