using UnityEngine;

/// <summary>
/// G-27: Yayılan hastalık (PROJECT.md: Öfke 40'ta hastalık yayılmaya başlar, 60'ta duvar genişler). Altındaki tüm PoisonZone'ların
/// yarıçapını Öfke seviyesine göre yavaşça büyütür (hem sunucu hem istemci aynı değeri hesaplar; spor parçacıkları yarıçapı izler).
/// Çiçeklerin olduğu bir üst nesneye eklenir.
/// </summary>
public class DiseaseSpread : MonoBehaviour
{
    [Tooltip("Öfke seviyesi 0-5 için yarıçap çarpanı.")]
    [SerializeField] private float[] radiusMultiplierByLevel = { 1f, 1f, 1.3f, 1.7f, 2.1f, 2.1f };
    [Tooltip("Çarpanın hedefe yaklaşma hızı (1/sn): yayılma yavaştır.")]
    [SerializeField] private float growPerSecond = 0.15f;

    private PoisonZone[] zones;
    private float[] baseRadius;
    private float current = 1f;

    private void Awake()
    {
        zones = GetComponentsInChildren<PoisonZone>(true);
        baseRadius = new float[zones.Length];
        for (int i = 0; i < zones.Length; i++) baseRadius[i] = zones[i].Radius;
    }

    private void Update()
    {
        int level = Mathf.Clamp(GuidanceEvents.CurrentLevel, 0, radiusMultiplierByLevel.Length - 1);
        current = Mathf.MoveTowards(current, radiusMultiplierByLevel[level], growPerSecond * Time.deltaTime);
        for (int i = 0; i < zones.Length; i++)
            if (zones[i] != null) zones[i].Radius = baseRadius[i] * current;
    }
}
