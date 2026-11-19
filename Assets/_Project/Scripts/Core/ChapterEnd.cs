using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// B2-06 (GDD 7.1): Bölüm 2 sonu. Rampanın tepesindeki ışık açıklığına (sahanlık) hayattaki TÜM oyuncular varınca —
/// ya da ilk varıştan sonra waitSeconds geçince — sunucu bölümü bitirir. Herkeste: ekran ışığa (beyaza) açılır, sonra kararır,
/// başlık görünür ("SİSLİ ADA / Bölüm 2: Derinlikler / Devamı gelecek..."), birkaç saniye sonra ana menüye dönülür.
/// Oyuncu kontrolü sonda kilitlenir. Sahne içi NetworkObject.
/// </summary>
public class ChapterEnd : NetworkBehaviour
{
    [Tooltip("Varış alanı (dünya, merkez + boyut).")]
    [SerializeField] private Vector3 areaCenter = new Vector3(0f, -287f, 128.5f);
    [SerializeField] private Vector3 areaSize = new Vector3(6f, 4f, 7f);
    [SerializeField] private float waitSeconds = 45f;
    [SerializeField] private string title = "SON HASAT";
    [SerializeField] private string subtitle = "Bölüm 2: Derinlikler";
    [SerializeField] private string footer = "Devamı gelecek...";
    [SerializeField] private float whiteSeconds = 2.5f;
    [SerializeField] private float blackSeconds = 1.5f;
    [SerializeField] private float titleHold = 6f;
    [Tooltip("B3-01: boş değilse bitiş ekranı yerine ekran beyaza açılır ve bu sahne eklemeli yüklenir (sunucu). Bölüm 3 düşüşü oradan başlar.")]
    [SerializeField] private string nextSceneName = "";
    [Tooltip("B3-06: true ise varış alanı ve rüzgar sesi kullanılmaz; bölüm yalnızca ServerEnd() ile biter (Bölüm 3 uyanışı).")]
    [SerializeField] private bool manualOnly;
    [Tooltip("B3: geçiş sırasında beyaz ekranda gösterilen başlık (ör. \"Bölüm 3\") ve alt başlık (ör. \"Düşler\"). Boşsa yazı çıkmaz.")]
    [SerializeField] private string nextTitle = "";
    [SerializeField] private string nextSubtitle = "";
    private Text nextTitleText;      // Bölüm 2 başlığıyla aynı biçim (ChapterTitleStyle)
    private bool nextRequested;

    /// <summary>Bu bölüm sonu bir sonraki bölümün sahnesini yükler mi (Bölüm 2 çıkışı). Bölüm seçimi kullanır.</summary>
    public bool LeadsToNextChapter => !manualOnly && !string.IsNullOrEmpty(nextSceneName);

    /// <summary>Sunucu: bölümü hemen bitirir (B3-06 uyanış: herkes yatağa yatınca DreamWakeWatcher çağırır).</summary>
    public void ServerEnd(bool skipWhite = false)
    {
        if (!IsServer || !IsSpawned || ended.Value) return;
        instant.Value = skipWhite;
        ended.Value = true;
    }

    // bölüm seçimi: ışık/beyaz aşaması olmadan doğrudan siyah ekran + sıradaki bölüm başlığı
    private readonly NetworkVariable<bool> instant = new NetworkVariable<bool>(false);
    private float freefallSeenAt = -1f;

    private readonly NetworkVariable<bool> ended = new NetworkVariable<bool>(false);
    private float firstArrival = -1f;
    private float endTime = -1f;
    private bool returned;
    private Image white, black;
    private Text titleText, subText, footText;
    private AudioSource windSource;

