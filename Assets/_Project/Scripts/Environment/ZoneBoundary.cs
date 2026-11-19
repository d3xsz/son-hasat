using UnityEngine;

/// <summary>
/// G-27: Daralan alanın kenarı: yarıçapı ve merkezi ShrinkingZone'u izleyen mor parlayan toz/küre halkası (yalnızca yerel görsel).
/// Alan harita dışındayken (yarıçap çok büyükken) kapalıdır.
/// </summary>
public class ZoneBoundary : MonoBehaviour
{
    [SerializeField] private ParticleSystem ring;
    [Tooltip("Bu yarıçaptan büyük alan 'harita dışı' sayılır, halka çizilmez.")]
    [SerializeField] private float maxVisibleRadius = 90f;
    [SerializeField] private float particlesPerMeterPerSecond = 2.5f;
    [SerializeField] private float maxRate = 450f;

    private void Update()
    {
        if (ring == null) return;
        ShrinkingZone zone = ShrinkingZone.Instance;
        ParticleSystem.EmissionModule em = ring.emission;

        if (zone == null || zone.Radius > maxVisibleRadius)
        {
            em.rateOverTime = 0f;
            SetWisps(0f, 0f);
            return;
        }

        ring.transform.position = new Vector3(zone.Center.x, 0.1f, zone.Center.z);
        ParticleSystem.ShapeModule shape = ring.shape;
        shape.radius = zone.Radius;
        float rate = Mathf.Min(maxRate, zone.Radius * particlesPerMeterPerSecond);
        em.rateOverTime = rate;
        SetWisps(zone.Radius, rate * wispRateFactor);
    }

    [Tooltip("V-06: halkanın altındaki soluk mor perde (alt nesne); aynı yarıçapı izler.")]
    [SerializeField] private ParticleSystem wisps;
    [SerializeField] private float wispRateFactor = 0.1f;

    private void SetWisps(float radius, float rate)
    {
        if (wisps == null) return;
        ParticleSystem.EmissionModule we = wisps.emission;
        we.rateOverTime = rate;
        if (radius <= 0f) return;
        ParticleSystem.ShapeModule ws = wisps.shape;
        ws.radius = radius;
    }
}
