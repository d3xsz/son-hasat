using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// G-30: Bölüm geçişi (GDD 13.2 "additive scene yükleme").
/// Sunucu, CollapseSequence bitince (ekran karardıktan holdSeconds sonra) sıradaki bölüm sahnesini NGO sahne yönetimiyle
/// ADDITIVE yükler (tüm istemcilerde). Yükleme herkeste bitince oyuncuları yeni sahnedeki noktalara taşır ve
/// CollapseSequence'ı Arrived evresine geçirir (kayma kontrolü bırakılır, ekran açılır).
/// Bölüm 1 sahnesi yüklü kalır: oyuncu nesneleri ve ortak sistemler (Öfke, GameManager, HUD) orada yaşar.
/// </summary>
public class ChapterLoader : MonoBehaviour
{
    [SerializeField] private CollapseSequence sequence;
    [SerializeField] private ChapterData nextChapter;
    [Tooltip("Ekran karardıktan ve başlık göründükten sonra yüklemeye kadar bekleme (sn).")]
    [SerializeField] private float holdSeconds = 3.5f;

    public static ChapterLoader Instance { get; private set; }
    public ChapterData CurrentChapter { get; private set; }

    private bool requested, loading, done;
    private float loadAt;

    private void OnEnable() { Instance = this; }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
    }

    private void Start()
    {
        if (sequence != null) sequence.SequenceFinished += OnSequenceFinished;
    }

    private void OnDestroy()
    {
        if (sequence != null) sequence.SequenceFinished -= OnSequenceFinished;
    }

    private void OnSequenceFinished()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || requested || nextChapter == null) return;
        requested = true;
        loadAt = Time.time + holdSeconds;
    }

    private void Update()
    {
        if (!requested || loading || done || Time.time < loadAt) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || nm.SceneManager == null) return;

        loading = true;
        nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        SceneEventProgressStatus status = nm.SceneManager.LoadScene(nextChapter.sceneName, LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError("[Bölüm] " + nextChapter.sceneName + " yüklenemedi: " + status + " (Build Settings'te mi?)");
            nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            loading = false;
            requested = false;
        }
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
    {
        if (nextChapter == null || sceneName != nextChapter.sceneName) return;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        loading = false;
        done = true;
        CurrentChapter = nextChapter;
        if (timedOut != null && timedOut.Count > 0) Debug.LogWarning("[Bölüm] Zaman aşımına uğrayan istemci: " + timedOut.Count);

        // yeni sahnedeki başlangıç noktaları
        List<Transform> points = new List<Transform>();
        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid())
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == nextChapter.spawnRootName)
                    foreach (Transform c in root.transform) points.Add(c);
        if (points.Count == 0) Debug.LogWarning("[Bölüm] " + nextChapter.spawnRootName + " bulunamadı, oyuncular taşınmadı.");

        List<PlayerLife> players = new List<PlayerLife>(PlayerLife.All);
        players.RemoveAll(p => p == null);
        players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        for (int i = 0; i < players.Count && points.Count > 0; i++)
        {
            Transform t = points[i % points.Count];
            players[i].ServerRespawn(t.position, t.rotation);
        }

        if (sequence != null) sequence.ServerMarkArrived();
    }
}
