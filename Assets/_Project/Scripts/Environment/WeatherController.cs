using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-23: Hava durumu. Ada Öfkesi seviyesine göre (PROJECT.md tablosu) yağmur, rüzgar, sis yoğunluğu ve şimşek.
/// Yağmur/sis/rüzgar her kopyada Öfke seviyesinden türetilir (kesin yumuşak geçişle); şimşek zamanlamasını SUNUCU belirler ve herkese gönderir.
/// Sahne içi NetworkObject. Yağmur yerel oyuncunun tepesinde (12 m yukarıda) yağar ve ona eşlik eder.
/// Ses (yağmur, gök gürültüsü, rüzgar) G-24'te eklenecek.
/// </summary>
public class WeatherController : NetworkBehaviour
{
    [Serializable]
    public struct WeatherLevel
    {
        [Tooltip("Yağmur parçacığı sayısı (saniyede).")] public float rainRate;
        [Tooltip("Rüzgar gücü (0-2): yağmuru yatırır, ileride ağaç sallanması.")] public float wind;
        [Tooltip("Temel sis yoğunluğu çarpanı.")] public float fogMultiplier;
        [Tooltip("Şimşekler arası en kısa süre (sn); 0 = şimşek yok.")] public float lightningMin;
        public float lightningMax;
    }

    [Header("Referanslar")]
    [SerializeField] private ParticleSystem rain;
    [SerializeField] private Light lightningLight;

    [Header("Sis")]
    [Tooltip("Öfke 0'daki sis yoğunluğu (Exponential Squared).")]
    [SerializeField] private float baseFogDensity = 0.14f;

    [Header("Öfke seviyesine göre hava (0-5, PROJECT.md tablosu)")]
    [SerializeField] private WeatherLevel[] levels = DefaultLevels();

    [Header("Geçiş hızları (yumuşak değişim)")]
    [SerializeField] private float rainChangePerSecond = 400f;
    [SerializeField] private float windChangePerSecond = 0.3f;
    [SerializeField] private float fogChangePerSecond = 0.15f;

    [Header("Şimşek")]
    [SerializeField] private Color flashColor = new Color(0.7f, 0.8f, 1f);
    [SerializeField] private float flashPeak = 2.5f;
    [SerializeField] private float flashAmbientBoost = 0.12f;

    [Header("Test")]
    [Tooltip("0-5: bu kopyada Öfke seviyesini yok sayıp bu seviyeyi uygular. -1 = kapalı (gerçek Öfke).")]
    [SerializeField] private int debugLevelOverride = -1;

    private float rainRate;
    private float wind;
    private float fogMultiplier = 1f;
    private float nextFlash = -1f;
    private float flashElapsed = -1f;
    private float flashStrength;
    private Color baseAmbient;

    public float Wind => wind;

    /// <summary>Tüm kopyalarda: şimşek çaktığında (güç 0.8-1.4). Ses (gök gürültüsü) buna bağlanır.</summary>
    public event Action<float> Lightning;
    public float RainRate => rainRate;

    private static WeatherLevel[] DefaultLevels()
    {
        return new[]
        {
            new WeatherLevel { rainRate = 0f,    wind = 0.2f, fogMultiplier = 1.0f, lightningMin = 0f,  lightningMax = 0f },
            new WeatherLevel { rainRate = 120f,  wind = 0.35f, fogMultiplier = 1.0f, lightningMin = 0f,  lightningMax = 0f },
            new WeatherLevel { rainRate = 500f,  wind = 0.6f, fogMultiplier = 1.0f, lightningMin = 14f, lightningMax = 28f },
            new WeatherLevel { rainRate = 800f,  wind = 0.8f, fogMultiplier = 1.2f, lightningMin = 9f,  lightningMax = 18f },
            new WeatherLevel { rainRate = 1500f, wind = 1.2f, fogMultiplier = 1.5f, lightningMin = 5f,  lightningMax = 11f },
            new WeatherLevel { rainRate = 1500f, wind = 1.2f, fogMultiplier = 1.5f, lightningMin = 5f,  lightningMax = 11f }
        };
    }

    private int CurrentLevel
    {
        get
        {
            int lvl = debugLevelOverride >= 0 ? debugLevelOverride : (WrathSystem.Instance != null ? WrathSystem.Instance.Level : 0);
            return Mathf.Clamp(lvl, 0, levels.Length - 1);
        }
    }

    public override void OnNetworkSpawn()
    {
        baseAmbient = RenderSettings.ambientLight;
        if (lightningLight != null) lightningLight.enabled = false;
    }

