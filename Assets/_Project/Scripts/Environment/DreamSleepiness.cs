using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-07 (KULLANICI 2026-11-14, engel): Uyku bastırır. Rüyalar aleminde (yere indikten sonra) zaman geçtikçe her oyuncunun
/// mevcut zehir göstergesi "uyku" olarak dolar; dolunca oyuncu bayılır (mevcut bayılma/kaldırma sistemi). Meydandaki ışıklı
/// fener mantarının yanında durmak uykuyu hızla azaltır. Yatakta yatan oyuncunun uykusu artmaz.
/// Yalnızca sunucuda çalışır; DreamWake nesnesinde durur.
/// </summary>
public class DreamSleepiness : MonoBehaviour
{
    [Tooltip("Uykunun sıfırdan bayılmaya kadar dolma süresi (sn).")]
    [SerializeField] private float fillSeconds = 420f;
    [Tooltip("Dinlenme noktasının yanında saniyede azalan uyku.")]
    [SerializeField] private float restPerSecond = 6f;
    [SerializeField] private float restRadius = 9f;
    [Tooltip("Uykuyu azaltan noktalar (ışıklı fener mantarı).")]
    [SerializeField] private Transform[] restPoints = new Transform[0];

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !FreefallSequence.Active) return;
        float dt = Time.deltaTime;
        float rise = PoisonSystem.MaxPoison / Mathf.Max(10f, fillSeconds);

        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            Vector3 pos = p.transform.position;
            // yalnızca yere inmiş oyuncular (düşüş sırasında sayılmaz)
            if (!FreefallSequence.InColumn(pos) || pos.y > FreefallSequence.GroundY + 20f) continue;
            PoisonSystem poison = p.GetComponent<PoisonSystem>();
            if (poison == null) continue;

            bool resting = false;
            foreach (Transform r in restPoints)
            {
                if (r == null) continue;
                Vector3 d = pos - r.position; d.y = 0f;
                if (d.sqrMagnitude <= restRadius * restRadius && Mathf.Abs(pos.y - r.position.y) < 12f) { resting = true; break; }
            }
            if (resting) { poison.RemovePoison(restPerSecond * dt); continue; }

            bool inBed = false;
            foreach (DreamBed b in DreamBed.All) if (b != null && b.OccupantId == p.OwnerClientId) { inBed = true; break; }
            if (!inBed) poison.AddPoisonIgnoringProtection(rise * dt);
        }
    }
}
