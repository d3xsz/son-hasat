using UnityEngine;

/// <summary>
/// G-16: Gerçek ses dosyaları gelene kadar (G-24/G-37) kodla üretilen placeholder sesler.
/// </summary>
public static class PlaceholderSounds
{
    private const int SampleRate = 44100;

    /// <summary>Kısa, iki vuruşlu öksürük benzeri gürültü patlaması.</summary>
    public static AudioClip CreateCough()
    {
        int n = (int)(SampleRate * 0.55f);
        float[] data = new float[n];
        var rng = new System.Random(1234);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Pulse(t, 0f, 0.18f) + 0.8f * Pulse(t, 0.25f, 0.2f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.25f;
            data[i] = lp * env * 1.6f;
        }
        var clip = AudioClip.Create("PlaceholderCough", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Alçak, yavaş yükselip sönen fısıltı benzeri gürültü.</summary>
    public static AudioClip CreateWhisper()
    {
        int n = (int)(SampleRate * 1.6f);
        float[] data = new float[n];
        var rng = new System.Random(4321);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float x = t / 1.6f;
            float env = Mathf.Sin(Mathf.PI * x);
            env *= env;
            float mod = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3.5f * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.08f;
            data[i] = lp * env * mod * 3f;
        }
        var clip = AudioClip.Create("PlaceholderWhisper", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>G-24: Yağmur hışırtısı (4 sn döngü, yüksek geçiren gürültü).</summary>
    public static AudioClip CreateRainLoop()
    {
        int n = SampleRate * 4;
        float[] data = new float[n];
        var rng = new System.Random(77);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.35f;
            data[i] = (noise - lp) * 0.6f;
        }
        MakeSeamless(data, SampleRate / 10);
        return MakeClip("PlaceholderRain", data);
    }

    /// <summary>G-24: Rüzgar (4 sn döngü, alçak geçiren gürültü, yavaş dalgalanma).</summary>
    public static AudioClip CreateWindLoop()
    {
        int n = SampleRate * 4;
        float[] data = new float[n];
        var rng = new System.Random(55);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.02f;
            float mod = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
            data[i] = lp * mod * 6f;
        }
        MakeSeamless(data, SampleRate / 10);
        return MakeClip("PlaceholderWind", data);
    }

    /// <summary>G-24: Gök gürültüsü (3.2 sn, alçak gümbürtü + başlangıçta kısa çıtırtı).</summary>
    public static AudioClip CreateThunder()
    {
        int n = (int)(SampleRate * 3.2f);
        float[] data = new float[n];
        var rng = new System.Random(99);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = t < 0.12f ? t / 0.12f : Mathf.Exp(-(t - 0.12f) * 1.3f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.025f;
            float crackle = noise * 0.15f * Mathf.Exp(-t * 8f);
            data[i] = (lp * 8f * env + crackle) * 0.9f;
        }
        return MakeClip("PlaceholderThunder", data);
    }

    /// <summary>G-24: Mağaradan gelen alçak uğultu (4 sn kusursuz döngü: tam periyotlu sinüsler).</summary>
    public static AudioClip CreateDroneLoop()
    {
        int n = SampleRate * 4;
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float tone = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.15f;
            data[i] = tone * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 0.25f * t)) * 0.4f;
        }
        return MakeClip("PlaceholderDrone", data);
    }

    private static AudioClip MakeClip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static void MakeSeamless(float[] data, int fade)
    {
        int n = data.Length;
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            data[n - fade + i] = data[n - fade + i] * (1f - k) + data[i] * k;
        }
    }

    private static float Pulse(float t, float start, float length)
    {
        float x = (t - start) / length;
        if (x < 0f || x > 1f) return 0f;
        return Mathf.Pow(1f - x, 2f) * Mathf.Min(1f, x * 20f);
    }
}
