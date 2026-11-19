using UnityEngine;

/// <summary>Menü kamp ateşi: ışık şiddeti ve konumu hafifçe titrer.</summary>
[RequireComponent(typeof(Light))]
public class FireFlicker : MonoBehaviour
{
    [SerializeField] private float baseIntensity = 6f;
    [SerializeField, Range(0f, 1f)] private float flicker = 0.35f;
    [SerializeField] private float speed = 7f;
    [SerializeField] private float jitter = 0.06f;

    private Light fireLight;
    private Vector3 startPos;
    private float seed;

    private void Awake()
    {
        fireLight = GetComponent<Light>();
        startPos = transform.localPosition;
        seed = Random.value * 100f;
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, 0.3f) * 0.7f + Mathf.PerlinNoise(t * 2.7f, 5.1f) * 0.3f; // 0..1
        fireLight.intensity = baseIntensity * (1f - flicker + flicker * 2f * n);
        transform.localPosition = startPos + new Vector3(Mathf.PerlinNoise(t, 9f) - 0.5f, Mathf.PerlinNoise(t, 13f) - 0.5f, Mathf.PerlinNoise(t, 17f) - 0.5f) * jitter;
    }
}
