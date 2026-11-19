using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-01 (KULLANICI 2026-11-14): Bölüm 3 girişindeki düşüş sekansı. Bölüm 3 sahnesinde durur (sahne içi NetworkObject).
/// Sahne herkeste yüklenip bu nesne doğunca sunucu oyuncuları gökyüzündeki başlangıç noktalarına taşır ve düşüşü başlatır.
/// Düşüşün kendisi oyuncu başına PlayerFreefall'da çalışır (yere değen oyuncu birinci şahısa döner).
/// Ayrıca yerel "rüya" ortamını uygular: gökyüzü, sis, ortam ışığı, kamera uzak düzlemi (sahne kapanınca geri alınır).
/// Nesnenin konumu düşüş sütununun eksenidir (x, z).
/// </summary>
public class FreefallSequence : NetworkBehaviour
{
    public static FreefallSequence Instance { get; private set; }
    public static bool Active => Instance != null && Instance.falling.Value;
    public static float GroundY => Instance != null ? Instance.groundY : float.MinValue;
    public static Vector3 Axis => Instance != null ? Instance.transform.position : Vector3.zero;
    public static float SteerRadius => Instance != null ? Instance.steerRadius : 0f;

    [Header("Düşüş sütunu")]
    [SerializeField] private Transform[] startPoints = new Transform[0];
    [SerializeField] private float groundY = -1010f;
    [Tooltip("Oyuncunun süzülebileceği en geniş yarıçap (m).")]
    [SerializeField] private float steerRadius = 90f;
    [SerializeField] private float columnRadius = 170f;
    [SerializeField] private float startDelay = 0.6f;
    [Tooltip("Sütun zeminin bu kadar altına uzanır (B3-05: ev içleri köyün altında durur ve aynı ortamı kullanır).")]
    [SerializeField] private float columnDepth = 90f;

    [Header("Rüya ortamı (yerel görsel)")]
    [SerializeField] private Material dreamSky;
    [SerializeField] private Color fogColor = new Color(0.4f, 0.3f, 0.5f);
    [SerializeField] private float fogDensity = 0.0035f;
    [SerializeField] private Color ambient = new Color(0.34f, 0.3f, 0.42f);
    [SerializeField] private float cameraFarClip = 520f;
    [Header("Yere yakın ortam (B3-02: loş mor alacakaranlık; ayrıntı fenerle görülür)")]
    [SerializeField] private Color groundFogColor = new Color(0.2f, 0.15f, 0.27f);
    [SerializeField] private float groundFogDensity = 0.009f;
    [SerializeField] private Color groundAmbient = new Color(0.075f, 0.062f, 0.105f);
    [Tooltip("Yerden bu yüksekliğe (m) kadar düşüş ortamından yer ortamına geçilir.")]
    [SerializeField] private float groundBlendHeight = 90f;
    [Tooltip("KULLANICI (2026-11-15): rüyaya özgü sis. Yer sisinin rengi bu renge doğru ağır ağır kayar.")]
    [SerializeField] private Color groundFogColorAlt = new Color(0.17f, 0.08f, 0.16f);
    [Tooltip("Yer sisi yoğunluğunun nefes alır gibi dalgalanma oranı (0.2 = ±%20).")]
    [SerializeField, Range(0f, 0.5f)] private float groundFogPulse = 0.2f;
    [Header("Yerde görüş kısıtı (KULLANICI 2026-11-15: \"fenersiz her yeri rahat görüyorum\")")]
    [Tooltip("Yerdeyken kullanılan gök: ufku sisle aynı koyu renkte → uzaktaki siluetler ufka karışır. Boşsa düşüş göğü kalır.")]
    [SerializeField] private Material groundSky;
    [Tooltip("Bölüm 1'den gelen ay ışığı (RenderSettings.sun) yerdeyken bu orana iner; köy her yerden eşit aydınlanmasın.")]
    [SerializeField, Range(0f, 1f)] private float groundSunFactor = 0.12f;
    [Tooltip("Yerdeyken gök yansıması bu orana iner (ıslak şapkalar gökyüzünü yansıtıp karanlıkta seçilmesin).")]
    [SerializeField, Range(0f, 1f)] private float groundReflection = 0.25f;
    private float savedReflection = 1f;
    private Light dimSun;
    private float dimSunBase;

    [Header("Rüya ortam sesi (üretilmiş uğultu; yerel)")]
    [SerializeField, Range(0f, 1f)] private float padVolume = 0.3f;
    private AudioSource padSource;

    private readonly NetworkVariable<bool> falling = new NetworkVariable<bool>(false);
    private float startAt = -1f;

