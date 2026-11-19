using UnityEngine;

/// <summary>
/// G-21: Güvenli alan sorgusu. Sabit totemler ve taşınabilir/yerdeki totem alanları birlikte değerlendirilir.
/// Her kopyada çalışır (totem durumu ve oyuncu konumları ağda senkron olduğu için).
/// </summary>
public static class SafeZones
{
    /// <summary>Yalnızca SABİT totem alanları (zehri engelleyen, yaratıkların girmediği alan). Taşınan/yerdeki totem sayılmaz.</summary>
    public static bool IsInsideProtected(Vector3 worldPosition)
    {
        foreach (Totem t in Totem.All)
            if (t != null && t.IsActive && t.Contains(worldPosition)) return true;
        return false;
    }

    /// <summary>Tüm güvenli alanlar (sabit + taşınan/yerdeki totem): ölü diriltme için sayılır.</summary>
    public static bool IsInside(Vector3 worldPosition)
    {
        foreach (Totem t in Totem.All)
            if (t != null && t.IsActive && t.Contains(worldPosition)) return true;
        foreach (PortableSafeZone z in PortableSafeZone.All)
            if (z != null && z.IsActive && z.Contains(worldPosition)) return true;
        return false;
    }
}
