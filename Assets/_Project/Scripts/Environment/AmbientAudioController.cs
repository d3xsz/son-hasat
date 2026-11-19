using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-24: Ada Öfkesi'ne göre katmanlı ortam sesi (PROJECT.md tablosu). Yalnızca yerel olarak çalışır (herkes kendi kulağıyla duyar).
/// Katmanlar: yağmur ve rüzgar (WeatherController'dan), gök gürültüsü (şimşekten kısa gecikmeyle), mağaradan gelen alçak uğultu (Öfke 60+),
/// hayvan sesleri (Öfke 20+, seviyeyle sıklaşır ve yükselir; 3B, oyuncunun çevresinde rastgele yönden).
/// Ses dosyaları: gerçek sesler alanlara atanınca onlar kullanılır. Yağmur/rüzgar/gök gürültüsü/uğultu boşsa kodla üretilen sade placeholder
/// kullanılır; HAYVAN SESLERİ boşsa sessiz kalır (placeholder üretilmez).
/// </summary>
public class AmbientAudioController : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private WeatherController weather;
    [SerializeField] private AudioSource rainSource;
    [SerializeField] private AudioSource windSource;
    [SerializeField] private AudioSource droneSource;
    [SerializeField] private AudioSource thunderSource;
    [SerializeField] private AudioSource animalSource;

    [Header("Sesler (boşsa placeholder; hayvanlar boşsa sessiz)")]
    [SerializeField] private AudioClip rainClip;
    [SerializeField] private AudioClip windClip;
    [SerializeField] private AudioClip droneClip;
    [SerializeField] private AudioClip thunderClip;
    [SerializeField] private AudioClip[] animalClips;

    [Header("Seviyeler")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float rainMax = 0.5f;
    [SerializeField, Range(0f, 1f)] private float windMax = 0.4f;
    [SerializeField, Range(0f, 1f)] private float droneMax = 0.3f;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float animalVolume = 0.7f;
    [Tooltip("Katman ses seviyelerinin hedefe yaklaşma hızı (1/sn).")]
    [SerializeField] private float fadeSpeed = 0.4f;

    [Header("Hayvan sesi aralığı (sn), Öfke seviyesi 0-5")]
    [SerializeField] private float[] animalMinInterval = { 0f, 30f, 20f, 12f, 7f, 5f };
    [SerializeField] private float[] animalMaxInterval = { 0f, 50f, 35f, 22f, 13f, 9f };

    private float nextAnimal = -1f;

    private void Awake()
    {
        if (rainClip == null) rainClip = PlaceholderSounds.CreateRainLoop();
        if (windClip == null) windClip = PlaceholderSounds.CreateWindLoop();
        if (droneClip == null) droneClip = PlaceholderSounds.CreateDroneLoop();
        if (thunderClip == null) thunderClip = PlaceholderSounds.CreateThunder();

        StartLoop(rainSource, rainClip);
        StartLoop(windSource, windClip);
        StartLoop(droneSource, droneClip);
    }

    private static void StartLoop(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;
        source.clip = clip;
        source.loop = true;
        source.volume = 0f;
        source.Play();
    }

    private void OnEnable() { if (weather != null) weather.Lightning += OnLightning; }
    private void OnDisable() { if (weather != null) weather.Lightning -= OnLightning; }

    private int Level => WrathSystem.Instance != null ? Mathf.Clamp(WrathSystem.Instance.Level, 0, 5) : 0;

    private void Update()
    {
        float dt = Time.deltaTime;
        float rainTarget = weather != null ? Mathf.Clamp01(weather.RainRate / 1500f) * rainMax : 0f;
        float windTarget = weather != null ? Mathf.Pow(Mathf.Clamp01(weather.Wind / 1.2f), 1.5f) * windMax : 0f; // düşük rüzgarda çok kısık, fırtınada tam
        int level = Level;
        float droneTarget = level >= 3 ? ((level - 2) / 3f) * droneMax : 0f;

        Fade(rainSource, rainTarget * masterVolume, dt);
        Fade(windSource, windTarget * masterVolume, dt);
        Fade(droneSource, droneTarget * masterVolume, dt);

        UpdateAnimals(level, dt);
    }

    private void Fade(AudioSource source, float target, float dt)
    {
        if (source == null) return;
        source.volume = Mathf.MoveTowards(source.volume, target, fadeSpeed * dt);
    }

    private void UpdateAnimals(int level, float dt)
    {
        // KULLANICI (cila): mağaraya girince (çöküş başladı / mağara ağzında sessizlik / yer altı) hayvan sesleri kesilir
        Transform listener = GetLocalPlayer();
        bool inCave = WeatherController.ForceCalm || (listener != null && listener.position.y < -3f);
        if (inCave && animalSource != null && animalSource.isPlaying) animalSource.Stop();

        if (inCave || animalSource == null || animalClips == null || animalClips.Length == 0 || level < 1 || level >= animalMaxInterval.Length || animalMaxInterval[level] <= 0f)
        {
            nextAnimal = -1f;
            return;
        }
        if (nextAnimal < 0f) nextAnimal = Random.Range(animalMinInterval[level], animalMaxInterval[level]);
        nextAnimal -= dt;
        if (nextAnimal > 0f) return;

        AudioClip clip = animalClips[Random.Range(0, animalClips.Length)];
        Transform player = GetLocalPlayer();
        if (clip != null && player != null)
        {
            float angle = Random.value * 360f;
            float dist = Random.Range(8f, 18f);
            animalSource.transform.position = player.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist + Vector3.up;
            animalSource.pitch = Random.Range(0.9f, 1.1f);
            animalSource.PlayOneShot(clip, animalVolume * masterVolume * Mathf.Lerp(0.5f, 1f, level / 5f));
        }
        nextAnimal = Random.Range(animalMinInterval[level], animalMaxInterval[level]);
    }

    private void OnLightning(float strength)
    {
        if (thunderSource == null || thunderClip == null) return;
        StartCoroutine(ThunderAfter(Random.Range(0.4f, 1.8f), strength));
    }

    private IEnumerator ThunderAfter(float delay, float strength)
    {
        yield return new WaitForSeconds(delay);
        thunderSource.pitch = Random.Range(0.85f, 1.05f);
        thunderSource.PlayOneShot(thunderClip, Mathf.Clamp01(thunderVolume * masterVolume * strength * 0.7f));
    }

    private static Transform GetLocalPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.transform;
    }
}
