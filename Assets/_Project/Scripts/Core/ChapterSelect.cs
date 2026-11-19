using UnityEngine;

/// <summary>
/// KULLANICI (2026-11-15: hocaya sunum için build'de de çalışan bölüm seçimi): Lobide host'un seçtiği başlangıç bölümü.
/// Oyun her zaman normal başlar (Bölüm 1 sahnesi yüklenir); 2 ya da 3 seçildiyse sunucu (ConnectionManager) giriş sinematiğini atlar
/// ve normal oyundaki bölüm geçişlerini kendisi tetikler (Bölüm 1 sonu → Bölüm 2; Bölüm 2 sonu → Bölüm 3).
/// Yalnızca host'ta anlamlıdır (geçişleri sunucu yürütür, istemciler sahne yüklemelerini izler).
/// </summary>
public static class ChapterSelect
{
    public static readonly string[] Names = { "Solgun Topraklar", "Derinlikler", "Düşler" };

    /// <summary>1, 2 ya da 3.</summary>
    public static int StartChapter { get; private set; } = 1;

    public static string Label => "Bölüm " + StartChapter + ": " + Names[StartChapter - 1];

    public static void Next() { StartChapter = StartChapter % Names.Length + 1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { StartChapter = 1; }
}
