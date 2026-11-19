using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (2026-11-10): Menü başlığı kamp ateşiyle birlikte hafifçe titrer ve sıcak bir ışıltı alır.
/// Başlık rengi ve (varsa) Outline/Shadow rengi Perlin gürültüsüyle oynatılır; açılışta yavaşça belirir.
/// </summary>
[RequireComponent(typeof(Text))]
public class TitleFlicker : MonoBehaviour
{
    [SerializeField] private Color baseColor = new Color(0.95f, 0.88f, 0.74f);
    [SerializeField] private Color warmColor = new Color(1f, 0.72f, 0.42f);
    [SerializeField] private float flicker = 0.35f;
    [SerializeField] private float fadeInSeconds = 1.6f;

    private Text text;
    private Shadow glow;
    private float seed, start;

    private void Awake()
    {
        text = GetComponent<Text>();
        glow = GetComponent<Shadow>();
        seed = Random.value * 50f;
        start = Time.unscaledTime;
    }

    private void Update()
    {
        float t = Time.unscaledTime;
        float n = Mathf.PerlinNoise(t * 3.1f, seed) * 0.7f + Mathf.PerlinNoise(t * 9.3f, seed + 7f) * 0.3f;
        float fade = Mathf.Clamp01((t - start) / Mathf.Max(0.01f, fadeInSeconds));
        Color c = Color.Lerp(baseColor, warmColor, n * flicker);
        c.a = fade;
        text.color = c;
        if (glow != null)
        {
            Color g = warmColor; g.a = (0.25f + 0.35f * n) * fade;
            glow.effectColor = g;
        }
    }
}