    public override void OnNetworkDespawn() { Restore(); }
    public override void OnDestroy() { Restore(); base.OnDestroy(); }

    private void Restore()
    {
        if (RenderSettings.fog) RenderSettings.fogDensity = baseFogDensity;
        RenderSettings.ambientLight = baseAmbient;
    }

    private void Update()
    {
        WeatherLevel target = ForceStorm ? levels[levels.Length - 1] : levels[CurrentLevel];
        float dt = Time.deltaTime;
        bool calm = ForceCalm;
        rainRate = calm ? Mathf.MoveTowards(rainRate, 0f, rainChangePerSecond * 4f * dt) : Mathf.MoveTowards(rainRate, target.rainRate, rainChangePerSecond * dt);
        wind = calm ? Mathf.MoveTowards(wind, 0f, windChangePerSecond * 4f * dt) : Mathf.MoveTowards(wind, target.wind, windChangePerSecond * dt);
        fogMultiplier = Mathf.MoveTowards(fogMultiplier, target.fogMultiplier, fogChangePerSecond * dt);

        if (RenderSettings.fog) RenderSettings.fogDensity = FogDensityOverride >= 0f ? FogDensityOverride : baseFogDensity * fogMultiplier;
        UpdateRain();
        UpdateFlash(dt);
        if (IsServer && IsSpawned && !calm) ServerLightning(target, dt);
    }

    /// <summary>G-29: mağara ağzında yağmur ve rüzgar hızla durur, şimşek kesilir ("ani sessizlik"). CollapseSequence yazar.</summary>
    public static bool ForceCalm { get; set; }

    /// <summary>KULLANICI: son totemden sonra (taş yağmuru) yağmur ve rüzgâr en şiddetli seviyede kalır. RockRain yazar.</summary>
    public static bool ForceStorm { get; set; }

    /// <summary>G-33: giriş sinematiği sırasında sis yoğunluğu (negatifse kapalı). IntroCinematic yazar.</summary>
    public static float FogDensityOverride { get; set; } = -1f;

    private void UpdateRain()
    {
        if (rain == null) return;
        Transform player = GetLocalPlayer();
        if (player != null) rain.transform.position = player.position + Vector3.up * 12f;

        ParticleSystem.EmissionModule em = rain.emission;
        em.rateOverTime = player != null ? rainRate : 0f;

        ParticleSystem.ForceOverLifetimeModule force = rain.forceOverLifetime;
        force.x = new ParticleSystem.MinMaxCurve(wind * 6f);
        force.y = new ParticleSystem.MinMaxCurve(0f);
        force.z = new ParticleSystem.MinMaxCurve(wind * 2f);
    }

    // ---------- Şimşek ----------

    private void ServerLightning(WeatherLevel target, float dt)
    {
        if (target.lightningMax <= 0f) { nextFlash = -1f; return; }
        if (nextFlash < 0f) nextFlash = UnityEngine.Random.Range(target.lightningMin, target.lightningMax);
        nextFlash -= dt;
        if (nextFlash <= 0f)
        {
            FlashRpc(UnityEngine.Random.Range(0.8f, 1.4f), UnityEngine.Random.Range(0f, 360f));
            nextFlash = UnityEngine.Random.Range(target.lightningMin, target.lightningMax);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void FlashRpc(float strength, float yaw)
    {
        flashStrength = strength;
        flashElapsed = 0f;
        Lightning?.Invoke(strength);
        if (lightningLight != null) lightningLight.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(40f, 70f), yaw, 0f);
    }

    private void UpdateFlash(float dt)
    {
        float intensity = 0f;
        if (flashElapsed >= 0f)
        {
            float t = flashElapsed;
            intensity = Mathf.Max(0f, 1f - Mathf.Abs(t - 0.05f) / 0.05f) + 0.7f * Mathf.Max(0f, 1f - Mathf.Abs(t - 0.2f) / 0.1f);
            flashElapsed += dt;
            if (flashElapsed > 0.45f) flashElapsed = -1f;
        }

        float k = Mathf.Clamp01(intensity) * flashStrength;
        if (lightningLight != null)
        {
            lightningLight.color = flashColor;
            lightningLight.intensity = flashPeak * k;
            lightningLight.enabled = k > 0.01f;
        }
        RenderSettings.ambientLight = baseAmbient + flashColor * (flashAmbientBoost * k);
    }

    private static Transform GetLocalPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.transform;
    }
}
