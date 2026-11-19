using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// G-16: Zehir evre efektleri (GDD 6.4). SADECE yerel oyuncuda çalışır (NetworkPlayer sahibe özel listesinde).
/// Evre 1: öksürük + ekran kenarları yeşil. Evre 2: görüş bulanık (hareket yavaşlaması PoisonSystem.SpeedMultiplier ile).
/// Evre 3: ekran titremesi + hayal sesleri (+ film grain). Yeşil renk kayması zehirle birlikte artar.
/// URP Volume parametreleri her karede zehir değerine göre sürülür; zehir 0 iken Volume ağırlığı 0'dır.
/// </summary>
[RequireComponent(typeof(PoisonSystem))]
public class PoisonEffects : MonoBehaviour
{
    [Header("Görsel (URP Volume)")]
    [SerializeField] private Volume volume;
    [SerializeField] private Color tintColor = new Color(0.72f, 1f, 0.66f);
    [SerializeField] private Color vignetteColor = new Color(0.1f, 0.45f, 0.08f);
    [Tooltip("Görsellerin zehir değerini takip hızı (değer/sn). Evre geçişlerini yumuşatır.")]
    [SerializeField] private float smoothingSpeed = 30f;

    [Header("Titreme (evre 3)")]
    [SerializeField] private float tremorYaw = 0.35f;
    [SerializeField] private float tremorRoll = 0.9f;
    [SerializeField] private float tremorSpeed = 8f;

    [Header("Sesler (placeholder, G-24'te gerçek seslerle değişecek)")]
    [SerializeField] private AudioSource coughSource;
    [SerializeField] private AudioSource whisperSource;
    [Tooltip("Boşsa kodla üretilen placeholder kullanılır.")]
    [SerializeField] private AudioClip coughClip;
    [Tooltip("KULLANICI (cila): gerçek öksürük kayıtları; doluysa her seferinde rastgele biri çalar (aynı ses tekrar etmez).")]
    [SerializeField] private AudioClip[] coughClips;
    [SerializeField] private AudioClip whisperClip;
    private int lastCough = -1;

    [Header("Rüya (Bölüm 3): gösterge zehir değil UYKU")]
    [Tooltip("KULLANICI (2026-11-15): rüyada öksürük ve yeşil kenar olmasın. Renk kayması soluk mor, kenarlar koyu, titreme yerine ağır salınım.")]
    [SerializeField] private Color dreamTintColor = new Color(0.84f, 0.78f, 1f);
    [SerializeField] private Color dreamVignetteColor = new Color(0.04f, 0.02f, 0.08f);
    [SerializeField] private float dreamSwayRoll = 2.4f;
    [SerializeField] private float dreamSwayYaw = 0.9f;

    /// <summary>Oyuncu rüyalar aleminde mi (zehir göstergesi uyku olarak kullanılır; efektler değişir).</summary>
    private bool InDream => FreefallSequence.Instance != null && FreefallSequence.InColumn(transform.position);

    private PoisonSystem poison;
    private PlayerController controller;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;
    private DepthOfField depthOfField;
    private FilmGrain filmGrain;
    private bool profileOk;

    private float shown;
    private float weight;
    private float tremorAmount;
    private float nextCough;
    private float nextWhisper;

    private void Awake()
    {
        poison = GetComponent<PoisonSystem>();
        controller = GetComponent<PlayerController>();

        if (volume != null)
        {
            profileOk = volume.profile.TryGet(out vignette)
                        && volume.profile.TryGet(out colorAdjustments)
                        && volume.profile.TryGet(out depthOfField)
                        && volume.profile.TryGet(out filmGrain);
        }
        if (!profileOk) Debug.LogWarning("[Zehir] Volume profili eksik (Vignette/ColorAdjustments/DepthOfField/FilmGrain).");

        if (coughClip == null && (coughClips == null || coughClips.Length == 0)) coughClip = PlaceholderSounds.CreateCough();
        if (whisperClip == null) whisperClip = PlaceholderSounds.CreateWhisper();
    }

    private void OnEnable()
    {
        shown = poison.Value;
        weight = 0f;
        if (volume != null) { volume.weight = 0f; volume.enabled = true; }
        nextCough = Time.time + 6f;
        nextWhisper = Time.time + 8f;
    }

    private void OnDisable()
    {
        if (volume != null) { volume.weight = 0f; volume.enabled = false; }
        if (controller != null) controller.TremorEuler = Vector3.zero;
    }

    private void Update()
    {
        shown = Mathf.MoveTowards(shown, poison.Value, smoothingSpeed * Time.deltaTime);
        if (profileOk) ApplyVisuals(shown);
        UpdateTremor();
        UpdateSounds();
    }

