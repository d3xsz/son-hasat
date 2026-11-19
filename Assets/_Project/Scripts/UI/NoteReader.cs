using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (2026-11-09): Okunabilir not kağıdı ekranı. ReadableNote E ile açar; ekranın ortasında eski bir kağıt sayfası
/// (prosedürel doku) ve el yazısı benzeri metin görünür. E / Esc / sol tık kapatır. Açıkken oyuncu yürüyemez/bakamaz
/// (oyun durmaz). Yalnızca yerel ekran; ağ yok. Not metni oyun içi bir belge (ipucu yazısı değil, okumak oyuncunun seçimi).
/// </summary>
public class NoteReader : MonoBehaviour
{
    private static NoteReader instance;
    private static Texture2D paperTex;

    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;
    /// <summary>Kapandığı kare (aynı karede E/Esc başka bir şeyi tetiklemesin).</summary>
    public static int ClosedFrame { get; private set; } = -1;

    private GameObject root;
    private Text titleText, bodyText;
    private CanvasGroup group;
    private RectTransform paper;
    private float openTime;

    public static void Open(string title, string body)
    {
        if (instance == null)
        {
            var go = new GameObject("NoteReader");
            instance = go.AddComponent<NoteReader>();
            instance.Build();
        }
        instance.Show(title, body);
    }

    private RawImage bigImage;

    /// <summary>Kağıt yerine büyük bir görsel gösterir (harita tabelası). Kapatma aynı.</summary>
    public static void OpenImage(Texture tex)
    {
        if (instance == null)
        {
            var go = new GameObject("NoteReader");
            instance = go.AddComponent<NoteReader>();
            instance.Build();
        }
        var self = instance;
        if (self.bigImage == null)
        {
            self.bigImage = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            self.bigImage.transform.SetParent(self.root.transform, false);
            self.bigImage.raycastTarget = false;
        }
        self.bigImage.texture = tex;
        float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1.3f;
        float h = 900f; self.bigImage.rectTransform.sizeDelta = new Vector2(h * aspect, h);
        self.paper.gameObject.SetActive(false);
        self.bigImage.gameObject.SetActive(true);
        self.root.SetActive(true);
        self.openTime = Time.unscaledTime;
        self.group.alpha = 0f;
        if (Application.isPlaying && Sfx.Lib != null) Sfx.UI(Sfx.Lib.pickup, 0.6f, 1.1f, 0.05f);
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400; // HUD üstü, duraklatma menüsü/karartma altı
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        Stretch((RectTransform)root.transform);
        group = root.AddComponent<CanvasGroup>();

        var dim = new GameObject("Dim", typeof(RectTransform)).AddComponent<Image>();
        dim.transform.SetParent(root.transform, false); Stretch(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, 0.72f);

        var p = new GameObject("Paper", typeof(RectTransform)).AddComponent<RawImage>();
        p.transform.SetParent(root.transform, false);
        paper = p.rectTransform;
        paper.sizeDelta = new Vector2(760, 980);
        p.texture = PaperTexture();

        titleText = MakeText(paper, "Title", GameFonts.Title, 50, new Vector2(0, -70), new Vector2(620, 90), TextAnchor.MiddleCenter);
        bodyText = MakeText(paper, "Body", GameFonts.Body, 27, new Vector2(0, -150), new Vector2(620, 770), TextAnchor.UpperLeft);
        bodyText.lineSpacing = 1.05f;
        // uzun notlar kağıttan taşmasın
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.resizeTextForBestFit = true; bodyText.resizeTextMinSize = 18; bodyText.resizeTextMaxSize = 27;
        root.SetActive(false);
    }

