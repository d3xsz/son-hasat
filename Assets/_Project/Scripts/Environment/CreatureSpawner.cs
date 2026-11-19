using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-26: Yaratık üreticisi (yalnızca SUNUCUDA çalışır). Ada Öfkesi seviyesi 4+ (80-100) iken en fazla 2 (seviye 4) / 3 (seviye 5) yaratık tutar.
/// Oyunculardan 22-30 m uzakta (sis dışında), güvenli alanların dışında doğar; hayatta oyuncu kalmayınca hepsi kaldırılır (bölüm yeniden başlar).
/// </summary>
public class CreatureSpawner : MonoBehaviour
{
    [SerializeField] private NetworkObject creaturePrefab;
    [SerializeField] private int minWrathLevel = 4;
    [SerializeField] private int maxCreaturesLevel4 = 2;
    [SerializeField] private int maxCreaturesLevel5 = 3;
    [SerializeField] private float spawnDistanceMin = 22f;
    [SerializeField] private float spawnDistanceMax = 30f;
    [Tooltip("Yeni yaratık denemeleri arasındaki bekleme (sn).")]
    [SerializeField] private float respawnDelay = 15f;

    [Header("Test")]
    [Tooltip("0-5: sunucuda Öfke seviyesini yok sayıp bu seviyeyi uygular. -1 = kapalı (gerçek Öfke).")]
    [SerializeField] private int debugLevelOverride = -1;

    private readonly List<NetworkObject> alive = new List<NetworkObject>();
    private float timer = 5f;

    private int Level
    {
        get
        {
            int lvl = debugLevelOverride >= 0 ? debugLevelOverride : (WrathSystem.Instance != null ? WrathSystem.Instance.Level : 0);
            return Mathf.Clamp(lvl, 0, 5);
        }
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || creaturePrefab == null) return;

        alive.RemoveAll(n => n == null || !n.IsSpawned);

        bool anyAlivePlayer = false;
        foreach (PlayerLife p in PlayerLife.All) if (p != null && p.State == PlayerLifeState.Alive) { anyAlivePlayer = true; break; }

        int level = Level;
        if (level < minWrathLevel || !anyAlivePlayer || CollapseSequence.InProgress)
        {
            DespawnAll();
            return;
        }

        int max = level >= 5 ? maxCreaturesLevel5 : maxCreaturesLevel4;
        if (alive.Count >= max) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = respawnDelay;
        TrySpawn();
    }

    private void DespawnAll()
    {
        foreach (NetworkObject n in alive) if (n != null && n.IsSpawned) n.Despawn(true);
        alive.Clear();
    }

    private void TrySpawn()
    {
        var players = new List<PlayerLife>();
        foreach (PlayerLife p in PlayerLife.All) if (p != null && p.State == PlayerLifeState.Alive) players.Add(p);
        if (players.Count == 0) return;
        PlayerLife anchor = players[Random.Range(0, players.Count)];

        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = Random.value * 360f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 pos = anchor.transform.position + dir * Random.Range(spawnDistanceMin, spawnDistanceMax);

            if (!Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 14f, ~0, QueryTriggerInteraction.Ignore)) continue;
            pos = hit.point + Vector3.up * 0.1f;
            if (Physics.CheckSphere(pos + Vector3.up * 0.5f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (SafeZones.IsInsideProtected(pos)) continue;

            NetworkObject creature = Instantiate(creaturePrefab, pos, Quaternion.LookRotation(-dir, Vector3.up));
            creature.Spawn(true);
            alive.Add(creature);
            return;
        }
    }
}
