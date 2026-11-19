using UnityEngine;

/// <summary>
/// V-03 (görsel cila): Giriş sinematiğindeki teknenin feneri ve çarpma sıçraması. Tekne nesnesinin üstünde durur.
/// Fener ışığı mum gibi titrer; tekne aniden yukarı fırlayınca (kayalara çarpma karesi) su sıçraması bir kez oynar.
/// Yalnızca görsel: Timeline'a, ağa ve sinematik zamanlamasına dokunmaz (IntroCinematic nesneyi açıp kapatır).
/// </summary>
public class IntroBoatFx : MonoBehaviour
{
    [SerializeField] private Light lantern;
    [SerializeField] private Renderer lanternGlass;
    [SerializeField] private ParticleSystem splash;
    [SerializeField] private float baseIntensity = 10f;
    [SerializeField, Range(0f, 0.6f)] private float flicker = 0.22f;
    [SerializeField, ColorUsage(false, true)] private Color glassColor = new Color(4.2f, 2.3f, 0.7f);
    [Tooltip("Teknenin bu dikey hızı (m/sn) aşması çarpma sayılır.")]
    [SerializeField] private float crashSpeed = 0.7f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock block;
    private Vector3 lastPos;
    private bool fired;
    private int warmup;

    private void OnEnable()
    {
        lastPos = transform.position;
        fired = false;
        warmup = 3;   // ilk karelerde Timeline tekneyi yerine oturtur; sıçrama sayılmasın
    }

    private void LateUpdate()
    {
        float n = Mathf.PerlinNoise(Time.time * 7f, 3.1f) * 0.7f + Mathf.PerlinNoise(Time.time * 19f, 8.4f) * 0.3f;
        float k = 1f + (n - 0.5f) * 2f * flicker;
        if (lantern != null) lantern.intensity = baseIntensity * k;
        if (lanternGlass != null)
        {
            if (block == null) block = new MaterialPropertyBlock();
            lanternGlass.GetPropertyBlock(block);
            block.SetColor(EmissionId, glassColor * k);
            lanternGlass.SetPropertyBlock(block);
        }

        float dt = Time.deltaTime;
        if (warmup > 0) warmup--;
        else if (!fired && dt > 0f && (transform.position.y - lastPos.y) / dt > crashSpeed)
        {
            fired = true;
            if (splash != null) splash.Play();
        }
        lastPos = transform.position;
    }
}
