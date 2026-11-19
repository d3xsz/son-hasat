using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-36: Yağmur damlalarının yere çarpma sıçramaları (yalnızca yerel görsel). Yerel oyuncunun çevresinde zeminde
/// küçük, kısa ömürlü sıçramalar; yayım WeatherController.RainRate ile orantılı. Yeraltında kapanır.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class RainSplash : MonoBehaviour
{
    [SerializeField] private WeatherController weather;
    [Tooltip("Yağmur yayımının (parçacık/sn) kaçta biri kadar sıçrama.")]
    [SerializeField] private float rateFactor = 0.25f;
    [SerializeField] private float undergroundY = -8f;
    [Tooltip("V-05: yerdeki halka dalgacıklar (alt nesne); aynı orana bağlı.")]
    [SerializeField] private ParticleSystem ripples;
    [SerializeField] private float rippleFactor = 0.3f;

    private ParticleSystem ps;

    private void Awake() { ps = GetComponent<ParticleSystem>(); }

    private void LateUpdate()
    {
        var emission = ps.emission;
        Transform player = LocalPlayer();
        if (weather == null || player == null || player.position.y < undergroundY || WeatherController.ForceCalm)
        {
            emission.rateOverTime = 0f;
            SetRipples(0f);
            return;
        }
        transform.position = new Vector3(player.position.x, 0.02f, player.position.z);
        emission.rateOverTime = weather.RainRate * rateFactor;
        SetRipples(weather.RainRate * rippleFactor);
    }

    private void SetRipples(float rate)
    {
        if (ripples == null) return;
        var e = ripples.emission;
        e.rateOverTime = rate;
    }

    private static Transform LocalPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.transform;
    }
}
