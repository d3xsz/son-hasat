using UnityEngine;

/// <summary>
/// G-32/G-34: Bir nokta, herhangi bir oyuncunun AÇIK fenerinin ışık konisinde ve görüş hattında mı?
/// Her kopyada yerel hesaplanır (fener durumu ve bakış yönü zaten ağda senkron).
/// </summary>
public static class FlashlightReveal
{
    public static bool IsLit(Vector3 point, float range, float halfAngle) => IsLit(point, range, halfAngle, null);

    /// <summary>ignore: aydınlatılan nesnenin kendisi — görüş hattı ona çarparsa engel sayılmaz (kendi collider'ı ışığı kesmesin).</summary>
    public static bool IsLit(Vector3 point, float range, float halfAngle, Transform ignore)
    {
        return Scan(point, range, halfAngle, ignore, false) != 0;
    }

    /// <summary>B3-03: noktaya tutulan boyut ışıklarının net yönü: +1 büyüten, -1 küçülten, 0 yok (ya da ikisi birden).</summary>
    public static int SizeDirection(Vector3 point, float range, float halfAngle, Transform ignore)
    {
        int sum = Scan(point, range, halfAngle, ignore, true);
        return sum > 0 ? 1 : sum < 0 ? -1 : 0;
    }

    // sizeOnly = false: ilk aydınlatan fenerde 1 döner. true: büyüten +1, küçülten -1 toplanır (normal ışık sayılmaz).
    private static int Scan(Vector3 point, float range, float halfAngle, Transform ignore, bool sizeOnly)
    {
        int sum = 0;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State == PlayerLifeState.Dead || p.CameraPivot == null) continue;
            FlashlightNetworkSync fl = p.GetComponent<FlashlightNetworkSync>();
            if (fl == null || !fl.IsOn) continue;
            if (sizeOnly && fl.Mode == FlashlightController.ModeNormal) continue;
            Transform pivot = p.CameraPivot;
            Vector3 to = point - pivot.position;
            float dist = to.magnitude;
            if (dist > range || dist < 0.01f) continue;
            if (Vector3.Angle(pivot.forward, to) > halfAngle) continue;
            // görüş hattı: işaretin hemen önüne (oyuncu tarafına) kadar engel yoksa aydınlanır
            Vector3 target = point - to.normalized * 0.25f;
            if (Physics.Linecast(pivot.position, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.root != p.transform.root
                && (ignore == null || !hit.collider.transform.IsChildOf(ignore))) continue;
            if (!sizeOnly) return 1;
            sum += fl.Mode == FlashlightController.ModeGrow ? 1 : -1;
        }
        return sum;
    }
}
