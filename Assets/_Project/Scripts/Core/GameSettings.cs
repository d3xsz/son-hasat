using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// KULLANICI (cila): Oyuncu ayarları (fare hassasiyeti, genel / müzik / efekt sesi, parlaklık, grafik kalitesi, tam ekran).
/// PlayerPrefs'te saklanır, oyun açılışında uygulanır. Parlaklık kalıcı, en yüksek öncelikli bir global Volume'daki
/// Lift/Gamma/Gain "gamma" ile verilir (zehir/bayılma efektlerinin kullandığı ColorAdjustments'a dokunmaz).
/// </summary>
public static class GameSettings
{
    private const string KeySens = "set_sensitivity", KeyVol = "set_volume", KeyBright = "set_brightness",
                         KeyQuality = "set_quality", KeyFull = "set_fullscreen", KeyMusic = "set_music", KeySfx = "set_sfx";

    public const float SensitivityMin = 0.3f, SensitivityMax = 2.5f;

    /// <summary>Fare hassasiyeti çarpanı (1 = varsayılan).</summary>
    public static float Sensitivity { get; private set; } = 1f;
    /// <summary>Genel ses (0-1).</summary>
    public static float MasterVolume { get; private set; } = 1f;
    /// <summary>Müzik (0-1). Müzik kaynakları AudioListener'ı yok sayar, Genel×Müzik kullanır (MusicPlayer).</summary>
    public static float MusicVolume { get; private set; } = 0.8f;
    /// <summary>Efektler ve ortam sesleri (0-1). AudioListener.volume = Genel×Efektler.</summary>
    public static float SfxVolume { get; private set; } = 1f;
    /// <summary>Parlaklık (-1 karanlık … 0 varsayılan … +1 aydınlık).</summary>
    public static float Brightness { get; private set; }
    public static int Quality { get; private set; }
    public static bool Fullscreen { get; private set; } = true;

    public static event Action Changed;

    private static LiftGammaGain gamma;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        Sensitivity = PlayerPrefs.GetFloat(KeySens, 1f);
        MasterVolume = PlayerPrefs.GetFloat(KeyVol, 1f);
        MusicVolume = PlayerPrefs.GetFloat(KeyMusic, 0.8f);
        SfxVolume = PlayerPrefs.GetFloat(KeySfx, 1f);
        Brightness = PlayerPrefs.GetFloat(KeyBright, 0f);
        Quality = Mathf.Clamp(PlayerPrefs.GetInt(KeyQuality, QualityLevels - 1), 0, QualityLevels - 1);
        Fullscreen = PlayerPrefs.GetInt(KeyFull, Screen.fullScreen ? 1 : 0) == 1;
        CreateBrightnessVolume();
        CreateRuntimePipeline();
        ApplyAll();
    }

    // ---------- Grafik kalitesi (URP ayarları; proje asset'i değişmesin diye çalışma anı kopyası) ----------

    /// <summary>0 = Düşük, 1 = Orta, 2 = Yüksek.</summary>
    public const int QualityLevels = 3;
    private static UniversalRenderPipelineAsset runtimeUrp;
    private static float baseShadowDistance = 50f;
    private static int baseMsaa = 1;

    private static void CreateRuntimePipeline()
    {
        var src = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (src == null) src = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (src == null) return;
        runtimeUrp = UnityEngine.Object.Instantiate(src);
        runtimeUrp.name = src.name + " (çalışma)";
        baseShadowDistance = src.shadowDistance;
        baseMsaa = src.msaaSampleCount;
        QualitySettings.renderPipeline = runtimeUrp;
        // editörde Play bitince proje ayarına orijinal asset geri yazılsın
        Application.quitting += () => QualitySettings.renderPipeline = src;
    }

    private static void ApplyQuality()
    {
        if (runtimeUrp == null) return;
        switch (Quality)
        {
            case 0: runtimeUrp.renderScale = 0.7f; runtimeUrp.shadowDistance = baseShadowDistance * 0.5f; runtimeUrp.msaaSampleCount = 1; break;
            case 1: runtimeUrp.renderScale = 0.85f; runtimeUrp.shadowDistance = baseShadowDistance * 0.75f; runtimeUrp.msaaSampleCount = Mathf.Min(baseMsaa, 2); break;
            default: runtimeUrp.renderScale = 1f; runtimeUrp.shadowDistance = baseShadowDistance; runtimeUrp.msaaSampleCount = baseMsaa; break;
        }
    }

    public static string QualityName(int q) { return q <= 0 ? "Düşük" : q == 1 ? "Orta" : "Yüksek"; }

    private static void CreateBrightnessVolume()
    {
        var go = new GameObject("BrightnessVolume");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1000f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        gamma = profile.Add<LiftGammaGain>(true);
        vol.sharedProfile = profile;
    }

    private static void ApplyAll()
    {
        AudioListener.volume = MasterVolume * SfxVolume;
        ApplyQuality();
#if !UNITY_EDITOR
        if (Screen.fullScreen != Fullscreen) Screen.fullScreen = Fullscreen;
#endif
        if (gamma != null)
        {
            gamma.gamma.Override(new Vector4(1f, 1f, 1f, Brightness * 0.45f));
            gamma.active = Mathf.Abs(Brightness) > 0.001f;
        }
        Changed?.Invoke();
    }

    public static void SetSensitivity(float v) { Sensitivity = Mathf.Clamp(v, SensitivityMin, SensitivityMax); PlayerPrefs.SetFloat(KeySens, Sensitivity); ApplyAll(); }
    public static void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyVol, MasterVolume); ApplyAll(); }
    public static void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyMusic, MusicVolume); ApplyAll(); }
    public static void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeySfx, SfxVolume); ApplyAll(); }
    public static void SetBrightness(float v) { Brightness = Mathf.Clamp(v, -1f, 1f); PlayerPrefs.SetFloat(KeyBright, Brightness); ApplyAll(); }
    public static void SetQuality(int q) { Quality = Mathf.Clamp(q, 0, QualityLevels - 1); PlayerPrefs.SetInt(KeyQuality, Quality); ApplyAll(); }
    public static void SetFullscreen(bool f) { Fullscreen = f; PlayerPrefs.SetInt(KeyFull, f ? 1 : 0); ApplyAll(); }

    public static void ResetDefaults()
    {
        Sensitivity = 1f; MasterVolume = 1f; MusicVolume = 0.8f; SfxVolume = 1f; Brightness = 0f; Quality = QualityLevels - 1; Fullscreen = true;
        PlayerPrefs.SetFloat(KeySens, Sensitivity); PlayerPrefs.SetFloat(KeyVol, MasterVolume); PlayerPrefs.SetFloat(KeyBright, Brightness);
        PlayerPrefs.SetFloat(KeyMusic, MusicVolume); PlayerPrefs.SetFloat(KeySfx, SfxVolume);
        PlayerPrefs.SetInt(KeyQuality, Quality); PlayerPrefs.SetInt(KeyFull, 1);
        ApplyAll();
    }

    public static void Save() { PlayerPrefs.Save(); }
}
