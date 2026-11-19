using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KULLANICI (2026-11-09): Menü müziği. Kendini kurar (kalıcı), ana menü ve lobi (host/katıl ekranı) sahnelerinde
/// chill döngüyü çalar; sahneler arasında kesilmez. Oyun sahnesine geçince yavaşça susar.
/// Ses: Genel × Müzik ayarı. Kaynak AudioListener.volume'u yok sayar (o "Efektler" ayarını taşır).
/// Klip: Resources/Audio/Music_MenuChill (OpenGameArt, etrock, CC0).
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    private static readonly string[] MenuScenes = { "MainMenu", "Lobby" };
    private const float BaseVolume = 0.55f;

    private static MusicPlayer instance;
    private AudioSource source;
    private float fade;      // 0..1 (sahneye göre hedefe kayar)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (instance != null) return;
        var clip = Resources.Load<AudioClip>("Audio/Music_MenuChill");
        if (clip == null) { Debug.LogWarning("[Müzik] Resources/Audio/Music_MenuChill bulunamadı."); return; }
        var go = new GameObject("MusicPlayer");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MusicPlayer>();
        var s = instance.source = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f;
        s.ignoreListenerVolume = true; // Genel×Müzik ile kendimiz ayarlıyoruz
        s.priority = 0; s.volume = 0f;
    }

    private static bool InMenu()
    {
        string n = SceneManager.GetActiveScene().name;
        foreach (string m in MenuScenes) if (m == n) return true;
        return false;
    }

    private void Update()
    {
        bool want = InMenu();
        if (want && !source.isPlaying) source.Play();
        fade = Mathf.MoveTowards(fade, want ? 1f : 0f, Time.unscaledDeltaTime * (want ? 0.5f : 0.35f));
        source.volume = BaseVolume * fade * GameSettings.MasterVolume * GameSettings.MusicVolume;
        if (!want && fade <= 0f && source.isPlaying) source.Stop();
    }
}
