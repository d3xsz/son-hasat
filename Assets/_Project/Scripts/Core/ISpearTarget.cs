using UnityEngine;

/// <summary>
/// G-19: Mızrakla dürtülebilen nesneler (hastalık çiçeği, uzaktan basılan düğme; ileride yaratıklar G-26).
/// Collider'ı (tetikleyici olabilir) bu bileşenin üstünde veya altında olmalı; objede NetworkObject bulunmalı.
/// OnSpearHit yalnızca SUNUCUDA, doğrulanmış bir vuruştan sonra çağrılır.
/// </summary>
public interface ISpearTarget
{
    void OnSpearHit(ulong attackerClientId, Vector3 point, Vector3 direction);
}
