using UnityEngine;

/// <summary>
/// KULLANICI (cila): Mağara ağzını kapatan taş yığını (eski düz "kapı" yerine). 5. totem alınana kadar (çöküş Idle) ağız kapalı;
/// çöküş başlayınca taşlar içeri, kuyuya doğru yuvarlanıp düşer, toz kalkar ve geçiş açılır.
/// Yalnızca görsel + engel collider'ı; durumu CollapseSequence faz değişiminden alır (her kopyada aynı).
/// </summary>
public class CaveRubble : MonoBehaviour
{
    [SerializeField] private Collider blocker;
    [SerializeField] private Renderer[] glowCracks;
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private float fallDuration = 2.2f;

    private Transform[] rocks;
    private Vector3[] startPos;
    private Quaternion[] startRot;
    private Vector3[] velocity;
    private Vector3[] spin;
    private float openTime = -1f;
    private bool open;

    private void Awake() { Cache(); }

    private void Cache()
    {
        if (rocks != null) return;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in transform) if (c.GetComponent<MeshRenderer>() != null && c.name.StartsWith("Rubble")) list.Add(c);
        rocks = list.ToArray();
        startPos = new Vector3[rocks.Length];
        startRot = new Quaternion[rocks.Length];
        velocity = new Vector3[rocks.Length];
        spin = new Vector3[rocks.Length];
        for (int i = 0; i < rocks.Length; i++) { startPos[i] = rocks[i].localPosition; startRot[i] = rocks[i].localRotation; }
    }

    /// <summary>open=true: yığın çöker (instant=true ise hemen kaybolur, geç katılan/yeniden yükleme için).</summary>
    public void SetOpen(bool value, bool instant)
    {
        Cache();
        if (value == open && !instant) return;
        open = value;
        if (blocker != null) blocker.enabled = !open;
        if (glowCracks != null) foreach (Renderer r in glowCracks) if (r != null) r.enabled = !open;

        if (!open)
        {
            openTime = -1f;
            for (int i = 0; i < rocks.Length; i++)
            {
                rocks[i].gameObject.SetActive(true);
                rocks[i].localPosition = startPos[i];
                rocks[i].localRotation = startRot[i];
            }
            return;
        }

        if (instant)
        {
            foreach (Transform r in rocks) r.gameObject.SetActive(false);
            return;
        }

        openTime = Time.time;
        for (int i = 0; i < rocks.Length; i++)
        {
            float h = startPos[i].y;
            // üstteki taşlar içeri ve aşağı (kuyuya) yuvarlanır, alttakiler biraz geç kayar
            velocity[i] = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0f, 1.2f), Random.Range(1.5f, 3.5f) + h * 0.4f);
            spin[i] = Random.insideUnitSphere * Random.Range(90f, 260f);
        }
        if (dust != null) dust.Play();
        // KULLANICI (cila): alçak gürleme + ardı ardına düşen taşlar
        if (Sfx.Lib != null)
        {
            Sfx.At(Sfx.Lib.rumble, transform.position + Vector3.up * 2f, 1f, 0.55f, 0.03f, 6f, 60f);
            StartCoroutine(RockHits());
        }
    }

    private System.Collections.IEnumerator RockHits()
    {
        for (int i = 0; i < 7; i++)
        {
            yield return new WaitForSeconds(Random.Range(0.12f, 0.35f));
            Sfx.At(Sfx.Lib.rockHit, transform.position + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(0f, 2f), 1f), Random.Range(0.6f, 1f), Random.Range(0.6f, 0.85f), 0.05f, 4f, 45f);
        }
    }

    private void Update()
    {
        if (openTime < 0f) return;
        float t = Time.time - openTime;
        float dt = Time.deltaTime;
        for (int i = 0; i < rocks.Length; i++)
        {
            if (!rocks[i].gameObject.activeSelf) continue;
            float delay = (4.5f - startPos[i].y) * 0.08f; // yukarıdakiler önce düşer
            if (t < delay) continue;
            velocity[i] += Vector3.down * (9f * dt);
            rocks[i].localPosition += velocity[i] * dt;
            rocks[i].localRotation = Quaternion.Euler(spin[i] * dt) * rocks[i].localRotation;
            if (rocks[i].localPosition.y < -8f || t > fallDuration + 1.5f) rocks[i].gameObject.SetActive(false);
        }
        if (t > fallDuration + 1.6f) openTime = -1f;
    }
}
