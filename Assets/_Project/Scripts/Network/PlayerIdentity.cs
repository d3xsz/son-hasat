using UnityEngine;

/// <summary>
/// Yerel oyuncunun adı (lobide girilir, PlayerPrefs'te saklanır). NetworkPlayer bunu ağda paylaşır.
/// Ad bu süreçte bellekte tutulur: aynı bilgisayarda açılan ikinci editör/oyun (ortak PlayerPrefs) diğerinin
/// yazdığı adı okumasın diye PlayerPrefs yalnızca açılışta bir kez okunur.
/// </summary>
public static class PlayerIdentity
{
    private const string Key = "PlayerName";
    public const int MaxLength = 16;

    private static string cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cached = null; }

    public static string LocalName
    {
        get
        {
            if (cached == null) cached = PlayerPrefs.GetString(Key, "");
            return cached;
        }
        set
        {
            string v = (value ?? "").Trim();
            if (v.Length > MaxLength) v = v.Substring(0, MaxLength);
            cached = v;
            PlayerPrefs.SetString(Key, v); // bir sonraki açılış için hatırla
            PlayerPrefs.Save();
        }
    }
}