    private static Text MakeText(RectTransform parent, string name, Font font, int size, Vector2 pos, Vector2 sizeDelta, TextAnchor anchor)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        t.font = font; t.fontSize = size; t.alignment = anchor;
        t.color = new Color(0.22f, 0.14f, 0.08f);
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void Show(string title, string body)
    {
        paper.gameObject.SetActive(true);
        if (bigImage != null) bigImage.gameObject.SetActive(false);
        // KULLANICI: başlıksız notlar — başlık yoksa metin sayfanın üstünden başlar
        bool hasTitle = !string.IsNullOrEmpty(title);
        titleText.gameObject.SetActive(hasTitle);
        titleText.text = title;
        bodyText.rectTransform.anchoredPosition = new Vector2(0f, hasTitle ? -150f : -90f);
        bodyText.rectTransform.sizeDelta = new Vector2(620f, hasTitle ? 770f : 820f);
        bodyText.text = body;
        root.SetActive(true);
        openTime = Time.unscaledTime;
        group.alpha = 0f;
        paper.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-2.5f, 2.5f));
        if (Application.isPlaying && Sfx.Lib != null) Sfx.UI(Sfx.Lib.pickup, 0.6f, 1.25f, 0.05f);
    }

    private void Close()
    {
        root.SetActive(false);
        ClosedFrame = Time.frameCount;
        if (Sfx.Lib != null) Sfx.UI(Sfx.Lib.pickup, 0.4f, 0.95f, 0.05f);
    }

    private void Update()
    {
        if (!IsOpen) return;
        float t = Time.unscaledTime - openTime;
        group.alpha = Mathf.Clamp01(t * 5f);
        paper.anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, 0f, Mathf.Clamp01(t * 4f)));
        if (bigImage != null && bigImage.gameObject.activeSelf) bigImage.rectTransform.anchoredPosition = paper.anchoredPosition;
        if (t < 0.15f) return; // açıldığı E basışı hemen kapatmasın
        var kb = Keyboard.current; var mouse = Mouse.current;
        bool close = (kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                     || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        // oyuncu öldü/bayıldıysa da kapan
        if (close) Close();
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    // KULLANICI (2026-11-10): eskiden kalma kağıt — sararmış zemin, yanık/yırtık kenar, kopuk köşe, çapraz katlama izleri,
    // su halkası, pas benekleri (foxing). Metnin okunduğu orta bölge görece temiz kalır. Prosedürel; asset yok.
    private static Texture2D PaperTexture()
    {
        if (paperTex != null) return paperTex;
        const int W = 380, H = 490;
        paperTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "NotePaper" };
        paperTex.SetPixels(AgedPaper(W, H, 1234));
        paperTex.Apply(false, true);
        return paperTex;
    }

    /// <summary>Eski kağıt pikselleri (dünyadaki not dokusu da aynı görünümü kullanır).</summary>
    public static Color[] AgedPaper(int W, int H, int seed)
    {
        var px = new Color[W * H];
        var rng = new System.Random(seed);
        Color baseC = new Color(0.82f, 0.73f, 0.55f), stain = new Color(0.58f, 0.44f, 0.27f), burn = new Color(0.22f, 0.13f, 0.06f);
        // pas benekleri
        int spotN = 40; var spots = new Vector3[spotN];
        for (int i = 0; i < spotN; i++) spots[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 0.004f + (float)rng.NextDouble() * 0.012f);
        Vector2 ringC = new Vector2(0.2f + (float)rng.NextDouble() * 0.6f, 0.15f + (float)rng.NextDouble() * 0.25f);
        float aspect = (float)W / H;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / W, v = (float)y / H;
                float n = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.6f + Mathf.PerlinNoise(u * 23f + 7f, v * 23f) * 0.4f;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                // kopuk sağ üst köşe: çapraz yırtık
                float corner = (1f - u) + (1f - v) - 0.13f - Mathf.PerlinNoise(u * 30f, v * 30f) * 0.04f;
                edge = Mathf.Min(edge, corner * 0.7f);
                float rag = 0.014f + Mathf.PerlinNoise(u * 45f, v * 45f + 3f) * 0.024f;
                float a = Mathf.Clamp01((edge - rag) * 260f);
                // kenara doğru sararma + yanık şerit
                float yellow = Mathf.Clamp01(1f - edge * 5f) * 0.55f;
                float scorch = Mathf.Clamp01(1f - (edge - rag) * 40f);
                float blot = Mathf.Clamp01((Mathf.PerlinNoise(u * 3f + 11f, v * 3f + 5f) - 0.58f) * 3.5f) * 0.45f;
                // çapraz katlama izleri: koyu çizgi + yanında açık şerit
                float fu = (u - 0.5f) * W, fv = (v - 0.5f) * H;
                float foldD = Mathf.Exp(-fv * fv / 2.2f) * 0.3f + Mathf.Exp(-fu * fu / 2.2f) * 0.25f;
                float foldL = Mathf.Exp(-(fv - 2.5f) * (fv - 2.5f) / 3f) * 0.12f + Mathf.Exp(-(fu - 2.5f) * (fu - 2.5f) / 3f) * 0.1f;
                // su halkası
                float rd = Vector2.Distance(new Vector2(u * aspect, v), new Vector2(ringC.x * aspect, ringC.y));
                float ring = Mathf.Clamp01(1f - Mathf.Abs(rd - 0.11f) * 90f) * 0.35f + (rd < 0.11f ? 0.08f : 0f);
                Color c = Color.Lerp(baseC, stain, Mathf.Clamp01(yellow + blot + (n - 0.5f) * 0.3f + foldD + ring));
                c = Color.Lerp(c, Color.white * 0.95f, foldL);
                foreach (var s in spots)
                {
                    float d = Vector2.Distance(new Vector2(u * aspect, v), new Vector2(s.x * aspect, s.y));
                    if (d < s.z * 2f) c = Color.Lerp(c, stain * 0.85f, Mathf.Clamp01(1f - d / (s.z * 2f)) * 0.55f);
                }
                c = Color.Lerp(c, burn, scorch * 0.85f);
                c *= 0.95f + Mathf.PerlinNoise(u * 120f, v * 120f) * 0.08f; // lif dokusu
                c.a = a;
                px[y * W + x] = c;
            }
        return px;
    }
}
