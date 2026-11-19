using UnityEngine;

/// <summary>
/// B3-09 (KULLANICI 2026-11-15: "ortam tatlı duruyor, görünüm olarak ufaktan gerilim lazım"): Rüya köyündeki fener ışığının
/// tekinsiz titremesi. Işık çoğunlukla sakin nefes alır; arada bir neredeyse sönüp geri gelir. Yalnızca yerel görsel.
/// </summary>
[RequireComponent(typeof(Light))]
public class DreamLampFlicker : MonoBehaviour
{
    [SerializeField] private float dipIntervalMin = 6f;
    [SerializeField] private float dipIntervalMax = 16f;
    [SerializeField, Range(0f, 1f)] private float dipDepth = 0.85f;

    private Light lamp;
    private float baseIntensity;
    private float nextDip, dipUntil, phase;

    private void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
        phase = Random.value * 10f;
        nextDip = Time.time + Random.Range(dipIntervalMin, dipIntervalMax);
    }

    private void Update()
    {
        float t = Time.time + phase;
        float k = 0.88f + 0.12f * Mathf.PerlinNoise(t * 1.3f, 0.3f);
        if (Time.time >= nextDip)
        {
            dipUntil = Time.time + Random.Range(0.25f, 0.9f);
            nextDip = Time.time + Random.Range(dipIntervalMin, dipIntervalMax);
        }
        if (Time.time < dipUntil) k *= 1f - dipDepth * (0.6f + 0.4f * Mathf.PerlinNoise(t * 22f, 1.7f));   // sönmeye yakın hızlı kıpırtı
        lamp.intensity = baseIntensity * k;
    }
}
