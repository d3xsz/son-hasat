using UnityEngine;

/// <summary>
/// G-27: Daralan alanın dışını dolduran hastalıklı çiçek tarlası (yalnızca yerel görsel, çarpışma yok; zehiri ShrinkingZone verir).
/// Sabit tohumla her kopyada aynı konumlarda üretilir. Çiçek, alan sınırı üzerinden geçtikçe (sınırın dışında) büyüyerek belirir.
/// </summary>
public class ZoneFlowerField : MonoBehaviour
{
    [SerializeField] private GameObject flowerPrefab;
    [SerializeField] private int count = 220;
    [Tooltip("Çiçeklerin dağıldığı kare alanın yarı kenarı (m); bu nesnenin konumu merkezdir.")]
    [SerializeField] private float halfExtent = 42f;
    [SerializeField] private int seed = 1337;
    [Tooltip("Sınırdan bu kadar (m) dışarıda çiçekler tam boya ulaşır.")]
    [SerializeField] private float growDistance = 3f;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(0.55f, 0.15f, 1f);
    [SerializeField] private float glowBase = 2.2f;
    [SerializeField] private float glowPulse = 0.6f;

    private Transform[] flowers;
    private float[] sizes;
    private bool[] visible;
    private Material headMaterial;

    private void Start()
    {
        if (flowerPrefab == null) return;
        flowers = new Transform[count];
        sizes = new float[count];
        visible = new bool[count];
        var rng = new System.Random(seed);

        for (int i = 0; i < count; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0) * halfExtent;
            float z = (float)(rng.NextDouble() * 2.0 - 1.0) * halfExtent;
            float yaw = (float)(rng.NextDouble() * 360.0);
            sizes[i] = 0.8f + (float)rng.NextDouble() * 0.7f;

            GameObject f = Instantiate(flowerPrefab, transform.position + new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f), transform);
            f.transform.localScale = Vector3.zero;
            f.SetActive(false);
            flowers[i] = f.transform;

            if (headMaterial == null)
            {
                Renderer head = f.GetComponentInChildren<Renderer>();
                var heads = f.GetComponentsInChildren<Renderer>();
                foreach (Renderer r in heads)
                    if (r.name == "Head") { head = r; break; }
                if (head != null) headMaterial = new Material(head.sharedMaterial);
            }
            if (headMaterial != null)
                foreach (Renderer r in f.GetComponentsInChildren<Renderer>())
                    if (r.name == "Head") r.sharedMaterial = headMaterial;
        }
    }

    private void Update()
    {
        if (flowers == null) return;
        ShrinkingZone zone = ShrinkingZone.Instance;

        if (headMaterial != null)
            headMaterial.SetColor("_EmissionColor", glowColor * (glowBase + Mathf.Sin(Time.time * 1.5f) * glowPulse));

        for (int i = 0; i < flowers.Length; i++)
        {
            float s = 0f;
            if (zone != null)
            {
                Vector3 d = flowers[i].position - zone.Center;
                d.y = 0f;
                s = Mathf.Clamp01((d.magnitude - zone.Radius) / Mathf.Max(0.1f, growDistance));
            }

            bool shouldShow = s > 0.001f;
            if (shouldShow != visible[i])
            {
                visible[i] = shouldShow;
                flowers[i].gameObject.SetActive(shouldShow);
            }
            if (shouldShow) flowers[i].localScale = Vector3.one * (s * sizes[i]);
        }
    }
}
