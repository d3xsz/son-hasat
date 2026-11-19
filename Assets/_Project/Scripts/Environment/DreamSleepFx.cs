using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// B3-07 (KULLANICI 2026-11-15: "öksürük saçma, zehir gibi olmasın; rüyada halüsinasyon görebilir"): Uykunun YEREL etkileri.
/// Uyku (zehir göstergesi) arttıkça: göz kapakları ağırlaşır (ekran üstten ve alttan kapanıp açılır, uyku arttıkça daha sık ve uzun),
/// halüsinasyonlar belirir (görüş kenarında duran karanlık bir siluet: bakınca, yaklaşınca ya da ışık tutunca kaybolur) ve
/// arada bir yakından kapı tıklatması duyulur. Her oyuncu yalnızca kendi halüsinasyonunu görür (ağ yok).
/// Bölüm 3 sahnesinde (DreamWake nesnesinde) durur; yerel oyuncuyu kendisi bulur. Yeşil kenar/öksürük PoisonEffects'te rüyada kapalıdır.
/// </summary>
public class DreamSleepFx : MonoBehaviour
{
    [Header("Göz kapakları")]
    [Tooltip("Uyku bu oranın üstündeyken göz kapakları ağırlaşmaya başlar (0..1).")]
    [SerializeField, Range(0f, 1f)] private float lidStart = 0.25f;

    [Header("Halüsinasyon")]
    [Tooltip("Sahnedeki siluet nesnesi (kapalı durur; yeri değiştirilip açılır).")]
    [SerializeField] private GameObject phantom;
    [SerializeField, Range(0f, 1f)] private float phantomStart = 0.4f;
    [SerializeField] private float phantomLife = 7f;
    [SerializeField] private float creepSpeed = 0.5f;

    private PlayerLife local;
    private PoisonSystem poison;
    private RectTransform oval;
    private RawImage ovalImage;
    private Image haze;
    private Texture2D ovalTex;
    private float blinkStart = -1f, blinkLength, blinkPeak, nextBlink;
    private float nextPhantom, phantomUntil, stare, nextKnock;

    private void OnDisable()
    {
        if (phantom != null) phantom.SetActive(false);
        SetLids(0f);
    }

    private void FindLocal()
    {
        if (local != null) return;
        foreach (PlayerLife p in PlayerLife.All)
            if (p != null && p.IsOwner) { local = p; poison = p.GetComponent<PoisonSystem>(); return; }
    }

