using UnityEngine;

/// <summary>
/// G-26: Örümcek hareketi (yalnızca görsel, her kopyada). M-04: kullanıcının tek parça modeli (`model`) varsa bacaklar
/// oynamaz; onun yerine model yürürken seğirerek sallanır/eğilir, dururken nefes alır, saldırıda ileri atılır (PlayLunge).
/// Eski placeholder bacak salınımı da korunur (renderer'ları kapalı olsa bile zararsız).
/// </summary>
public class SpiderAnimator : MonoBehaviour
{
    [Tooltip("Bacak pivotları (8 adet). Her biri yerel Y ekseninde ileri-geri sallanır.")]
    [SerializeField] private Transform[] legs;
    [SerializeField] private Transform body;
    [SerializeField] private float swingAngle = 28f;
    [SerializeField] private float stepFrequency = 9f;

    [Header("Tek parça model (M-04)")]
    [SerializeField] private Transform model;
    [SerializeField] private float bobHeight = 0.05f;
    [SerializeField] private float rollAngle = 6f;
    [SerializeField] private float lungeDistance = 0.45f;
    [SerializeField] private float lungeDuration = 0.4f;

    private Vector3 modelStartPos;
    private Quaternion modelStartRot;
    private float lungeTime = -1f;

    /// <summary>Saldırı anında (Creature.AttackSoundRpc, herkeste) kısa ileri atılma.</summary>
    public void PlayLunge() { if (deathTime < 0f) lungeTime = 0f; }

    private Vector3 lastPosition;
    private float smoothSpeed;
    private float phase;
    private Vector3 bodyStart;
    private Quaternion[] legStart;

    private void Awake()
    {
        lastPosition = transform.position;
        if (body != null) bodyStart = body.localPosition;
        if (model != null) { modelStartPos = model.localPosition; modelStartRot = model.localRotation; }
        if (legs != null)
        {
            legStart = new Quaternion[legs.Length];
            for (int i = 0; i < legs.Length; i++) if (legs[i] != null) legStart[i] = legs[i].localRotation;
        }
    }

    // ---------- Ölüm (KULLANICI: anında yok olmasın) ----------
    private float deathTime = -1f;
    private float deathDuration;
    private Transform visual;
    private Vector3 visualStartPos;
    private Quaternion visualStartRot;
    private Vector3 flipPivot;

    /// <summary>Creature ölünce her kopyada çağırır: sırtüstü devrilir, bacaklar çırpınıp durur, sonda yere gömülür.</summary>
    public void PlayDeath(float duration)
    {
        if (deathTime >= 0f) return;
        deathTime = 0f;
        deathDuration = Mathf.Max(2f, duration);
        visual = body != null && body.parent != null ? body.parent : transform;
        visualStartPos = visual.localPosition;
        visualStartRot = visual.localRotation;
        flipPivot = body != null ? body.localPosition : Vector3.up * 0.3f;
    }

    private static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    private void UpdateDeath(float dt)
    {
        deathTime += dt;
        float flip = Smooth01(deathTime / 0.45f);
        Quaternion r = Quaternion.Euler(0f, 0f, 180f * flip);
        Vector3 c = visualStartRot * flipPivot;
        float hop = Mathf.Sin(flip * Mathf.PI) * 0.35f;
        float settle = flip * c.y * 0.45f;                                     // sırtı yere değsin
        float sink = Smooth01((deathTime - (deathDuration - 1.6f)) / 1.4f) * 0.7f; // sonda toprağa gömülür
        visual.localRotation = r * visualStartRot;
        visual.localPosition = visualStartPos + c - r * c + Vector3.up * (hop - settle - sink);

        if (legs == null) return;
        float twitch = Mathf.Clamp01(1f - (deathTime - 0.3f) / 1.6f);
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i] == null) continue;
            float a = Mathf.Sin(deathTime * (22f + i * 3f) + i) * 30f * twitch;
            legs[i].localRotation = legStart[i] * Quaternion.Euler(0f, a, 0f);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (deathTime >= 0f) { UpdateDeath(dt); return; }
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        lastPosition = transform.position;
        smoothSpeed = Mathf.Lerp(smoothSpeed, speed, 8f * dt);

        float amp = Mathf.Clamp01(smoothSpeed / 3f);
        phase += dt * stepFrequency * (0.4f + amp);

        if (legs != null)
        {
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                float p = phase + (i % 2 == 0 ? 0f : Mathf.PI) + i * 0.4f;
                legs[i].localRotation = legStart[i] * Quaternion.Euler(0f, Mathf.Sin(p) * swingAngle * amp, 0f);
            }
        }
        if (body != null) body.localPosition = bodyStart + Vector3.up * (Mathf.Abs(Mathf.Sin(phase * 2f)) * 0.03f * amp);
        if (model != null) AnimateModel(dt, amp);
    }

    private void AnimateModel(float dt, float amp)
    {
        float t = Time.time;
        // yürürken: hızlı seğirme (adım ritminde zıplama, yana yatma, burun eğilmesi); dururken: yavaş nefes
        float bob = Mathf.Abs(Mathf.Sin(phase * 2f)) * bobHeight * amp + Mathf.Sin(t * 2.2f) * 0.008f * (1f - amp);
        float roll = Mathf.Sin(phase) * rollAngle * amp;
        float pitch = Mathf.Sin(phase * 2f + 0.8f) * 3f * amp + 4f * amp; // koşarken burun hafif aşağı
        float yawJitter = Mathf.Sin(phase * 3.1f) * 3f * amp;

        float push = 0f, rear = 0f;
        if (lungeTime >= 0f)
        {
            float k = lungeTime / Mathf.Max(0.05f, lungeDuration);
            // hızlı ileri, yavaş geri; atılırken ön kısım kalkar
            float s = k < 0.3f ? Mathf.SmoothStep(0f, 1f, k / 0.3f) : Mathf.SmoothStep(1f, 0f, (k - 0.3f) / 0.7f);
            push = s * lungeDistance;
            rear = s * 18f;
            lungeTime += dt;
            if (k >= 1f) lungeTime = -1f;
        }

        model.localPosition = modelStartPos + new Vector3(0f, bob + push * 0.15f, push);
        model.localRotation = modelStartRot * Quaternion.Euler(pitch - rear, yawJitter, roll);
    }
}
