using UnityEngine;

/// <summary>
/// V-05 (görsel cila): Yağmur yağdıkça zemin ıslanır (koyulaşır, parlar), durunca yavaşça kurur.
/// Goblin/GroundBlend shader'ındaki _GlobalWetness değerini sürer. Yalnızca yerel görsel; ağ ve oynanış yok.
/// </summary>
public class RainWetness : MonoBehaviour
{
    [SerializeField] private WeatherController weather;
    [Tooltip("Bu yağmur oranında (parçacık/sn) zemin tam ıslak sayılır.")]
    [SerializeField] private float fullWetRate = 700f;
    [SerializeField] private float wetPerSecond = 0.06f;
    [SerializeField] private float dryPerSecond = 0.012f;

    private static readonly int WetnessId = Shader.PropertyToID("_GlobalWetness");
    private float wetness;

    private void OnEnable() { wetness = 0f; Shader.SetGlobalFloat(WetnessId, 0f); }
    private void OnDisable() { Shader.SetGlobalFloat(WetnessId, 0f); }

    private void Update()
    {
        float target = weather != null ? Mathf.Clamp01(weather.RainRate / Mathf.Max(1f, fullWetRate)) : 0f;
        float speed = target > wetness ? wetPerSecond : dryPerSecond;
        wetness = Mathf.MoveTowards(wetness, target, speed * Time.deltaTime);
        Shader.SetGlobalFloat(WetnessId, wetness);
    }
}
