using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (2026-11-10): Lobiden oyuna geçişte yükleme ekranı. Sisli koyu arka plan (prosedürel), "SON HASAT"
/// başlığı ve en az 3 sn'de dolan ince çizgi. İpucu yazısı yok. ConnectionManager sahne yüklemesi başlarken Show,
/// herkes yükleyince Hide çağırır; çizgi sonuna kadar dolar, sonra ekran açılır (sinematik o zaman başlar).
/// Kalıcı, kendi canvas'ı (ScreenFader'ın üstünde).
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    private static LoadingScreen instance;

    private CanvasGroup group;
    private RectTransform barFill;
    private RawImage fog;
    private float progress, alpha, alphaTarget, shownAt, fullAt = -1f;
    private bool completing;

    /// <summary>Yükleme çok hızlı olsa bile çizgi en az bu sürede dolar (sn).</summary>
    public const float MinShowSeconds = 3f;
    /// <summary>Çizgi dolduktan sonra bekleme + ekranın açılma süresi (sn). Sinematik bundan sonra başlar.</summary>
    public const float HoldSeconds = 0.3f, FadeOutSeconds = 0.8f;

    public static void Show()
    {
        Ensure();
        instance.gameObject.SetActive(true);
        instance.progress = 0f; instance.completing = false; instance.fullAt = -1f;
        instance.shownAt = Time.unscaledTime;
        instance.alphaTarget = 1f;
        instance.alpha = 1f; // anında kapla (siyah flaş olmasın)
    }

    /// <summary>Yükleme bitti: çizgi sonuna kadar dolar, sonra ekran yavaşça açılır.</summary>
    public static void Hide()
    {
        if (instance == null || !instance.gameObject.activeSelf) return;
        instance.completing = true;
    }

    private static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreen>();
        instance.Build();
    }

    private void Build()
    {
        // ScreenFader'ın (1000) üstünde: sahne yüklenince fader'ın kararıp açılması yükleme ekranının arkasında kalır
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1010;
        var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        group = gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = true; group.alpha = 0f;

        var bg = Child<RawImage>("Background"); Stretch(bg.rectTransform);
        bg.color = Color.white; bg.texture = FogTexture();
        fog = Child<RawImage>("Fog"); Stretch(fog.rectTransform); fog.texture = bg.texture; fog.color = new Color(1f, 1f, 1f, 0.35f);

        var title = Child<Text>("Title");
        title.rectTransform.anchoredPosition = new Vector2(0f, 40f); title.rectTransform.sizeDelta = new Vector2(1400f, 160f);
        title.font = GameFonts.Title; title.fontSize = 120; title.alignment = TextAnchor.MiddleCenter; title.text = "SON HASAT";
        title.color = new Color(0.93f, 0.88f, 0.75f); // menü başlığıyla aynı renk

        var track = Child<Image>("BarTrack");
        track.rectTransform.anchoredPosition = new Vector2(0f, -70f); track.rectTransform.sizeDelta = new Vector2(520f, 3f);
        track.color = new Color(0.9f, 0.8f, 0.6f, 0.15f);
        var fill = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>(); fill.transform.SetParent(track.transform, false);
        barFill = fill.rectTransform; barFill.anchorMin = new Vector2(0f, 0f); barFill.anchorMax = new Vector2(0f, 1f); barFill.pivot = new Vector2(0f, 0.5f);
        barFill.anchoredPosition = Vector2.zero; barFill.sizeDelta = new Vector2(0f, 0f);
        fill.color = new Color(0.95f, 0.72f, 0.4f, 0.9f);
        gameObject.SetActive(false);
    }

    private T Child<T>(string name) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(transform, false);
        var g = go.AddComponent<T>(); g.raycastTarget = false; return g;
    }

    private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        // çizgi en az MinShowSeconds'ta, düzgün hızla dolar; yükleme bitmeden %90'da bekler
        float elapsed = Time.unscaledTime - shownAt;
        float desired = Mathf.Clamp01(elapsed / MinShowSeconds);
        if (!completing) desired = Mathf.Min(desired, 0.9f);
        progress = Mathf.MoveTowards(progress, desired, dt * 1.5f);
        barFill.sizeDelta = new Vector2(520f * progress, 0f);
        if (completing && progress >= 0.999f && fullAt < 0f) fullAt = Time.unscaledTime;
        if (fullAt >= 0f && Time.unscaledTime - fullAt >= HoldSeconds) alphaTarget = 0f;
        alpha = Mathf.MoveTowards(alpha, alphaTarget, dt * (alphaTarget > alpha ? 4f : 1f / FadeOutSeconds));
        group.alpha = alpha;
        // sis yavaşça kayar
        if (fog != null) fog.uvRect = new Rect(Time.unscaledTime * 0.012f, 0f, 1f, 1f);
        if (alphaTarget <= 0f && alpha <= 0.001f) gameObject.SetActive(false);
    }

    private static Texture2D fogTex;
    private static Texture2D FogTexture()
    {
        if (fogTex != null) return fogTex;
        const int W = 256, H = 144;
        fogTex = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Repeat, name = "LoadingFog" };
        var px = new Color[W * H];
        Color top = new Color(0.02f, 0.025f, 0.04f), bottom = new Color(0.06f, 0.075f, 0.1f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / W, v = (float)y / H;
                float n = Mathf.PerlinNoise(u * 4f, v * 3f) * 0.6f + Mathf.PerlinNoise(u * 11f + 3f, v * 8f) * 0.4f;
                float mist = Mathf.Clamp01(n * (1.2f - v)) * 0.6f;
                px[y * W + x] = Color.Lerp(Color.Lerp(bottom, top, v), new Color(0.16f, 0.18f, 0.22f), mist * 0.5f);
            }
        fogTex.SetPixels(px); fogTex.Apply();
        return fogTex;
    }
}
