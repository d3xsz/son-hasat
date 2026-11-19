using Unity.Netcode;
using UnityEngine;

/// <summary>
/// B3-06 (KULLANICI 2026-11-14): Uyanış. Sunucuda çalışır: hayattaki HER oyuncu bir yatakta yatıyorsa (oyuncu sayısı kadar yatak yeter)
/// ve bu durum kısa süre sürerse bağlı ChapterEnd'i tetikler (ekran beyaza açılır → bölüm sonu yazısı → ana menü).
/// ChapterEnd ile aynı nesnede durur.
/// </summary>
[RequireComponent(typeof(ChapterEnd))]
public class DreamWakeWatcher : MonoBehaviour
{
    [SerializeField] private float holdSeconds = 2.5f;

    private ChapterEnd end;
    private float held;

    private void Awake() { end = GetComponent<ChapterEnd>(); }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || end == null || !end.IsSpawned) return;

        int alive = 0, asleep = 0;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State == PlayerLifeState.Dead) continue;
            alive++;
            foreach (DreamBed b in DreamBed.All)
                if (b != null && b.OccupantId == p.OwnerClientId) { asleep++; break; }
        }
        held = alive > 0 && asleep >= alive ? held + Time.deltaTime : 0f;
        if (held >= holdSeconds) end.ServerEnd();
    }
}
