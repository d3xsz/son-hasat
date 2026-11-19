using UnityEngine;

/// <summary>
/// G-27: Yönlendirme olayları için ortak Öfke seviyesi (PROJECT.md: görünmez duvar yok, doğal engellerle yönlendirme).
/// Düşen ağaçlar (seviye 2), yayılan hastalık (seviye 2+), mağaradan kırmızı ışık + uğultu (seviye 3, 80'de yoğun), su izleri (yağmurla).
/// Bu bileşen sahnede bir kez durur; diğer yönlendirme bileşenleri CurrentLevel'i okur.
/// </summary>
public class GuidanceEvents : MonoBehaviour
{
    public static GuidanceEvents Instance { get; private set; }

    [Header("Test")]
    [Tooltip("0-5: bu kopyada Öfke seviyesini yok sayıp bu seviyeyi uygular (sunucuda ağaç düşürmek için host'ta ayarla). -1 = kapalı (gerçek Öfke).")]
    [SerializeField] private int debugLevelOverride = -1;

    /// <summary>Yönlendirme için geçerli Öfke seviyesi (0-5): test geçersiz kılması varsa o, yoksa gerçek Öfke.</summary>
    public static int CurrentLevel
    {
        get
        {
            if (Instance != null && Instance.debugLevelOverride >= 0) return Mathf.Clamp(Instance.debugLevelOverride, 0, 5);
            return WrathSystem.Instance != null ? Mathf.Clamp(WrathSystem.Instance.Level, 0, 5) : 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    private void OnEnable() { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }
}