    private bool Inside(Vector3 p)
    {
        Vector3 d = p - areaCenter;
        return Mathf.Abs(d.x) <= areaSize.x * 0.5f && Mathf.Abs(d.y) <= areaSize.y * 0.5f && Mathf.Abs(d.z) <= areaSize.z * 0.5f;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && !ended.Value) { if (!manualOnly) ServerCheck(); DebugSkip(); }
        if (ended.Value) UpdateEnding();
        else if (!manualOnly) UpdateAmbience();
    }

    private void ServerCheck()
    {
        int alive = 0, inside = 0;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State == PlayerLifeState.Dead) continue;
            alive++;
            if (Inside(p.transform.position)) inside++;
        }
        if (inside > 0 && firstArrival < 0f) firstArrival = Time.time;
        if (alive > 0 && (inside == alive || (firstArrival >= 0f && Time.time - firstArrival >= waitSeconds))) ended.Value = true;
    }

    /// <summary>TEST KISAYOLU (yalnızca Editor / Development Build): mağaradayken host F9 → bölüm sonu hemen tetiklenir
    /// (Bölüm 3 düşüşüne geçiş). Sürüm build'inde derlenmez.</summary>
    private void DebugSkip()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || !kb.f9Key.wasPressedThisFrame) return;
        if (manualOnly != (FreefallSequence.Instance != null)) return;      // Bölüm 3 yüklüyse yalnızca Bölüm 3'ün sonu tetiklenir
        Debug.Log("[TEST] F9: bölüm sonu (" + subtitle + ")");
        ended.Value = true;
