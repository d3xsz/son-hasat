using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// KULLANICI (cila): Aksiyon sesleri. Klipler Resources/Audio/SfxLibrary'de (BigSoundBank CC0 kayıtları, Audio/SFX).
/// Kalıcı bir AudioSource havuzu ile 3B (dünyada bir noktada) veya 2B (arayüz) tek seferlik ses çalar.
/// Aynı setten art arda aynı klip seçilmez; perde hafifçe rastgele değişir (tekrar hissi azalır).
/// Genel ses ayarı AudioListener.volume üzerinden zaten uygulanır.
/// </summary>
public static class Sfx
{
    private const int PoolSize = 32;
    private static SfxLibrary lib;
    private static AudioSource[] pool;
    private static int next;
    private static readonly Dictionary<AudioClip[], int> lastIndex = new Dictionary<AudioClip[], int>();

    public static SfxLibrary Lib
    {
        get
        {
            if (lib == null) lib = Resources.Load<SfxLibrary>("Audio/SfxLibrary");
            return lib;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { pool = null; lib = null; next = 0; lastIndex.Clear(); }

    private static void EnsurePool()
    {
        if (pool != null && pool[0] != null) return;
        var root = new GameObject("SfxPool");
        Object.DontDestroyOnLoad(root);
        pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Sfx" + i);
            go.transform.SetParent(root.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            pool[i] = src;
        }
    }

    private static AudioSource NextSource()
    {
        EnsurePool();
        for (int k = 0; k < PoolSize; k++)
        {
            AudioSource s = pool[(next + k) % PoolSize];
            if (!s.isPlaying) { next = (next + k + 1) % PoolSize; return s; }
        }
        AudioSource oldest = pool[next];
        next = (next + 1) % PoolSize;
        return oldest;
    }

    private static AudioClip Pick(AudioClip[] set)
    {
        if (set == null || set.Length == 0) return null;
        if (set.Length == 1) return set[0];
        lastIndex.TryGetValue(set, out int last);
        int i = Random.Range(0, set.Length);
        if (i == last) i = (i + 1 + Random.Range(0, set.Length - 1)) % set.Length;
        lastIndex[set] = i;
        return set[i];
    }

    /// <summary>Dünyada bir noktada 3B ses.</summary>
    public static void At(AudioClip[] set, Vector3 position, float volume = 1f, float pitch = 1f, float pitchVariation = 0.06f,
                          float minDistance = 1.5f, float maxDistance = 30f)
    {
        AudioClip clip = Pick(set);
        if (clip == null) return;
        AudioSource s = NextSource();
        s.transform.position = position;
        s.spatialBlend = 1f;
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        s.pitch = pitch * Random.Range(1f - pitchVariation, 1f + pitchVariation);
        s.volume = Mathf.Clamp01(volume);
        s.clip = clip;
        s.Play();
    }

    /// <summary>Arayüz / kendi kulağında 2B ses.</summary>
    public static void UI(AudioClip[] set, float volume = 1f, float pitch = 1f, float pitchVariation = 0.04f)
    {
        AudioClip clip = Pick(set);
        if (clip == null) return;
        AudioSource s = NextSource();
        s.spatialBlend = 0f;
        s.pitch = pitch * Random.Range(1f - pitchVariation, 1f + pitchVariation);
        s.volume = Mathf.Clamp01(volume);
        s.clip = clip;
        s.Play();
    }
}
