using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// B2-08 (KULLANICI, 2026-11-09): Uçurum üstündeki çöken tahta köprü (karanlık geçitten sonra).
/// - Yürümek güvenli: tahtalar basınca hafifçe çöker, gıcırdar (her kopyada yerel görsel/ses).
/// - Köprüde KOŞMA ya da zıplayıp İNME (Noise olayları) → sunucu köprüyü yıkar: kısa sarsıntı, sonra kırılma noktasından
///   iki yana dalga hâlinde tahtalar ve halatlar aşağı çöker (ağ: durum + kırılma tahtası). Gürültü Dinleyen'i çeker.
///   Köprü bir kez yıkılınca geri gelmez; arkadakiler yan duvardaki dar kaya patikasından (ChasmRoom/Ledge) geçer.
/// - Uçuruma düşen oyuncu ÖLÜR (mevcut ölüm sistemi → izleyici). Düşerken yerel: rüzgâr sesi, ekran kararır, dipte
///   sert çarpma sesi + kamera sarsıntısı. Diğer oyuncular dipten gelen gümlemeyi duyar.
/// Ekranda yazı yok.
/// </summary>
public class CollapsingBridge : NetworkBehaviour
{
    [SerializeField] private float sagPerPlayer = 0.07f;
    [Tooltip("Uçurum alanı (z aralığı) — bu aralıkta düşen oyuncu yakalanır.")]
    [SerializeField] private Vector2 pitZRange = new Vector2(69.5f, 93.5f);
    [Tooltip("Bu yüksekliğin altında düşüş hissi başlar.")]
    [SerializeField] private float fallFeelY = -302.5f;
    [Tooltip("Bu yüksekliğin altına inen oyuncu ölür (dip ≈ -321.5).")]
    [SerializeField] private float deathY = -317f;
    [SerializeField] private float collapseNoise = 22f;
    [Tooltip("Koşu/inişten çöküşe kadar sarsıntı süresi (sn).")]
    [SerializeField] private float shakeSeconds = 0.55f;
    [Tooltip("Tahta başına çöküş dalgası gecikmesi (sn).")]
    [SerializeField] private float waveDelay = 0.035f;
    [Tooltip("Tahtalarla birlikte düşen halatlar (Ropes altındaki 'Rope' nesneleri).")]
    [SerializeField] private Transform ropesRoot;
    [Tooltip("B2-10: düşen oyuncunun bu eşyaları (kristal parçası) köprü başında yeniden belirir.")]
    [SerializeField] private ItemData rescueItem;
    [SerializeField] private Transform rescuePoint;
    [Tooltip("B2-11: köprü yıkılınca açılan NavMeshObstacle (carve) — yaratık yıkık köprüyü yol sanmasın.")]
    [SerializeField] private UnityEngine.AI.NavMeshObstacle bridgeBlocker;

    private readonly NetworkVariable<bool> collapsed = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<int> breakIndex = new NetworkVariable<int>(0);

    private Transform[] planks;
    private Vector3[] restPos;
    private Quaternion[] restRot;
    private float[] sag;
    private Vector3[] fallSpin;
    private Transform[] ropes;
    private Vector3[] ropePos;
    private Quaternion[] ropeRot;
    private Vector3[] ropeSpin;
    private float lastCreak;
    private float collapseAt = -1f; // yerel zaman (sarsıntı başlangıcı)
    private Bounds area;
    private PlayerController[] players = new PlayerController[0];
    private float nextPlayerScan;

    // düşüş hissi (yalnızca yerel oyuncu)
    private AudioSource windSource;
    private Image blackout;
    private float blackAlpha;
    private bool localFalling;
    private bool localImpacted;
    private float fallStart;
    private readonly System.Collections.Generic.HashSet<PlayerLife> thudded = new System.Collections.Generic.HashSet<PlayerLife>();

    private void Awake() { Cache(); }

