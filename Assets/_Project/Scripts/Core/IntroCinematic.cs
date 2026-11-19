using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

public enum IntroState : byte { Pending = 0, Playing = 1, Done = 2 }

/// <summary>
/// G-33: Tekne giriş sinematiği (GDD 6.2). Timeline (PlayableDirector) + Cinemachine kameraları.
/// Sunucu bölüm sahnesi açılınca başlangıç zamanını belirler; her kopya Timeline'ı sunucu saatine göre elle
/// değerlendirir (herkeste aynı kare). Atlama: her oyuncu [E]'ye basar; HERKES bastıysa sunucu bitirir.
/// Oynarken yerel oyuncu kontrolü ve HUD kapalı, sis inceltilir; bitince giriş nesneleri gizlenir.
/// </summary>
public class IntroCinematic : NetworkBehaviour
{
    [SerializeField] private PlayableDirector director;
    [SerializeField] private Camera introCamera;
    [Tooltip("Yalnızca sinematik sırasında görünen nesneler (tekne, goblinler, ışıklar).")]
    [SerializeField] private GameObject introRoot;
    [Tooltip("M-05: sinematik sırasında gizli, sonra görünen nesneler (ör. iskele ucundaki batık tekne; yaklaşan tekneyle aynı anda görünmesin).")]
    [SerializeField] private GameObject[] hideDuringIntro = new GameObject[0];
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private bool playIntro = true;
    [SerializeField] private float startDelay = 1f;
    [Tooltip("Sinematiğin bittiği an (sn); 0 ise Timeline sonuna kadar. KULLANICI KARARI: tekne çarpınca biter.")]
    [SerializeField] private float endTime = 29.5f;
    [Tooltip("Teknedeki karakterler (sırayla); yalnızca bağlı oyuncu sayısı kadarı görünür.")]
    [SerializeField] private GameObject[] boatCrew = new GameObject[0];
    [SerializeField] private float introFogDensity = 0.035f;

    [Header("Ekran")]
    [SerializeField] private string title = "SON HASAT\n<size=40>Bölüm 1: Sisli Ada</size>";
    [SerializeField] private float titleStart = 27.8f;
    [SerializeField] private float titleEnd = 30.5f;
    [SerializeField] private float fadeSeconds = 1.6f;
    [Tooltip("KULLANICI (2026-11-15): Bölüm 2 ve 3 ile aynı biçimde bölüm başlığı; tekne çarpınca siyah ekranda gösterilir. (Eski iki satırlı 'title' artık kullanılmıyor.)")]
    [SerializeField] private string chapterTitle = "BÖLÜM 1: Solgun Topraklar";
    [Tooltip("Sinematik bitince başlığın siyah ekranda kaldığı süre (sn).")]
    [SerializeField] private float chapterTitleHold = 2.8f;
    private float titleHoldUntil = -1f;
    private bool HoldingTitle => titleHoldUntil >= 0f && Time.time < titleHoldUntil;

    public static bool Playing { get; private set; }

    private readonly NetworkVariable<IntroState> state = new NetworkVariable<IntroState>(IntroState.Pending);
    private readonly NetworkVariable<double> startTime = new NetworkVariable<double>(0);
    private readonly NetworkVariable<int> skipVotes = new NetworkVariable<int>(0);
    private readonly HashSet<ulong> voters = new HashSet<ulong>();

    private InputAction interactAction;
    private bool voted;
    private Canvas hudCanvas;
    private Image fadeImage;
    private Text titleText, skipText;
    private float wakeStart = -1f;
    private bool wasPlaying;
    [SerializeField] private Color hazeColor = new Color(0.05f, 0.06f, 0.08f);

    /// <summary>Uyanma pusu (0 = açık, 1 = tam koyu) zamana göre: puslu açılış, 2 hafif kararma, açılış. Hiç tam kararmaz.</summary>
    private static readonly AnimationCurve WakeCurve = new AnimationCurve(
        new Keyframe(0f, 0.8f), new Keyframe(0.8f, 0.3f), new Keyframe(1.15f, 0.65f),
        new Keyframe(1.8f, 0.2f), new Keyframe(2.15f, 0.55f), new Keyframe(3.2f, 0f));
    private bool bodiesHidden;