    private void ApplyVisuals(float v)
    {
        float s1 = poison.Stage1Threshold;
        float s2 = poison.Stage2Threshold;
        float s3 = poison.Stage3Threshold;
        float max = PoisonSystem.MaxPoison;

        weight = Mathf.MoveTowards(weight, v > 0.01f ? 1f : 0f, 2f * Time.deltaTime);
        volume.weight = weight;

        // Evre 1+: kenarlar yeşil
        float vig = Mathf.Lerp(0f, 0.25f, Mathf.Clamp01(v / s1)) + Mathf.Lerp(0f, 0.35f, Mathf.InverseLerp(s1, max, v));
        vignette.intensity.value = vig;
        vignette.color.value = InDream ? dreamVignetteColor : vignetteColor;
        vignette.smoothness.value = 0.6f;

        // Yeşil renk kayması (zehirle sürekli artar)
        float t = Mathf.Clamp01(v / max);
        colorAdjustments.colorFilter.value = Color.Lerp(Color.white, InDream ? dreamTintColor : tintColor, t);
        colorAdjustments.saturation.value = -40f * t;

        // Evre 2+: görüş bulanık
        float blur = Mathf.Clamp01((v - s2) / 8f) * Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(s2, max, v));
        depthOfField.active = blur > 0.001f;
        depthOfField.mode.value = DepthOfFieldMode.Gaussian;
        depthOfField.gaussianStart.value = 2f;
        depthOfField.gaussianEnd.value = Mathf.Lerp(45f, 8f, blur);
        depthOfField.gaussianMaxRadius.value = 1.5f;

        // Evre 3: hafif film grain
        float grain = Mathf.Clamp01((v - s3) / 8f) * Mathf.Lerp(0.25f, 0.7f, Mathf.InverseLerp(s3, max, v));
        filmGrain.active = grain > 0.001f;
        filmGrain.intensity.value = grain;
    }

    private void UpdateTremor()
    {
        if (controller == null) return;
        bool dream = InDream;
        // rüyada titreme yok: evre 2'den itibaren başın ağır ağır sallanması (uykulu)
        float target = dream ? Mathf.InverseLerp(poison.Stage2Threshold, PoisonSystem.MaxPoison, poison.Value)
                             : poison.Stage == PoisonSystem.PoisonStage.Stage3 ? 1f : 0f;
        tremorAmount = Mathf.MoveTowards(tremorAmount, target, 0.6f * Time.deltaTime);
        float time = Time.time * (dream ? 0.55f : tremorSpeed);
        float yaw = (Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * (dream ? dreamSwayYaw : tremorYaw) * tremorAmount;
        float roll = (Mathf.PerlinNoise(4.1f, time) - 0.5f) * 2f * (dream ? dreamSwayRoll : tremorRoll) * tremorAmount;
        controller.TremorEuler = new Vector3(0f, yaw, roll);
    }

    private void UpdateSounds()
    {
        PoisonSystem.PoisonStage stage = poison.Stage;
        bool sick = stage >= PoisonSystem.PoisonStage.Stage1 && stage <= PoisonSystem.PoisonStage.Stage3;
        if (InDream) sick = false;      // rüyada öksürük yok (uyku, zehir değil)

        if (!sick) nextCough = Time.time + 6f;
        else if (Time.time >= nextCough)
        {
            PlayCough(stage);
            float min = stage == PoisonSystem.PoisonStage.Stage1 ? 10f : stage == PoisonSystem.PoisonStage.Stage2 ? 7f : 4f;
            nextCough = Time.time + Random.Range(min, min + 5f);
        }

        if (stage != PoisonSystem.PoisonStage.Stage3) nextWhisper = Time.time + 8f;
        else if (Time.time >= nextWhisper)
        {
            PlayWhisper();
            nextWhisper = Time.time + Random.Range(12f, 22f);
        }
    }

    private void PlayCough(PoisonSystem.PoisonStage stage)
    {
        if (coughSource == null) return;
        AudioClip clip = coughClip;
        if (coughClips != null && coughClips.Length > 0)
        {
            int i = Random.Range(0, coughClips.Length);
            if (coughClips.Length > 1 && i == lastCough) i = (i + 1) % coughClips.Length;
            lastCough = i;
            clip = coughClips[i];
        }
        if (clip == null) return;
        coughSource.pitch = Random.Range(0.95f, 1.05f);
        float vol = stage == PoisonSystem.PoisonStage.Stage1 ? 0.35f : stage == PoisonSystem.PoisonStage.Stage2 ? 0.5f : 0.65f;
        coughSource.PlayOneShot(clip, vol);
    }

    private void PlayWhisper()
    {
        if (whisperSource == null || whisperClip == null) return;
        float angle = Random.value * 360f;
        float dist = Random.Range(5f, 9f);
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist;
        whisperSource.transform.position = transform.position + offset + Vector3.up * 1.6f;
        whisperSource.PlayOneShot(whisperClip, 0.5f);
    }
}
