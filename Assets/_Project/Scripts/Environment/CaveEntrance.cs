using UnityEngine;

/// <summary>
/// G-27: Mağara girişi işaretçisi (G-28'de haritaya gerçek yerleşim, G-29'da çöküş tetikleyicisi buraya bağlanır).
/// Öfke seviyesi 3'ten (60) itibaren kırmızı ışık ve 3B alçak uğultu yayar, 80'de ve 100'de yoğunlaşır: oyuncuyu çeken yön ipucu.
/// Yalnızca yerel görsel/ses (her oyuncu kendi kopyasında).
/// </summary>
public class CaveEntrance : MonoBehaviour
{
    public static CaveEntrance Instance { get; private set; }
    public Vector3 Position => transform.position;

    [Header("Işık (Öfke seviyesi 0-5)")]
    [SerializeField] private Light redLight;
    [SerializeField] private Renderer[] glowRenderers;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(1f, 0.1f, 0.05f);
    [SerializeField] private float[] lightIntensityByLevel = { 0f, 0f, 0f, 6f, 14f, 24f };
    [SerializeField] private float emissionAtMaxLight = 3f;
    [SerializeField] private float flicker = 0.15f;
    [SerializeField] private float fadeSpeed = 6f;

    [Header("Uğultu (3B, mağaradan)")]
    [SerializeField] private AudioSource droneSource;
    [Tooltip("Boşsa kodla üretilen alçak uğultu placeholder'ı kullanılır; kullanıcı gerçek ses atayabilir.")]
    [SerializeField] private AudioClip droneClip;
    [SerializeField] private float[] droneVolumeByLevel = { 0f, 0f, 0f, 0.35f, 0.7f, 1f };
    [SerializeField, Range(0f, 1f)] private float droneMaster = 0.6f;

    private float light01;
    private float droneVolume;
    private MaterialPropertyBlock block;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (droneSource != null)
        {
            if (droneClip == null) droneClip = PlaceholderSounds.CreateDroneLoop();
            droneSource.clip = droneClip;
            droneSource.loop = true;
            droneSource.spatialBlend = 1f;
            droneSource.pitch = 0.7f;
            droneSource.volume = 0f;
            droneSource.Play();
        }
    }

    private void OnEnable() { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }

    private void Update()
    {
        int level = Mathf.Clamp(GuidanceEvents.CurrentLevel, 0, lightIntensityByLevel.Length - 1);
        float dt = Time.deltaTime;

        float targetLight = lightIntensityByLevel[level];
        float maxLight = lightIntensityByLevel[lightIntensityByLevel.Length - 1];
        light01 = Mathf.MoveTowards(light01, targetLight, fadeSpeed * dt);

        if (redLight != null)
        {
            float n = (Mathf.PerlinNoise(Time.time * 1.5f, 5.3f) - 0.5f) * 2f;
            redLight.intensity = light01 * (1f + n * flicker);
            redLight.enabled = light01 > 0.05f;
        }
        if (glowRenderers != null && maxLight > 0f)
        {
            Color c = glowColor * (emissionAtMaxLight * Mathf.Clamp01(light01 / maxLight));
            foreach (Renderer r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(block);
            }
        }

        if (droneSource != null)
        {
            float target = droneVolumeByLevel[Mathf.Clamp(level, 0, droneVolumeByLevel.Length - 1)] * droneMaster;
            droneVolume = Mathf.MoveTowards(droneVolume, target, 0.3f * dt);
            droneSource.volume = droneVolume;
        }
    }
}
