using UnityEngine;

/// <summary>
/// B3 (KULLANICI 2026-11-14: "mağaradaki su damlası sesi ne alaka, buraya özgü ses lazım"): Rüyalar alemine özgü sesler.
/// Hazır ses dosyası olmadığı için çalışma anında ÜRETİLİR (placeholder; kullanıcı gerçek ses eklerse buradan değiştirilir):
/// Chime = yumuşak çan/tını (bulmaca geri bildirimi, çanlar), Tick = kısa tahta tıkırtısı (dişliler),
/// Pad = döngülü, hafif tekinsiz rüya uğultusu (ortam). Sfx.At / Sfx.UI ile çalınır (dizi bekledikleri için tek elemanlı dizi).
/// </summary>
public static class DreamSfx
{
    private const int Rate = 44100;
    private static AudioClip[] chime, tick;
    private static AudioClip pad;

    public static AudioClip[] Chime => chime != null && chime[0] != null ? chime : chime = new[] { BuildChime() };
    public static AudioClip[] Tick => tick != null && tick[0] != null ? tick : tick = new[] { BuildTick() };
    public static AudioClip Pad => pad != null ? pad : pad = BuildPad();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { chime = null; tick = null; pad = null; }

    // çan: temel ses + çana özgü uyumsuz üst sesler, üstel sönüm
    private static AudioClip BuildChime()
    {
        int n = (int)(Rate * 1.6f);
        float[] d = new float[n];
        const float f = 660f;
        float[] ratio = { 1f, 2.76f, 5.4f, 8.93f };
        float[] amp = { 1f, 0.45f, 0.2f, 0.08f };
        float[] decay = { 3.2f, 5f, 8f, 12f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, s = 0f;
            for (int k = 0; k < ratio.Length; k++) s += Mathf.Sin(2f * Mathf.PI * f * ratio[k] * t) * amp[k] * Mathf.Exp(-decay[k] * t);
            d[i] = s * 0.42f * Mathf.Clamp01(t * 400f) * Mathf.Clamp01((1.6f - t) * 8f);
        }
        AudioClip c = AudioClip.Create("DreamChime", n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    // tık: çok kısa, boğuk vuruş
    private static AudioClip BuildTick()
    {
        int n = (int)(Rate * 0.09f);
        float[] d = new float[n];
        System.Random rnd = new System.Random(5);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp = Mathf.Lerp(lp, (float)rnd.NextDouble() * 2f - 1f, 0.25f);
            d[i] = (lp * 0.6f + Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.5f) * Mathf.Exp(-55f * t) * 0.8f;
        }
        AudioClip c = AudioClip.Create("DreamTick", n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    // uğultu: 12 sn'lik kusursuz döngü (tüm frekanslar 12 sn'de tam tur atar); yavaş dalgalanan, hafif gergin akor
    private static AudioClip BuildPad()
    {
        const float len = 12f;
        int n = (int)(Rate * len);
        float[] d = new float[n];
        float[] f = { 110f, 165f, 220f, 233f, 261.5f, 330f };
        float[] a = { 0.5f, 0.32f, 0.3f, 0.1f, 0.2f, 0.14f };
        float[] lfo = { 1f, 2f, 3f, 5f, 4f, 7f };       // 12 sn'deki dalgalanma sayısı
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, s = 0f;
            for (int k = 0; k < f.Length; k++)
            {
                float swell = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * lfo[k] / len * t + k * 1.3f);
                s += Mathf.Sin(2f * Mathf.PI * f[k] * t) * a[k] * swell;
                s += Mathf.Sin(2f * Mathf.PI * (f[k] + 1f / 3f) * t) * a[k] * 0.5f * swell;     // hafif akort kayması (koro etkisi; 12 sn'de tam tur)
            }
            d[i] = s * 0.2f;
        }
        AudioClip c = AudioClip.Create("DreamPad", n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
