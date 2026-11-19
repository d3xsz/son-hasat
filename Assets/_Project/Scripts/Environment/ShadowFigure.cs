using UnityEngine;

/// <summary>
/// G-25: Gölge siluet (GDD 8.1, 2. kademe). Yalnızca görsel, yerel (ağda gönderilmez, oyun durumunu etkilemez).
/// KULLANICI (cila): Sıska, uzun kollu, alttan sis tutamlarına dağılan silüet (kameraya dönük kart) + çevresinde koyu duman,
/// titreyen küçük gözler. Sisin içinden solarak belirir, kaybolurken yukarı uzayıp dağılır (küçülmez).
/// Sis içinde oyuncunun çevresinde yan yana dolaşır. Oyuncu yaklaşırsa, doğrudan bakarsa veya ömrü dolarsa kaybolur.
/// </summary>
public class ShadowFigure : MonoBehaviour
{
    [Header("Görsel")]
    [SerializeField] private Renderer silhouette;
    [SerializeField] private ParticleSystem smoke;
    [SerializeField] private float silhouetteAlpha = 0.92f;

    [Header("Gözler")]
    [SerializeField] private Renderer[] eyeRenderers;
    [SerializeField, ColorUsage(false, true)] private Color eyeColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private float eyeIntensity = 3f;
    [Tooltip("Gözlerin çevresindeki yumuşak hale (sisin içinden fark edilsin diye).")]
    [SerializeField] private Renderer eyeGlow;
    [SerializeField] private float eyeGlowIntensity = 0.6f;
    [SerializeField, Range(0f, 1f)] private float eyeGlowAlpha = 0.55f;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 1.2f;
    [SerializeField] private float growTime = 1.4f;
    [SerializeField] private float vanishTime = 0.9f;
    [Tooltip("Kamera yönüyle figür arasındaki bu açıdan (derece) küçük bakış 'doğrudan bakma' sayılır.")]
    [SerializeField] private float lookAngle = 12f;

    private Transform target;
    private Camera cam;
    private float baseScale = 1f;
    private float lifetime;
    private float vanishDistance;
    private float lookSeconds;
    private float age;
    private float lookTimer;
    private float vanishElapsed = -1f;
    private float strafeDirection = 1f;
    private float flickerPhase;
    private MaterialPropertyBlock block;
    private Color silhouetteBase;
    private bool initialized;

    public void Init(Transform playerTarget, Camera viewCamera, float scale, float lifeSeconds, float vanishDist, float lookSecs)
    {
        target = playerTarget;
        cam = viewCamera;
        baseScale = scale;
        lifetime = lifeSeconds;
        vanishDistance = vanishDist;
        lookSeconds = lookSecs;
        strafeDirection = Random.value < 0.5f ? -1f : 1f;
        flickerPhase = Random.value * 10f;
        block = new MaterialPropertyBlock();
        transform.localScale = Vector3.one * baseScale;
        if (silhouette != null) silhouetteBase = silhouette.sharedMaterial.GetColor("_BaseColor");
        ApplyAlpha(0f, 1f);
        initialized = true;
    }

    public void Vanish()
    {
        if (vanishElapsed >= 0f) return;
        vanishElapsed = 0f;
        if (smoke != null)
        {
            smoke.Emit(30); // dağılırken duman patlaması
            var em = smoke.emission; em.enabled = false;
        }
    }

    private void ApplyAlpha(float a, float stretchY)
    {
        if (silhouette != null && block != null)
        {
            silhouette.GetPropertyBlock(block);
            Color c = silhouetteBase; c.a = silhouetteBase.a * a * silhouetteAlpha;
            block.SetColor("_BaseColor", c);
            silhouette.SetPropertyBlock(block);
            Vector3 s = silhouette.transform.localScale;
            silhouette.transform.localScale = new Vector3(s.x, Mathf.Abs(s.y) > 0f ? baseSilY * stretchY : s.y, s.z);
        }
    }

    private float baseSilY = -1f;

    private void Awake()
    {
        if (silhouette != null) baseSilY = silhouette.transform.localScale.y;
    }

    private void Update()
    {
        if (!initialized) return;
        if (target == null) { Destroy(gameObject); return; }

        float dt = Time.deltaTime;
        age += dt;

        float alpha, stretch = 1f;
        if (vanishElapsed >= 0f)
        {
            vanishElapsed += dt;
            float t = Mathf.Clamp01(vanishElapsed / vanishTime);
            if (t >= 1f) { Destroy(gameObject, smoke != null ? 2.5f : 0f); initialized = false; ApplyAlpha(0f, 1f); SetEyes(0f); return; }
            alpha = 1f - t * t;
            stretch = 1f + t * 0.35f;                       // yukarı doğru uzayıp dağılır
            transform.position += Vector3.up * (0.8f * dt);
        }
        else alpha = Mathf.SmoothStep(0f, 1f, age / growTime); // sisten solarak belirir
        // nefes / titreme: hafif yalpa ve yoğunluk dalgalanması
        alpha *= 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 1.7f, flickerPhase);
        ApplyAlpha(alpha, stretch);

        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        if (dist > 0.01f)
        {
            Vector3 dir = toPlayer / dist;
            // kart her zaman kameraya döner (Y ekseninde), hafifçe öne eğik
            Vector3 camDir = cam != null ? cam.transform.position - transform.position : toPlayer;
            camDir.y = 0f;
            if (camDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(camDir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.9f + flickerPhase) * 2.5f);
            if (vanishElapsed < 0f) transform.position += Vector3.Cross(Vector3.up, dir) * (strafeDirection * moveSpeed * dt);
        }

        SetEyes(vanishElapsed >= 0f ? 1f - Mathf.Clamp01(vanishElapsed / (vanishTime * 0.5f)) : Mathf.Clamp01((age - growTime * 0.6f) / 0.5f));
        if (vanishElapsed >= 0f) return;

        if (age >= lifetime || dist < vanishDistance) { Vanish(); return; }

        if (cam != null)
        {
            Vector3 toFigure = transform.position + Vector3.up * 1.8f * baseScale - cam.transform.position;
            if (Vector3.Angle(cam.transform.forward, toFigure) < lookAngle)
            {
                lookTimer += dt;
                if (lookTimer >= lookSeconds) Vanish();
            }
            else lookTimer = Mathf.Max(0f, lookTimer - dt);
        }
    }

    private void SetEyes(float strength)
    {
        if (eyeRenderers == null || block == null) return;
        float on = Mathf.PerlinNoise(Time.time * 6f, flickerPhase) > 0.2f ? 1f : 0.1f;
        Color c = eyeColor * (eyeIntensity * on * strength);
        if (eyeGlow != null)
        {
            eyeGlow.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", eyeColor * (eyeGlowIntensity * on * strength));
            block.SetColor("_BaseColor", new Color(0f, 0f, 0f, eyeGlowAlpha * on * strength));
            eyeGlow.SetPropertyBlock(block);
        }
        foreach (Renderer r in eyeRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", c);
            block.SetColor("_BaseColor", new Color(c.r, c.g, c.b, strength));
            r.SetPropertyBlock(block);
        }
    }
}