#endif
    }

    // ışığa yaklaştıkça dışarıdan rüzgar sesi
    private void UpdateAmbience()
    {
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null) return;
        float d = Vector3.Distance(cam.position, areaCenter);
        float target = Mathf.Clamp01(1f - (d - 6f) / 30f) * 0.35f;
        EnsureWind();
        if (windSource != null) windSource.volume = Mathf.MoveTowards(windSource.volume, target, Time.deltaTime * 0.3f);
    }

    private void EnsureWind()
    {
        if (windSource != null || Sfx.Lib == null || Sfx.Lib.fallWind == null) return;
        windSource = gameObject.AddComponent<AudioSource>();
        windSource.clip = Sfx.Lib.fallWind; windSource.loop = true; windSource.spatialBlend = 0f; windSource.pitch = 0.6f; windSource.volume = 0f;
        windSource.Play();
    }

    private void UpdateEnding()
    {
        if (endTime < 0f)
        {
            endTime = instant.Value ? Time.time - whiteSeconds : Time.time;
            BuildOverlay();
            PlayerController.InputLocked = true;
            if (Sfx.Lib != null && !instant.Value) Sfx.UI(Sfx.Lib.rumble, 0.5f, 1.4f, 0.01f);
        }
        float t = Time.time - endTime;
        if (!string.IsNullOrEmpty(nextSceneName)) { UpdateTransition(t); return; }
        float w = Mathf.Clamp01(t / whiteSeconds);
        float b = Mathf.Clamp01((t - whiteSeconds) / blackSeconds);
        white.color = new Color(1f, 0.97f, 0.9f, w * (1f - b));
        black.color = new Color(0f, 0f, 0f, b);
        float ta = Mathf.Clamp01((t - whiteSeconds - blackSeconds) / 1.2f);
        titleText.color = new Color(0.93f, 0.88f, 0.75f, ta);
        subText.color = new Color(0.75f, 0.78f, 0.82f, Mathf.Clamp01(ta * 1.2f - 0.2f));
        footText.color = new Color(0.6f, 0.62f, 0.66f, Mathf.Clamp01((t - whiteSeconds - blackSeconds - 2f) / 1.2f));
        if (windSource != null) windSource.volume = Mathf.Lerp(windSource.volume, 0f, Time.deltaTime);

        if (!returned && t >= whiteSeconds + blackSeconds + titleHold)
        {
            returned = true;
            PlayerController.InputLocked = false;
            if (PauseMenu.Instance != null) PauseMenu.Instance.ReturnToMainMenu();
        }
    }

    /// <summary>B3-01: ışığa girince beyaz ekran → sunucu sıradaki sahneyi yükler → düşüş başlayınca beyaz açılır.</summary>
    private void UpdateTransition(float t)
    {
        float w = Mathf.Clamp01(t / whiteSeconds);
        if (IsServer && !nextRequested && t >= whiteSeconds)
        {
            nextRequested = true;
            NetworkManager nm = NetworkManager.Singleton;
            SceneEventProgressStatus status = nm != null && nm.SceneManager != null
                ? nm.SceneManager.LoadScene(nextSceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive)
                : SceneEventProgressStatus.SceneFailedVerification;
            if (status != SceneEventProgressStatus.Started)
                Debug.LogError("[Bölüm] " + nextSceneName + " yüklenemedi: " + status + " (Build Settings'te mi?)");
        }
        if (FreefallSequence.Active && freefallSeenAt < 0f)
        {
            freefallSeenAt = Time.time;
            PlayerController.InputLocked = false;
        }
        // düşüş hiç başlamazsa (sahne yüklenemedi) eski bitiş ekranına düş
        if (freefallSeenAt < 0f && t > whiteSeconds + 20f)
        {
            if (nextTitleText != null) nextTitleText.color = ChapterTitleStyle.WithAlpha(0f);
            nextSceneName = ""; endTime = Time.time - whiteSeconds; return;
        }

        // KULLANICI (2026-11-15): Bölüm 3 başlığı Bölüm 2 ile AYNI görsel dilde: ışığa girince beyaz → siyaha döner, siyah üstünde
        // krem başlık → düşüş başlayınca siyah açılır, başlık birkaç saniye daha kalıp söner.
        bool hasTitle = !string.IsNullOrEmpty(nextTitle);
        // başlık okunabilsin diye siyah ekran en az bu kadar kalır (sahne çabuk yüklense bile)
        float revealAt = freefallSeenAt >= 0f ? Mathf.Max(freefallSeenAt, endTime + whiteSeconds + (hasTitle ? 2.4f : 0f)) : -1f;
        float open = revealAt >= 0f ? Mathf.Clamp01((Time.time - revealAt - 0.5f) / 1.8f) : 0f;
        float dark = hasTitle ? (instant.Value ? 1f : Mathf.Clamp01((t - whiteSeconds) / 0.9f)) : 0f;
        white.color = new Color(1f, 0.97f, 0.9f, instant.Value ? 0f : w * (1f - dark) * (1f - open));
        black.color = new Color(0f, 0f, 0f, dark * (1f - open));
        if (hasTitle)
        {
            if (nextTitleText == null)
            {
                string line = string.IsNullOrEmpty(nextSubtitle) ? nextTitle : nextTitle + ": " + nextSubtitle;
                nextTitleText = ChapterTitleStyle.Create(transform, line, 310);
            }
            float appear = Mathf.Clamp01((t - whiteSeconds - 0.5f) / 1f);
            float linger = revealAt >= 0f ? Mathf.Clamp01(1f - (Time.time - revealAt - 3f) / 1.5f) : 1f;
            nextTitleText.color = ChapterTitleStyle.WithAlpha(appear * linger);
        }
        if (windSource != null) windSource.volume = Mathf.Lerp(windSource.volume, 0f, Time.deltaTime);
    }

    private void BuildOverlay()
    {
        var canvasGo = new GameObject("ChapterEndOverlay");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 300;
        var scaler = canvasGo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        white = FullImage(canvasGo.transform, "White");
        black = FullImage(canvasGo.transform, "Black");
        titleText = Label(canvasGo.transform, title, GameFonts.Title, 110, new Vector2(0, 60));
        subText = Label(canvasGo.transform, subtitle, GameFonts.Body, 40, new Vector2(0, -40));
        footText = Label(canvasGo.transform, footer, GameFonts.Body, 30, new Vector2(0, -150));
    }

    private static Image FullImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>(); img.raycastTarget = false; img.color = new Color(0, 0, 0, 0);
        return img;
    }

    private static Text Label(Transform parent, string text, Font font, int size, Vector2 pos)
    {
        var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(1600, 200);
        var t = go.AddComponent<Text>(); t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.text = text; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.color = new Color(1, 1, 1, 0);
        return t;
    }

    public override void OnDestroy()
    {
        if (endTime >= 0f) PlayerController.InputLocked = false;
        base.OnDestroy();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.95f, 0.6f, 0.3f);
        Gizmos.DrawCube(areaCenter, areaSize);
    }
}