    private double Duration => director != null && director.playableAsset != null
        ? (endTime > 0f ? System.Math.Min(director.duration, endTime) : director.duration) : 0;
    private float LocalTime => IsSpawned ? (float)(NetworkManager.ServerTime.Time - startTime.Value) : 0f;

    private void Awake()
    {
        if (inputActions != null) interactAction = inputActions.FindActionMap("Player", true).FindAction("Interact", true);
        if (director != null)
        {
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
        }
        if (introCamera != null) introCamera.gameObject.SetActive(false);
        if (introRoot != null) introRoot.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // KULLANICI: sinematik, herkes sahneyi yükleyip yükleme ekranı kapanınca başlar (ServerBegin).
            // Bekleme (Pending) sırasında giriş kilitli, sinematik kamerası hazır; uzun sürerse kendiliğinden başlar.
            if (playIntro && director != null && director.playableAsset != null) { state.Value = IntroState.Pending; pendingSince = Time.time; }
            else state.Value = IntroState.Done;
        }
        Instance = this;
    }

    public static IntroCinematic Instance { get; private set; }
    private float pendingSince = -1f;
    [Tooltip("Sahne yüklemesi bitince sinematiğe kadar bekleme (yükleme ekranının kapanma süresi).")]
    [SerializeField] private float afterLoadDelay = 1.6f;

    /// <summary>Sunucu: herkes sahneyi yükledi (ConnectionManager) → yükleme ekranı kapanırken sinematik başlar.</summary>
    public void ServerBegin(float delay = -1f)
    {
        if (!IsServer || state.Value != IntroState.Pending) return;
        startTime.Value = NetworkManager.ServerTime.Time + (delay >= 0f ? delay : afterLoadDelay) + startDelay;
        state.Value = IntroState.Playing;
        pendingSince = -1f;
    }

    /// <summary>Sunucu: giriş sinematiğini hiç oynatmadan bitirir (bölüm seçimiyle Bölüm 2/3'ten başlarken).</summary>
    public void ServerSkip()
    {
        if (!IsServer) return;
        pendingSince = -1f;
        if (state.Value != IntroState.Done) state.Value = IntroState.Done;
    }

    public override void OnNetworkDespawn() { Cleanup(); }
    public override void OnDestroy() { Cleanup(); base.OnDestroy(); }

    private void Cleanup()
    {
        if (Instance == this) Instance = null;
        Playing = false;
        PlayerController.InputLocked = false;
        WeatherController.FogDensityOverride = -1f;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SkipRpc(RpcParams rpcParams = default)
    {
        if (state.Value != IntroState.Playing) return;
        if (voters.Add(rpcParams.Receive.SenderClientId)) skipVotes.Value = voters.Count;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        // güvenlik: yükleme olayı gelmezse (ör. doğrudan sahneden test) 15 sn sonra başla
        if (IsServer && state.Value == IntroState.Pending && pendingSince >= 0f && Time.time - pendingSince > 15f) ServerBegin();
        bool playing = state.Value == IntroState.Playing;
        // sunucu bitiş: süre doldu ya da herkes atlamak istedi
        if (IsServer && playing)
        {
            int players = NetworkManager.ConnectedClientsIds.Count;
            if (LocalTime >= Duration || (players > 0 && skipVotes.Value >= players)) state.Value = IntroState.Done;
        }

        Playing = playing || state.Value == IntroState.Pending;
        PlayerController.InputLocked = Playing || HoldingTitle;
        WeatherController.FogDensityOverride = Playing ? introFogDensity : -1f;
        if (introCamera != null && introCamera.gameObject.activeSelf != Playing) introCamera.gameObject.SetActive(Playing);
        if (introRoot != null && introRoot.activeSelf != Playing) introRoot.SetActive(Playing);
        foreach (GameObject g in hideDuringIntro) if (g != null && g.activeSelf == Playing) g.SetActive(!Playing);
        SetHudVisible(!Playing && !HoldingTitle);
        // kıyıda bekleyen gerçek oyuncu gövdeleri sinematikte görünmesin (teknedeki goblinler onların yerine)
        if (Playing || bodiesHidden)
        {
            foreach (PlayerLife p in PlayerLife.All)
            {
                NetworkPlayer np = p != null ? p.GetComponent<NetworkPlayer>() : null;
                if (np != null) np.SetSpectated(Playing);
            }
            bodiesHidden = Playing;
        }
        EnsureOverlay();

        if (playing)
        {
            // teknede bağlı oyuncu sayısı kadar karakter
            int crew = Mathf.Clamp(PlayerLife.All.Count, 1, Mathf.Max(1, boatCrew.Length));
            for (int i = 0; i < boatCrew.Length; i++)
                if (boatCrew[i] != null && boatCrew[i].activeSelf != (i < crew)) boatCrew[i].SetActive(i < crew);
            float t = LocalTime;
            if (director != null)
            {
                director.time = Mathf.Clamp(t, 0f, (float)Duration);
                director.Evaluate();
            }
            UpdateIntroAudio(t, true);
            float fadeIn = 1f - Mathf.Clamp01(t / fadeSeconds);
            float fadeOut = Mathf.Clamp01((t - ((float)Duration - fadeSeconds)) / fadeSeconds);
            SetFade(Mathf.Max(fadeIn, fadeOut));
            // başlık, tekne çarpıp ekran kararırken belirir; sinematik bitince siyah ekranda bir süre kalır (aşağıda)
            titleText.color = ChapterTitleStyle.WithAlpha(Mathf.Clamp01((t - titleStart) / 1f));

            if (!voted && interactAction != null && interactAction.WasPressedThisFrame())
            {
                voted = true;
                SkipRpc();
            }
            int total = Mathf.Max(1, PlayerLife.All.Count);
            skipText.text = t > 0.5f ? "[E] Atla  " + skipVotes.Value + "/" + total : "";
            wasPlaying = true;
        }
        else if (state.Value == IntroState.Done)
        {
            UpdateIntroAudio(-1f, false);
            // kıyıya fırlayan goblin kendine gelir: ekran 2 kez hafifçe, puslu kararıp açılır (yalnızca bir kez oynar)
            if (wasPlaying) { wasPlaying = false; titleHoldUntil = Time.time + chapterTitleHold; }
            skipText.text = "";
            if (titleHoldUntil >= 0f)
            {
                // siyah ekran + başlık (atlanırsa da gösterilir); sürenin sonunda başlık söner, ardından uyanma pusu başlar
                float left = titleHoldUntil - Time.time;
                if (left > 0f)
                {
                    SetFade(1f);
                    titleText.color = ChapterTitleStyle.WithAlpha(Mathf.Min(titleText.color.a + Time.deltaTime, 1f) * Mathf.Clamp01(left / 0.8f));
                    return;
                }
                titleHoldUntil = -1f;
                wakeStart = Time.time;
            }
            float haze = wakeStart >= 0f ? WakeCurve.Evaluate(Time.time - wakeStart) : 0f;
            SetHaze(haze);
            if (wakeStart >= 0f && Time.time - wakeStart > WakeCurve.keys[WakeCurve.length - 1].time) wakeStart = -1f;
            titleText.color = Color.clear;
        }
        else SetFade(1f); // Pending: siyah ekran
    }

    // ---------- KULLANICI (cila): sinematik sesi ----------
    [Header("Ses")]
    [Tooltip("Teknenin kayalara çarptığı an (sn, Timeline'daki baş kalkışı).")]
    [SerializeField] private float crashTime = 27.3f;
    [SerializeField, Range(0f, 1f)] private float wavesVolume = 0.6f;
    private AudioSource wavesSource;
    private float lastAudioTime = -1f;

    /// <summary>Sinematik boyunca deniz dalgaları (2B döngü, yumuşak giriş/çıkış); çarpma anında tahta çatırtısı + su + taş + gürleme.</summary>
    private void UpdateIntroAudio(float t, bool playing)
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib == null || lib.seaWaves == null) return;
        if (wavesSource == null)
        {
            var go = new GameObject("IntroWaves");
            go.transform.SetParent(transform, false);
            wavesSource = go.AddComponent<AudioSource>();
            wavesSource.clip = lib.seaWaves; wavesSource.loop = true; wavesSource.spatialBlend = 0f; wavesSource.playOnAwake = false; wavesSource.volume = 0f;
        }
        if (playing)
        {
            if (!wavesSource.isPlaying) wavesSource.Play();
            float dur = (float)Duration;
            float target = wavesVolume * Mathf.Clamp01(t / 2.5f);
            if (t > crashTime) target *= 0.7f;                                   // çarpmadan sonra sahil, biraz kısık
            target *= 1f - Mathf.Clamp01((t - (dur - 2f)) / 2f);                // sonda söner
            wavesSource.volume = Mathf.MoveTowards(wavesSource.volume, target, Time.deltaTime * 0.8f);

            if (lastAudioTime >= 0f && lastAudioTime < crashTime && t >= crashTime)
            {
                Sfx.UI(lib.treeCrash, 1f, 0.78f, 0.02f);
                Sfx.UI(lib.splash, 0.85f, 0.9f, 0.03f);
                Sfx.UI(lib.rockHit, 0.9f, 0.55f, 0.03f);
                Sfx.UI(lib.rumble, 0.6f, 0.75f, 0.02f);
            }
            lastAudioTime = t;
        }
        else
        {
            lastAudioTime = -1f;
            if (wavesSource.isPlaying)
            {
                wavesSource.volume = Mathf.MoveTowards(wavesSource.volume, 0f, Time.deltaTime * 0.5f);
                if (wavesSource.volume <= 0.001f) wavesSource.Stop();
            }
        }
    }

    private void SetHudVisible(bool visible)
    {
        if (hudCanvas == null)
        {
            HudController hud = FindFirstObjectByType<HudController>();
            if (hud != null) hudCanvas = hud.GetComponentInParent<Canvas>();
        }
        if (hudCanvas != null && hudCanvas.enabled != visible) hudCanvas.enabled = visible;
    }

    private void SetHaze(float a)
    {
        if (fadeImage == null) return;
        fadeImage.color = new Color(hazeColor.r, hazeColor.g, hazeColor.b, a);
        fadeImage.enabled = a > 0.001f;
    }

    private void SetFade(float a)
    {
        if (fadeImage == null) return;
        fadeImage.color = new Color(0f, 0f, 0f, a);
        fadeImage.enabled = a > 0.001f;
    }

    private void EnsureOverlay()
    {
        if (fadeImage != null) return;
        GameObject canvasGo = new GameObject("IntroOverlay");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        Font font = GameFonts.Body;

        fadeImage = NewChild<Image>(canvasGo.transform, "Fade");
        fadeImage.raycastTarget = false;
        Stretch(fadeImage.rectTransform);

        // bölüm başlığı: Bölüm 2 ve 3 ile aynı biçim (kendi tuvalinde, karartmanın üstünde)
        titleText = ChapterTitleStyle.Create(transform, chapterTitle, 151);

        skipText = NewChild<Text>(canvasGo.transform, "Skip");
        skipText.font = font; skipText.fontSize = 22; skipText.alignment = TextAnchor.LowerRight;
        skipText.color = new Color(0.85f, 0.85f, 0.85f, 0.6f); skipText.raycastTarget = false;
        RectTransform sr = skipText.rectTransform;
        sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(1f, 0f);
        sr.anchoredPosition = new Vector2(-30f, 24f); sr.sizeDelta = new Vector2(400f, 40f);
    }

    private static T NewChild<T>(Transform parent, string name) where T : Component
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
