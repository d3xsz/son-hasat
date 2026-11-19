using UnityEngine;

/// <summary>
/// G-30: Bölüm tanımı (ScriptableObject). ChapterLoader bir sonraki bölümün sahnesini bununla additive yükler.
/// </summary>
[CreateAssetMenu(fileName = "Chapter", menuName = "Goblin/Chapter Data")]
public class ChapterData : ScriptableObject
{
    public int chapterNumber = 2;
    public string title = "Derinlikler";
    [Tooltip("Build Settings'te kayıtlı sahne adı (additive yüklenir).")]
    public string sceneName = "Ch2_Transition";
    [Tooltip("Yüklenen sahnede oyuncuların konulacağı noktaların ebeveyn nesnesinin adı (çocukları sırayla kullanılır).")]
    public string spawnRootName = "Ch2_SpawnPoints";
}
