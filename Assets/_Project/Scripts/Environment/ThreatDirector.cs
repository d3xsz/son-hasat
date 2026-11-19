using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ThreatTier { None = 0, Sound = 1, Shadow = 2, Physical = 3 }

/// <summary>
/// G-25: Tehdit yönetmeni (GDD 8.1). Ada Öfkesi seviyesine göre üç kademe: 1. Ses (Öfke 20-40: AmbientAudioController'daki hayvan sesleri),
/// 2. Gölge (Öfke 60: bu sınıf, sis içinde yerel gölge siluetler), 3. Fiziksel (Öfke 80-100: G-26 yaratıklar).
/// Her kademede ses bir öncekinden taşınır ve yoğunlaşır. Gölgeler yalnızca yerel ve görsel; ağda gönderilmez.
/// Sahte alarm: bazı denemelerde hiçbir şey çıkmaz.
/// </summary>
public class ThreatDirector : MonoBehaviour
{
    public static ThreatDirector Instance { get; private set; }

    [Header("Gölge kademesi")]
    [SerializeField] private ShadowFigure shadowPrefab;
    [Tooltip("Gölgelerin başladığı Öfke seviyesi (GDD: 60 = seviye 3).")]
    [SerializeField] private int shadowMinLevel = 3;
    // KULLANICI (cila): gölgeler hiç görülmüyordu → daha sık, daha yakın, daha uzun kalır
    [SerializeField] private int maxShadowsLevel3 = 3;
    [SerializeField] private int maxShadowsLevel4Plus = 5;
    [SerializeField] private float spawnIntervalMin = 2.5f;
    [SerializeField] private float spawnIntervalMax = 6f;
    [Tooltip("Her denemede gölge çıkma olasılığı; kalanı sahte alarm.")]
    [SerializeField, Range(0f, 1f)] private float spawnChance = 0.85f;
    [SerializeField] private float spawnDistanceMin = 8f;
    [SerializeField] private float spawnDistanceMax = 12f;
    [Tooltip("Görüş çevresi: kamera yönünden bu açı aralığında (derece) çıkar.")]
    [SerializeField] private float peripheralAngleMin = 18f;
    [SerializeField] private float peripheralAngleMax = 70f;
    [SerializeField] private float lifetimeMin = 7f;
    [SerializeField] private float lifetimeMax = 12f;
    [SerializeField] private float vanishDistance = 5f;
    [SerializeField] private float vanishLookSeconds = 2f;
    [Tooltip("Öfke 80+'da 'dev gölgeler' (GDD 6.6 tablosu): ölçek çarpanı.")]
    [SerializeField] private float giantScale = 1.8f;

    [Header("Test")]
    [Tooltip("0-5: bu kopyada Öfke seviyesini yok sayıp bu seviyeyi uygular. -1 = kapalı (gerçek Öfke).")]
    [SerializeField] private int debugLevelOverride = -1;

    private readonly List<ShadowFigure> active = new List<ShadowFigure>();
    private float timer = 4f;

    public int Level
    {
        get
        {
            int lvl = debugLevelOverride >= 0 ? debugLevelOverride : (WrathSystem.Instance != null ? WrathSystem.Instance.Level : 0);
            return Mathf.Clamp(lvl, 0, 5);
        }
    }

    public ThreatTier Tier
    {
        get
        {
            int l = Level;
            if (l >= 4) return ThreatTier.Physical;
            if (l >= shadowMinLevel) return ThreatTier.Shadow;
            if (l >= 1) return ThreatTier.Sound;
            return ThreatTier.None;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    private void OnEnable() { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }

    private void Update()
    {
        active.RemoveAll(f => f == null);

        int level = Level;
        Transform player = GetLocalPlayer(out PlayerLife life);
        bool canSpawn = shadowPrefab != null && player != null && level >= shadowMinLevel && (life == null || life.CanAct) && !CollapseSequence.InProgress;

        if (!canSpawn)
        {
            // seviye düştüyse veya oyuncu bayıldıysa gölgeler kaybolur
            foreach (ShadowFigure f in active) f.Vanish();
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = UnityEngine.Random.Range(spawnIntervalMin, spawnIntervalMax);

        int max = level >= 4 ? maxShadowsLevel4Plus : maxShadowsLevel3;
        if (active.Count >= max) return;
        if (UnityEngine.Random.value > spawnChance) return; // sahte alarm

        TrySpawn(player, level);
    }

    private void TrySpawn(Transform player, int level)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float baseYaw = cam.transform.eulerAngles.y;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            float yaw = baseYaw + side * UnityEngine.Random.Range(peripheralAngleMin, peripheralAngleMax);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 pos = player.position + dir * UnityEngine.Random.Range(spawnDistanceMin, spawnDistanceMax);

            if (!Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 14f, ~0, QueryTriggerInteraction.Ignore)) continue;
            pos = hit.point;
            if (Physics.CheckSphere(pos + Vector3.up * 1f, 0.5f, ~0, QueryTriggerInteraction.Ignore)) continue;

            ShadowFigure fig = Instantiate(shadowPrefab, pos, Quaternion.LookRotation(-dir, Vector3.up));
            fig.Init(player, cam, level >= 4 ? giantScale : 1f, UnityEngine.Random.Range(lifetimeMin, lifetimeMax), vanishDistance, vanishLookSeconds);
            active.Add(fig);
            return;
        }
    }

    private static Transform GetLocalPlayer(out PlayerLife life)
    {
        life = null;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        life = nm.LocalClient.PlayerObject.GetComponent<PlayerLife>();
        return nm.LocalClient.PlayerObject.transform;
    }
}