    private void Cache()
    {
        if (planks != null) return;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in transform) if (c.name.StartsWith("Plank_")) list.Add(c);
        planks = list.ToArray();
        int n = planks.Length;
        restPos = new Vector3[n]; restRot = new Quaternion[n]; sag = new float[n]; fallSpin = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            restPos[i] = planks[i].localPosition; restRot[i] = planks[i].localRotation;
            var col = planks[i].GetComponent<Collider>();
            if (i == 0) area = col.bounds; else area.Encapsulate(col.bounds);
        }
        area.Expand(new Vector3(0.5f, 3f, 0.5f));

        var rl = new System.Collections.Generic.List<Transform>();
        if (ropesRoot != null) foreach (Transform c in ropesRoot) if (c.name == "Rope") rl.Add(c);
        ropes = rl.ToArray();
        ropePos = new Vector3[ropes.Length]; ropeRot = new Quaternion[ropes.Length]; ropeSpin = new Vector3[ropes.Length];
        for (int i = 0; i < ropes.Length; i++) { ropePos[i] = ropes[i].position; ropeRot[i] = ropes[i].rotation; }
    }

    public override void OnNetworkSpawn()
    {
        collapsed.OnValueChanged += OnCollapsedChanged;
        Noise.Emitted += OnNoise;
        if (collapsed.Value)
        {
            if (bridgeBlocker != null) bridgeBlocker.enabled = true;
            collapseAt = Time.time - 10f;
            foreach (Transform t in planks) t.gameObject.SetActive(false);
            foreach (Transform r in ropes) r.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        collapsed.OnValueChanged -= OnCollapsedChanged;
        Noise.Emitted -= OnNoise;
    }

    public override void OnDestroy()
    {
        if (blackout != null) Destroy(blackout.canvas.gameObject);
        base.OnDestroy();
    }

    private int PlankAt(Vector3 feet)
    {
        if (!area.Contains(feet)) return -1;
        int best = -1; float bestD = 0.8f;
        for (int i = 0; i < planks.Length; i++)
        {
            Vector3 w = planks[i].parent.TransformPoint(restPos[i]);
            if (Mathf.Abs(feet.x - w.x) > 1.3f) continue;
            float d = Mathf.Abs(feet.z - w.z);
            if (d < bestD && feet.y > w.y - 1f && feet.y < w.y + 2f) { bestD = d; best = i; }
        }
        return best;
    }

    // Adım/iniş olayları her kopyada gelir: gıcırtı yerel, yıkılma kararı sunucuda.
    private void OnNoise(Vector3 pos, float radius)
    {
        if (collapsed.Value || collapseAt >= 0f) return;
        int i = PlankAt(pos);
        if (i < 0) return;
        bool heavy = radius >= Noise.RunStep - 0.01f; // koşu adımı ya da iniş
        if (Sfx.Lib != null && Time.time - lastCreak > (heavy ? 0.2f : 0.9f))
        {
            lastCreak = Time.time;
            Sfx.At(Sfx.Lib.doorCreak, planks[i].position, heavy ? 0.6f : 0.22f, heavy ? 0.7f : 1.05f, 0.12f, 1.5f, 18f);
        }
        if (IsServer && IsSpawned && heavy)
        {
            breakIndex.Value = i;
            collapsed.Value = true;
            StartCoroutine(DropCreatures());
        }
    }

    // B2-11: köprü üstündeki yaratık da düşer
    private System.Collections.IEnumerator DropCreatures()
    {
        yield return new WaitForSeconds(shakeSeconds + 0.1f);
        foreach (var c in FindObjectsByType<ListenerCreature>(FindObjectsSortMode.None))
        {
            Vector3 p = c.transform.position;
            if (p.z > pitZRange.x && p.z < pitZRange.y && Mathf.Abs(p.x) < 1.6f) c.ServerFall();
        }
    }

    private void OnCollapsedChanged(bool oldV, bool newV)
    {
        if (newV && bridgeBlocker != null) bridgeBlocker.enabled = true; // yaratık yıkık köprüden yürümesin (navmesh oyulur)
        if (!newV) return;
        collapseAt = Time.time;
        for (int i = 0; i < planks.Length; i++)
            fallSpin[i] = new Vector3(Random.Range(-220f, 220f), Random.Range(-50f, 50f), Random.Range(-160f, 160f));
        for (int i = 0; i < ropes.Length; i++)
            ropeSpin[i] = new Vector3(Random.Range(-90f, 90f), Random.Range(-30f, 30f), Random.Range(-90f, 90f));
        if (Sfx.Lib != null)
        {
            Vector3 at = planks[Mathf.Clamp(breakIndex.Value, 0, planks.Length - 1)].position;
            Sfx.At(Sfx.Lib.doorCreak, at, 1f, 0.55f, 0.04f, 2f, 35f);
            Sfx.At(Sfx.Lib.rumble, at, 0.6f, 1.3f, 0.05f, 4f, 50f);
            StartCoroutine(CollapseSounds(at));
        }
    }

    private System.Collections.IEnumerator CollapseSounds(Vector3 at)
    {
        yield return new WaitForSeconds(shakeSeconds);
        if (Sfx.Lib == null) yield break;
        Sfx.At(Sfx.Lib.treeCrash, at, 0.9f, 1.25f, 0.06f, 3f, 45f);
        Noise.Emit(at, collapseNoise);
        for (int k = 0; k < 6; k++)
        {
            yield return new WaitForSeconds(Random.Range(0.12f, 0.3f));
            Sfx.At(Sfx.Lib.treeCrash, at + new Vector3(0f, -2f, Random.Range(-8f, 8f)), Random.Range(0.35f, 0.6f), Random.Range(1.5f, 2f), 0.08f, 3f, 40f);
        }
        yield return new WaitForSeconds(1.2f);
        Sfx.At(Sfx.Lib.rockHit, at + Vector3.down * 20f, 0.7f, 0.6f, 0.08f, 5f, 50f);
        Sfx.At(Sfx.Lib.rockHit, at + Vector3.down * 20f + Vector3.forward * 6f, 0.5f, 0.7f, 0.08f, 5f, 50f);
    }

    private void Update()
    {
        if (planks == null) return;
        if (collapseAt >= 0f) AnimateCollapse(Time.time - collapseAt);
        else AnimateSag(Time.deltaTime);
        FallFeel(Time.deltaTime);
        if (IsServer && IsSpawned) ServerTick();
    }

    private void AnimateSag(float dt)
    {
        for (int i = 0; i < sag.Length; i++) sag[i] = 0f;
        if (Time.time >= nextPlayerScan)
        {
            nextPlayerScan = Time.time + 1f;
            players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        }
        foreach (var p in players)
        {
            if (p == null) continue;
            int i = PlankAt(p.transform.position);
            if (i < 0) continue;
            for (int k = Mathf.Max(0, i - 4); k <= Mathf.Min(planks.Length - 1, i + 4); k++)
                sag[k] += sagPerPlayer * (1f - Mathf.Abs(k - i) / 5f);
        }
        for (int i = 0; i < planks.Length; i++)
        {
            float wobble = sag[i] > 0f ? Mathf.Sin(Time.time * 9f + i) * 0.012f : 0f;
            Vector3 target = restPos[i] + Vector3.down * (sag[i] + wobble);
            planks[i].localPosition = Vector3.Lerp(planks[i].localPosition, target, 1f - Mathf.Exp(-12f * dt));
        }
    }

    private void AnimateCollapse(float t)
    {
        int b = Mathf.Clamp(breakIndex.Value, 0, planks.Length - 1);
        if (t < shakeSeconds)
        {
            // sarsıntı: kırılma noktasına yakın tahtalar daha çok titrer ve çöker
            for (int i = 0; i < planks.Length; i++)
            {
                float near = 1f - Mathf.Clamp01(Mathf.Abs(i - b) / 12f);
                float j = 0.04f + 0.08f * near;
                planks[i].localPosition = restPos[i] + new Vector3(Mathf.Sin(t * 47f + i) * j * 0.4f, -t * 0.5f * near + Mathf.Sin(t * 61f + i * 1.7f) * j, 0f);
                planks[i].localRotation = restRot[i] * Quaternion.Euler(Mathf.Sin(t * 53f + i) * 6f * near, 0f, Mathf.Sin(t * 41f + i) * 5f * near);
            }
            return;
        }
        float tc = t - shakeSeconds;
        for (int i = 0; i < planks.Length; i++)
        {
            Transform p = planks[i];
            if (!p.gameObject.activeSelf) continue;
            float ft = tc - Mathf.Abs(i - b) * waveDelay;
            if (ft < 0f) continue;
            var col = p.GetComponent<Collider>(); if (col != null && col.enabled) col.enabled = false;
            float side = (i < planks.Length / 2 ? -1f : 1f) * Mathf.Clamp01(Mathf.Abs(i - b) / 16f);
            p.localPosition = restPos[i] + new Vector3(0f, -0.5f * 9.8f * ft * ft, side * ft * 1.5f);
            p.localRotation = Quaternion.Euler(fallSpin[i] * ft) * restRot[i];
            if (ft > 3f) p.gameObject.SetActive(false);
        }
        float z0 = planks[0].parent.TransformPoint(restPos[0]).z;
        for (int i = 0; i < ropes.Length; i++)
        {
            Transform r = ropes[i];
            if (!r.gameObject.activeSelf) continue;
            int idx = Mathf.Clamp(Mathf.RoundToInt((ropePos[i].z - z0) / 0.75f), 0, planks.Length - 1);
            float ft = tc - Mathf.Abs(idx - b) * waveDelay - 0.05f;
            if (ft < 0f) continue;
            r.SetPositionAndRotation(ropePos[i] + Vector3.down * (0.5f * 9.8f * ft * ft), Quaternion.Euler(ropeSpin[i] * ft) * ropeRot[i]);
            if (ft > 3f) r.gameObject.SetActive(false);
        }
    }

    private bool InPit(Vector3 p) => p.z > pitZRange.x && p.z < pitZRange.y && Mathf.Abs(p.x) < 8.5f;

    // ---------- düşüş hissi ----------
    private void FallFeel(float dt)
    {
        PlayerLife local = null;
        foreach (var l in PlayerLife.All)
        {
            if (l == null) continue;
            Vector3 p = l.transform.position;
            // herkes: dipten gelen gümleme (her oyuncu için bir kez)
            if (InPit(p) && p.y < deathY - 2f && !thudded.Contains(l))
            {
                thudded.Add(l);
                if (!l.IsOwner && Sfx.Lib != null)
                {
                    Sfx.At(Sfx.Lib.impactBoom, p, 0.6f, 1.2f, 0.05f, 4f, 45f);
                    Sfx.At(Sfx.Lib.rockHit, p, 0.8f, 0.7f, 0.05f, 4f, 45f);
                }
            }
            else if (p.y > fallFeelY) thudded.Remove(l); // yeniden başlatma sonrası tekrar duyulabilsin
            if (l.IsOwner) local = l;
        }

        bool falling = false;
        if (local != null && local.State != PlayerLifeState.Dead)
        {
            Vector3 p = local.transform.position;
            falling = InPit(p) && p.y < fallFeelY;
        }
        if (falling && !localFalling) { localFalling = true; fallStart = Time.time; EnsureFallFx(); if (windSource.clip != null) windSource.Play(); }

        if (localFalling && local != null)
        {
            Vector3 p = local.transform.position;
            float k = Mathf.InverseLerp(fallFeelY, deathY, p.y);
            if (windSource != null) { windSource.volume = Mathf.Lerp(0.2f, 1f, k); windSource.pitch = Mathf.Lerp(0.8f, 1.35f, k); }
            blackAlpha = Mathf.Max(blackAlpha, Mathf.Lerp(0f, 0.75f, k * k));
            var feel = local.GetComponent<PlayerFeel>();
            if (feel != null && !localImpacted) feel.Kick(new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1.5f, 1.5f)) * (0.4f + k));
            if (!localImpacted && (p.y < deathY - 2f || local.State == PlayerLifeState.Dead))
            {
                localImpacted = true;
                blackAlpha = 1f;
                if (windSource != null) windSource.Stop();
                if (feel != null) feel.Kick(new Vector3(14f, 0f, Random.Range(-8f, 8f)));
                if (Sfx.Lib != null)
                {
                    Sfx.UI(Sfx.Lib.impactBoom, 1f, 0.9f, 0.03f);
                    Sfx.UI(Sfx.Lib.rockHit, 1f, 0.55f, 0.03f);
                    Sfx.UI(Sfx.Lib.heartbeat, 0.8f, 0.8f, 0.02f);
                }
            }
            if (localImpacted && Time.time - fallStart > 1f && local.State == PlayerLifeState.Dead && p.y > deathY)
                localFalling = false; // izleyiciye geçti / başka yere taşındı
        }
        if (!localFalling || localImpacted)
            blackAlpha = Mathf.MoveTowards(blackAlpha, 0f, dt * (localImpacted ? 0.35f : 2f));
        if (localImpacted && blackAlpha <= 0.001f) { localImpacted = false; localFalling = false; }
        if (blackout != null)
        {
            blackout.color = new Color(0f, 0f, 0f, blackAlpha);
            blackout.enabled = blackAlpha > 0.001f;
        }
    }

    private void EnsureFallFx()
    {
        if (windSource == null)
        {
            windSource = gameObject.AddComponent<AudioSource>();
            windSource.spatialBlend = 0f; windSource.loop = true; windSource.playOnAwake = false; windSource.volume = 0f;
            if (Sfx.Lib != null) windSource.clip = Sfx.Lib.fallWind;
        }
        if (blackout == null)
        {
            var go = new GameObject("FallBlackout");
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
            var img = new GameObject("Black", typeof(RectTransform)); img.transform.SetParent(go.transform, false);
            var rt = (RectTransform)img.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            blackout = img.AddComponent<Image>(); blackout.raycastTarget = false; blackout.color = new Color(0f, 0f, 0f, 0f);
        }
    }

    // ---------- sunucu: dibe düşen ölür ----------
    private void ServerTick()
    {
        foreach (var l in PlayerLife.All)
        {
            if (l == null || l.State == PlayerLifeState.Dead) continue;
            Vector3 p = l.transform.position;
            if (InPit(p) && p.y < deathY)
            {
                l.ServerKill();
                RescueItems(l);
            }
        }
    }

    // B2-10: uçuruma düşenin taşıdığı kristal parçaları kaybolmasın (labirent yuvaları için gerekli) → köprü başında belirir
    private void RescueItems(PlayerLife l)
    {
        if (rescueItem == null || rescueItem.WorldPrefab == null || rescuePoint == null) return;
        var inv = l.GetComponent<InventoryNetworkSync>();
        if (inv == null) return;
        int id = inv.IdOf(rescueItem);
        for (int s = 0; s < InventorySystem.SlotCount; s++)
        {
            if (inv.GetSlotItemId(s) != id) continue;
            inv.ServerTakeItem(rescueItem);
            Vector3 pos = rescuePoint.position + new Vector3(Random.Range(-0.6f, 0.6f), 0.4f, Random.Range(-0.4f, 0.4f));
            GameObject go = Instantiate(rescueItem.WorldPrefab, pos, Random.rotation);
            var no = go.GetComponent<NetworkObject>();
            if (no == null) { Destroy(go); continue; }
            no.Spawn(true);
        }
    }
}
