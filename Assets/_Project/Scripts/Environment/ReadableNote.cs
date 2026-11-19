using UnityEngine;

/// <summary>
/// KULLANICI (2026-11-09): Bölüm başlarında (checkpoint yanında) duran okunabilir kağıt not. Dikkat çeksin diye kağıt
/// nefes alır gibi parlar, sıcak ışık mum gibi titrer, çevresinde toz zerreleri uçuşur ve kağıt rüzgârda kıpırdar.
/// Okunduktan sonra sönükleşir. E ile NoteReader açılır. Yalnızca yerel (ağ yok).
/// </summary>
public class ReadableNote : MonoBehaviour, IInteractable
{
    [Tooltip("Boş bırakılırsa sayfada başlık olmaz (KULLANICI tercihi).")]
    [SerializeField] private string title = "";
    [SerializeField, TextArea(8, 30)] private string body = "";
    [Tooltip("Doluysa metin yerine bu görsel büyük gösterilir (ör. harita tabelası).")]
    [SerializeField] private Texture image;
    [SerializeField] private string prompt = "Oku";
    [Tooltip("Doluysa not okunup KAPATILDIĞI anda ekranda bu bilgilendirme gösterilir (KULLANICI 2026-11-15: Bölüm 3 öğretici notu).")]
    [SerializeField] private string afterReadHint = "";
    [SerializeField] private float afterReadHintSeconds = 8f;
    private bool waitingClose;
    [Tooltip("Kıpırdayan kağıt (isteğe bağlı).")]
    [SerializeField] private Transform paper;
    [SerializeField] private Renderer paperRenderer;
    [SerializeField] private Light glow;
    [SerializeField] private ParticleSystem motes;
    [SerializeField, ColorUsage(false, true)] private Color paperGlow = new Color(1.1f, 0.85f, 0.5f);
    [SerializeField] private float lightIntensity = 3f;

    private Quaternion paperRest;
    private float phase;
    private bool read;
    private float readBlend;
    private float litBlend;      // KULLANICI (2026-11-15): fener tutulunca kağıt parlıyordu → kendi parıltısı ve ışığı söner
    private MaterialPropertyBlock block;

    public string InteractionPrompt => string.IsNullOrEmpty(prompt) ? "Oku" : prompt;
    public bool CanInteract(PlayerInteractor interactor) => !NoteReader.IsOpen;

    public void Interact(PlayerInteractor interactor)
    {
        read = true;
        if (image != null) NoteReader.OpenImage(image);
        else NoteReader.Open(title, body);
        waitingClose = !string.IsNullOrEmpty(afterReadHint);
    }

    private void Awake()
    {
        if (paper != null) paperRest = paper.localRotation;
        phase = Random.value * 10f;
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (waitingClose && !NoteReader.IsOpen)
        {
            waitingClose = false;
            HudController hud = FindFirstObjectByType<HudController>();
            if (hud != null) hud.ShowHint(afterReadHint, afterReadHintSeconds);
        }
        float t = Time.time + phase;
        readBlend = Mathf.MoveTowards(readBlend, read ? 1f : 0f, Time.deltaTime * 0.5f);
        float strength = Mathf.Lerp(1f, 0.35f, readBlend);
        Vector3 paperPos = paperRenderer != null ? paperRenderer.bounds.center : transform.position + Vector3.up;
        bool lit = FlashlightReveal.IsLit(paperPos, 14f, 30f, transform);
        litBlend = Mathf.MoveTowards(litBlend, lit ? 1f : 0f, Time.deltaTime * 4f);
        strength *= Mathf.Lerp(1f, 0.1f, litBlend);

        if (paper != null)
            paper.localRotation = paperRest * Quaternion.Euler(Mathf.Sin(t * 2.3f) * 5f + Mathf.Sin(t * 5.1f) * 2f, 0f, Mathf.Sin(t * 1.7f) * 3f);

        // kağıt yavaşça "nefes alır" (okunmadıysa belirgin)
        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 2.2f));
        if (paperRenderer != null)
        {
            paperRenderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", paperGlow * (pulse * strength));
            paperRenderer.SetPropertyBlock(block);
        }
        if (glow != null)
        {
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(t * 3f, phase);
            glow.intensity = lightIntensity * strength * flicker * (0.75f + 0.25f * pulse);
        }
        if (motes != null)
        {
            var em = motes.emission;
            em.rateOverTimeMultiplier = read ? 1.5f : 6f;
        }
    }
}