    private bool envSaved;
    private Material savedSky;
    private Color savedFogColor;
    private Camera farCam;
    private float savedFar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    /// <summary>Konum düşüş sütununun içinde mi (başka bölümlerdeki oyuncular etkilenmesin).</summary>
    public static bool InColumn(Vector3 p)
    {
        if (Instance == null) return false;
        Vector3 d = p - Instance.transform.position;
        d.y = 0f;
        return d.sqrMagnitude <= Instance.columnRadius * Instance.columnRadius && p.y > Instance.groundY - Instance.columnDepth && p.y < Instance.transform.position.y + 80f;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer) startAt = Time.time + startDelay;
        SaveEnvironment();
    }

    public override void OnNetworkDespawn() { Cleanup(); }
    public override void OnDestroy() { Cleanup(); base.OnDestroy(); }

    private void Cleanup()
    {
        RestoreEnvironment();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer || falling.Value || startAt < 0f || Time.time < startAt) return;

        List<PlayerLife> players = new List<PlayerLife>(PlayerLife.All);
        players.RemoveAll(p => p == null);
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        for (int i = 0; i < players.Count && startPoints.Length > 0; i++)
        {
            Transform t = startPoints[i % startPoints.Length];
            if (t != null) players[i].ServerRespawn(t.position, t.rotation);
        }
        falling.Value = true;
    }

    // ---------- yerel ortam ----------

    private void SaveEnvironment()
    {
        if (envSaved) return;
        envSaved = true;
        savedSky = RenderSettings.skybox;
        savedFogColor = RenderSettings.fogColor;
        savedReflection = RenderSettings.reflectionIntensity;
    }

    private void RestoreEnvironment()
    {
        if (!envSaved) return;
        envSaved = false;
        RenderSettings.skybox = savedSky;
        RenderSettings.fogColor = savedFogColor;
        if (farCam != null) farCam.farClipPlane = savedFar;
        farCam = null;
        RestoreSun();
        RenderSettings.reflectionIntensity = savedReflection;
    }

    private void RestoreSun()
    {
        if (dimSun != null) dimSun.intensity = dimSunBase;
        dimSun = null;
    }

    // WeatherController sis yoğunluğunu ve ortam ışığını her Update'te yazar → LateUpdate'te üstüne yazılır
    private void LateUpdate()
    {
        if (!IsSpawned || !envSaved) return;
        Camera cam = Camera.main;
        bool here = cam != null && InColumn(cam.transform.position);
        // rüya uğultusu: sütundayken açılır (düşüşte rüzgar baskın olduğu için daha kısık)
        if (padSource == null && here)
        {
            padSource = gameObject.AddComponent<AudioSource>();
            padSource.clip = DreamSfx.Pad; padSource.loop = true; padSource.spatialBlend = 0f; padSource.volume = 0f; padSource.playOnAwake = false;
            padSource.Play();
        }
        if (padSource != null)
        {
            float high = cam != null ? Mathf.Clamp01((cam.transform.position.y - groundY - 3f) / Mathf.Max(1f, groundBlendHeight)) : 0f;
            padSource.volume = Mathf.MoveTowards(padSource.volume, here ? padVolume * Mathf.Lerp(1f, 0.4f, high) : 0f, Time.deltaTime * 0.25f);
        }
        if (!here)
        {
            // oyuncu henüz taşınmadıysa (ya da sütunun dışındaysa) eski ortam geçerli
            bool ours = (dreamSky != null && RenderSettings.skybox == dreamSky) || (groundSky != null && RenderSettings.skybox == groundSky);
            if (ours) { RenderSettings.skybox = savedSky; RenderSettings.fogColor = savedFogColor; }
            if (farCam != null) { farCam.farClipPlane = savedFar; farCam = null; }
            RestoreSun();
            RenderSettings.reflectionIntensity = savedReflection;
            return;
        }
        if (dreamSky != null) RenderSettings.skybox = dreamSky;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((cam.transform.position.y - groundY - 3f) / Mathf.Max(1f, groundBlendHeight)));
        float tt = Time.time;
        float hue = Mathf.PerlinNoise(tt * 0.05f, 0.7f);
        float breath = 1f + groundFogPulse * (Mathf.Sin(tt * 0.31f) * 0.6f + (Mathf.PerlinNoise(tt * 0.11f, 3.1f) - 0.5f) * 0.8f);
        RenderSettings.fogColor = Color.Lerp(Color.Lerp(groundFogColor, groundFogColorAlt, hue), fogColor, k);
        RenderSettings.fogDensity = Mathf.Lerp(groundFogDensity * breath, fogDensity, k);
        RenderSettings.ambientLight = Color.Lerp(groundAmbient, ambient, k);
        // yerde: ufku koyu gök + kısık ay ışığı (düşüşte gök ve ay normal)
        if (groundSky != null && k < 0.35f) RenderSettings.skybox = groundSky;
        RenderSettings.reflectionIntensity = Mathf.Lerp(groundReflection, savedReflection, k);
        Light sun = RenderSettings.sun;
        if (sun != null)
        {
            if (dimSun != sun) { RestoreSun(); dimSun = sun; dimSunBase = sun.intensity; }
            sun.intensity = dimSunBase * Mathf.Lerp(groundSunFactor, 1f, k);
        }
        if (farCam != cam)
        {
            if (farCam != null) farCam.farClipPlane = savedFar;
            farCam = cam;
            savedFar = cam.farClipPlane;
        }
        cam.farClipPlane = cameraFarClip;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.6f, 1f, 0.6f);
        Vector3 c = new Vector3(transform.position.x, groundY, transform.position.z);
        const int steps = 48;
        Vector3 prev = c + new Vector3(steerRadius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * steerRadius, 0f, Mathf.Sin(a) * steerRadius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
        Gizmos.DrawLine(transform.position, c);
    }
}
