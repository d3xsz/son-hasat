using System;
using UnityEngine;

/// <summary>
/// B2-04 (GDD 7.1): Gürültü olayları. Ses çıkaran şeyler (koşma/yürüme adımı, iniş, mızrak, kristal "tın"ı) buraya
/// konum + duyulma yarıçapı bildirir. Dinleyen (ListenerCreature) SUNUCUDA dinler; diğer kopyalarda olay yok sayılır.
/// Adımlar her kopyada her oyuncu için yerel hesaplandığından sunucu kendi kopyasındaki adımları duyar (ağ mesajı gerekmez).
/// </summary>
public static class Noise
{
    public const float CrouchStep = 0.8f;
    public const float WalkStep = 2f;
    public const float RunStep = 14f;
    public const float Landing = 16f;
    public const float SpearSwing = 7f;
    public const float SpearHit = 11f;
    public const float CrystalChime = 10f;

    /// <summary>(konum, duyulma yarıçapı m)</summary>
    public static event Action<Vector3, float> Emitted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Emitted = null; }

    public static void Emit(Vector3 position, float radius)
    {
        Emitted?.Invoke(position, radius);
    }
}
