using UnityEngine;

/// <summary>
/// G-27: Mağaraya akan su izi (PROJECT.md: Öfke 20'de mağaraya akan yağmur suyu). Yerel görsel: yerden mağaraya doğru akan ince su çizgileri.
/// Yoğunluk yağmurla birlikte artar (WeatherController.RainRate). from -> to arasında çalışır.
/// </summary>
public class WaterTrail : MonoBehaviour
{
    [SerializeField] private Transform from;
    [SerializeField] private Transform to;
    [SerializeField] private ParticleSystem particles;
    [SerializeField] private float speed = 1.5f;
    [Tooltip("Yağmur tam yoğunken (500/sn ve üstü) saniyedeki parçacık sayısı.")]
    [SerializeField] private float maxRate = 25f;

    private WeatherController weather;

    private void Start()
    {
        if (from == null || to == null || particles == null) return;
        Vector3 delta = to.position - from.position;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.1f) return;

        particles.transform.SetPositionAndRotation(from.position, Quaternion.LookRotation(delta / dist, Vector3.up));
        ParticleSystem.MainModule main = particles.main;
        main.startSpeed = speed;
        main.startLifetime = dist / speed;
        main.maxParticles = Mathf.CeilToInt(maxRate * (dist / speed)) + 50;
    }

    private void Update()
    {
        if (particles == null) return;
        if (weather == null) weather = FindFirstObjectByType<WeatherController>();
        float rain = weather != null ? Mathf.Clamp01(weather.RainRate / 500f) : 0f;
        ParticleSystem.EmissionModule em = particles.emission;
        em.rateOverTime = rain * maxRate;
    }
}