    private void BuildLids()
    {
        var canvasGo = new GameObject("DreamSleepOverlay");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 250;   // bölüm sonu yazısının (300) altında
        // KULLANICI (2026-11-15): düz siyah şerit değil; kenarlardan içe doğru kapanan PUSLU, hafif siyah OVAL.
        // Doku: ortası saydam, dışa doğru yumuşakça kararan oval. Görüntü büyütülüp küçültülerek açıklık daraltılır.
        const int S = 128;
        ovalTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v * 2.6f);                 // yatay oval: önce üstten ve alttan kapanır
                float t = Mathf.Clamp01((d - 0.16f) / 0.62f);
                float a = t * t * (3f - 2f * t);
                px[y * S + x] = new Color32(5, 3, 9, (byte)(a * 232f));     // tam siyah değil, puslu
            }
        ovalTex.SetPixels32(px); ovalTex.Apply();

        var ogo = new GameObject("Oval", typeof(RectTransform)); ogo.transform.SetParent(canvasGo.transform, false);
        oval = (RectTransform)ogo.transform;
        oval.anchorMin = oval.anchorMax = oval.pivot = new Vector2(0.5f, 0.5f);
        ovalImage = ogo.AddComponent<RawImage>(); ovalImage.texture = ovalTex; ovalImage.raycastTarget = false;

        var hgo = new GameObject("Haze", typeof(RectTransform)); hgo.transform.SetParent(canvasGo.transform, false);
        var hrt = (RectTransform)hgo.transform; hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
        haze = hgo.AddComponent<Image>(); haze.raycastTarget = false; haze.color = new Color(0.02f, 0.012f, 0.035f, 0f);
    }

    private void OnDestroy() { if (ovalTex != null) Destroy(ovalTex); }

    private void SetLids(float closed)
    {
        if (oval == null) return;
        closed = Mathf.Clamp01(closed);
        bool on = closed > 0.002f;
        if (ovalImage.enabled != on) { ovalImage.enabled = on; haze.enabled = on; }
        if (!on) return;
        // açık: görüntü ekranın 9 katı (yalnızca saydam orta görünür); kapalı: ekran kadar (oval açıklık küçücük)
        float s = Mathf.Lerp(9f, 1f, Mathf.Pow(closed, 0.55f));
        oval.sizeDelta = new Vector2(Screen.width, Screen.height) * s / Mathf.Max(0.01f, oval.lossyScale.x);
        Color c = haze.color; c.a = closed * closed * 0.8f; haze.color = c;      // tam kapanınca ortadaki açıklık da puslanır
    }

    private void Update()
    {
        FindLocal();
        Camera cam = Camera.main;
        bool active = local != null && poison != null && cam != null && local.State == PlayerLifeState.Alive && FreefallSequence.Active
                      && FreefallSequence.InColumn(local.transform.position) && local.transform.position.y < FreefallSequence.GroundY + 20f;
        float sleep = active ? poison.Value01 : 0f;
        if (oval == null) { if (sleep <= 0f) return; BuildLids(); }

        // ---------- göz kapakları ----------
        float k = Mathf.InverseLerp(lidStart, 1f, sleep);
        float closed = sleep > 0.5f ? (sleep - 0.5f) * 0.36f : 0f;                // yüksek uykuda kapaklar hep biraz düşük
        if (k > 0f && blinkStart < 0f && Time.time >= nextBlink)
        {
            blinkStart = Time.time;
            blinkLength = Mathf.Lerp(0.3f, 1.6f, k) * Random.Range(0.8f, 1.2f);
            blinkPeak = Mathf.Lerp(0.6f, 1f, k);
        }
        if (blinkStart >= 0f)
        {
            float t = (Time.time - blinkStart) / blinkLength;
            if (t >= 1f) { blinkStart = -1f; nextBlink = Time.time + Mathf.Lerp(13f, 3f, k) * Random.Range(0.7f, 1.3f); }
            else closed = Mathf.Max(closed, Mathf.Sin(t * Mathf.PI) * blinkPeak);
        }
        if (k <= 0f) nextBlink = Time.time + 4f;
        SetLids(closed);

        if (!active) { if (phantom != null && phantom.activeSelf) phantom.SetActive(false); return; }

        // ---------- kapı tıklatması ----------
        if (sleep < 0.3f) nextKnock = Time.time + Random.Range(15f, 30f);
        else if (Time.time >= nextKnock)
        {
            nextKnock = Time.time + Random.Range(18f, 40f);
            Vector3 p = cam.transform.position + Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward * Random.Range(4f, 8f);
            for (int i = 0; i < 3; i++) Sfx.At(DreamSfx.Tick, p, 0.6f, 0.7f, 0.03f, 2f, 20f);
        }

        // ---------- siluet ----------
        if (phantom == null) return;
        Transform ct = cam.transform;
        if (!phantom.activeSelf)
        {
            if (sleep < phantomStart) { nextPhantom = Time.time + Random.Range(8f, 16f); return; }
            if (Time.time < nextPhantom) return;
            nextPhantom = Time.time + Mathf.Lerp(28f, 9f, Mathf.InverseLerp(phantomStart, 1f, sleep)) * Random.Range(0.7f, 1.3f);
            // görüş kenarında, yerde, engelsiz bir nokta
            Vector3 flat = ct.forward; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            Vector3 dir = Quaternion.Euler(0f, (Random.value < 0.5f ? -1f : 1f) * Random.Range(32f, 62f), 0f) * flat.normalized;
            float dist = Random.Range(9f, 16f);
            Vector3 eye = ct.position;
            if (Physics.Raycast(eye, dir, out RaycastHit hit, dist + 1f, ~0, QueryTriggerInteraction.Ignore)) dist = hit.distance - 1.2f;
            if (dist < 4.5f) return;
            Vector3 feet = new Vector3(eye.x, local.transform.position.y - 1f, eye.z) + dir * dist;
            phantom.transform.position = feet;
            phantom.SetActive(true);
            phantomUntil = Time.time + phantomLife;
            stare = 0f;
            Sfx.At(DreamSfx.Chime, feet + Vector3.up, 0.25f, 0.45f, 0.02f, 3f, 25f);
            return;
        }

        // açıkken: oyuncuya döner, ağır ağır yaklaşır; bakınca / yaklaşınca / ışık tutunca / süresi dolunca kaybolur
        Vector3 pos = phantom.transform.position;
        Vector3 to = ct.position - pos; to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
        {
            phantom.transform.rotation = Quaternion.LookRotation(to);
            phantom.transform.position = pos + to.normalized * creepSpeed * Time.deltaTime;
        }
        Vector3 chest = pos + Vector3.up * 1.2f;
        bool looking = Vector3.Angle(ct.forward, chest - ct.position) < 11f;
        stare = looking ? stare + Time.deltaTime : 0f;
        bool lit = FlashlightReveal.IsLit(chest, 18f, 20f, phantom.transform);
        if (stare > 0.35f || lit || to.magnitude < 4.5f || Time.time >= phantomUntil || sleep < phantomStart * 0.6f)
        {
            phantom.SetActive(false);
            Sfx.At(DreamSfx.Chime, chest, 0.3f, 0.35f, 0.02f, 3f, 25f);
        }
    }
}
