using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// B2-04 (GDD 7.1): "Dinleyen" — kör, sese tepki veren mağara yaratığı. Yapay zeka SUNUCUDA (NavMeshAgent), konum
/// NetworkTransform ile herkese. Işığı görmez; yalnızca gürültüyü (Noise) duyar:
/// - Gezinme: geçitte yavaşça dolaşır, ara sıra tık sesleri çıkarır (yankıyla yer bulma).
/// - Duyunca: başını sese çevirir, keskin tık + hırıltı (herkes duyar), sesin geldiği yere hızla gider.
/// - Arama: oraya varınca birkaç saniye dinler; yeni ses yoksa gezinmeye döner.
/// - Çok yakındaki oyuncuyu (titreşim) sezer. Yetişirse ZEHİR ekler (Bölüm 1 yaratıklarıyla aynı dil), kısa bekleme.
/// - Mızrak: öldürmez; geri iter ve sersemletir (ama mızrak sesi de gürültüdür).
/// Bölgesi dışındaki sesleri yok sayar (giriş mağarası güvenli). Ekranda yazı yok.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ListenerCreature : NetworkBehaviour, ISpearTarget
{
    public enum Mood : byte { Wander = 0, Investigate = 1, Search = 2, Stunned = 3 }

    [Header("Hız (m/sn) — oyuncu yürüme 4, koşu 6.5")]
    [SerializeField] private float wanderSpeed = 1.3f;
    [SerializeField] private float huntSpeed = 4.4f;

    [Header("Duyma")]
    [Tooltip("Duyduğu yarıçapın çarpanı (1 = gürültü yarıçapı kadar).")]
    [SerializeField] private float hearingMultiplier = 1f;
    [Tooltip("Bu mesafedeki oyuncuyu sessiz olsa da sezer (titreşim).")]
    [SerializeField] private float feelRadius = 2.3f;
    [SerializeField] private float searchSeconds = 4f;
    [Tooltip("Yaratığın dolaşıp avlanabildiği bölge (dünya XZ dikdörtgenleri). Dışındaki sesler yok sayılır.")]
    [SerializeField] private Rect[] territory = new Rect[0];
    [SerializeField] private float territoryY = -300f;

    [Header("Saldırı (zehir)")]
    [SerializeField] private float attackRange = 1.7f;
    [SerializeField] private float attackPoison = 22f;
    [SerializeField] private float attackCooldown = 2.5f;
    [Tooltip("M-12: vuruş öncesi kolları kaldırma süresi (oyuncu kaçabilir).")]
    [SerializeField] private float windupSeconds = 0.5f;
    [Tooltip("Vuruş anında bu mesafedeki hedefe isabet eder.")]
    [SerializeField] private float hitReach = 2.4f;
    [SerializeField] private float knockbackSpeed = 7f;

    [Header("Mızrak")]
    [SerializeField] private float stunSeconds = 2.2f;
    [SerializeField] private float pushDistance = 2.5f;
    [Tooltip("M-12: sersemledikten sonra bu süre daha keskin duyar.")]
    [SerializeField] private float enragedSeconds = 6f;
    [SerializeField] private float enragedHearing = 1.4f;

    [Header("Davranış (M-12)")]
    [Tooltip("Uzak/zayıf seste temkinli yürüme hızı; yakın/güçlü seste huntSpeed.")]
    [SerializeField] private float cautiousSpeed = 2.4f;
    [SerializeField] private float sniffRadius = 4.5f;
    [SerializeField] private Vector2 wanderPause = new Vector2(1.5f, 3.5f);

    [System.Serializable]
    public class Zone
    {
        public string name;
        public Rect[] rects = new Rect[0];
        [Tooltip("Yalnızca duyma için ek alanlar (gezinme noktası seçilmez; ör. köprü, duvar patikası).")]
        public Rect[] listenRects = new Rect[0];
    }

    [Header("Bölgeler (B2-11, KULLANICI: oyuncuların bulunduğu bölüme gelir)")]
    [Tooltip("Sıralı bölgeler (geçit, uçurum, labirent, ışın odası). Yaratık oyuncuların çoğunun bulunduğu bölgeye taşınır. Boşsa 'territory' tek bölge.")]
    [SerializeField] private Zone[] zones = new Zone[0];
    [Tooltip("Oyuncular yeni bölgeye geçtikten sonra yaratığın peşlerinden gelme gecikmesi (sn).")]
    [SerializeField] private Vector2 followDelay = new Vector2(7f, 13f);
    [Tooltip("Yürüyerek gidilecek en uzun yol (m). Işınlanma yok: yol yoksa/uzunsa o bölgeye gelmez.")]
    [SerializeField] private float maxWalkPath = 60f;

    [Header("Uyanıklık / iz sürme (B2-11)")]
    [Tooltip("Ses duydukça artan uyanıklık: hız ve duyma biraz artar, yavaşça söner.")]
    [SerializeField] private float alertDecay = 0.05f;
    [SerializeField] private float alertSpeedBonus = 0.15f;
    [SerializeField] private float alertHearingBonus = 0.3f;
    [Tooltip("Arama bitince bu süre son duyduğu yerin çevresinde dolaşır (iz sürer).")]
    [SerializeField] private float stalkSeconds = 20f;
    [SerializeField] private float stalkRadius = 7f;

    private readonly NetworkVariable<Mood> mood = new NetworkVariable<Mood>(Mood.Wander);
    private readonly NetworkVariable<Vector3> attention = new NetworkVariable<Vector3>(Vector3.zero);

    public Mood CurrentMood => mood.Value;
    /// <summary>Yaratığın kulak verdiği nokta (görsel: baş oraya döner).</summary>
    public Vector3 Attention => attention.Value;

    private NavMeshAgent agent;
    private Vector3 noisePoint;
    private float noiseStrength;
    private bool hasNoise;
    private float searchUntil, stunUntil, nextAttack, nextWander, nextClick, nextFeelCheck;
    private PlayerLife attackTarget;
    private float attackAt, enragedUntil, nextSniff, pauseUntil, stuckTime;
    private Vector3 lastPos;

    private int currentZone;            // yaratığın şu an avlandığı bölge
    private int pendingZone = -1;       // oyuncuların geçtiği bölge (gecikmeli takip)
    private float relocateAt, nextZoneCheck;
    private bool walkingToZone;
    private float alert;                // 0..1 uyanıklık
    private Vector3 lastHeard, prevHeard;
    private bool hasPrevHeard;
    private float stalkUntil, fallSpeed;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public override void OnNetworkSpawn()
    {
        agent.enabled = IsServer; // istemciler NetworkTransform'u izler
        if (IsServer)
        {
            agent.Warp(transform.position);
            Noise.Emitted += OnNoise;
            agent.speed = wanderSpeed;
            nextWander = Time.time + 1f;
            int z = ZoneOf(transform.position);
            currentZone = z >= 0 ? z : 0;
        }
    }

    // ---------------- Bölgeler ----------------

    private int ZoneCount => zones != null && zones.Length > 0 ? zones.Length : 1;

    private Rect[] RectsOf(int zone)
    {
        if (zones == null || zones.Length == 0) return territory;
        return zones[Mathf.Clamp(zone, 0, zones.Length - 1)].rects;
    }

    private int ZoneOf(Vector3 p)
    {
        if (Mathf.Abs(p.y - territoryY) > 6f) return -1;
        Vector2 xz = new Vector2(p.x, p.z);
        for (int z = 0; z < ZoneCount; z++)
        {
            Rect[] rs = RectsOf(z);
            if (rs == null) continue;
            foreach (Rect r in rs) if (r.Contains(xz)) return z;
        }
        return -1;
    }

    /// <summary>Hayattaki oyuncuların çoğunun bulunduğu bölge (eşitlikte daha ilerideki). Kimse bölgede değilse -1.</summary>
    private int PlayersZone()
    {
        int[] count = new int[ZoneCount];
        bool any = false;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            int z = ZoneOf(p.transform.position);
            if (z >= 0) { count[z]++; any = true; }
        }
        if (!any) return -1;
        int best = 0;
        for (int z = 1; z < count.Length; z++) if (count[z] >= count[best]) best = z;
        return best;
    }

    private void UpdateZoneFollow(float now)
    {
        if (now >= nextZoneCheck)
        {
            nextZoneCheck = now + 1f;
            int pz = PlayersZone();
            if (pz >= 0 && pz != currentZone)
            {
                if (pendingZone != pz) { pendingZone = pz; relocateAt = now + Random.Range(followDelay.x, followDelay.y); }
            }
            else pendingZone = -1;
        }
        // avlanırken/saldırırken bölge değiştirmez
        if (pendingZone < 0 || now < relocateAt || attackTarget != null || mood.Value == Mood.Investigate) return;

        // KULLANICI: ışınlanma yok — yalnızca yürüyerek gelir. Yol yoksa (kapalı kapı, yıkık köprü) gelemez; sonra tekrar dener.
        int target = pendingZone;
        Vector3 dest = RandomPointIn(target);
        bool walk = false;
        if (agent.isOnNavMesh && NavMesh.SamplePosition(dest, out NavMeshHit h, 3f, NavMesh.AllAreas))
        {
            var path = new NavMeshPath();
            if (agent.CalculatePath(h.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                float len = 0f; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                walk = len <= maxWalkPath;
                dest = h.position;
            }
        }
        if (!walk) { relocateAt = now + 10f; return; }
        pendingZone = -1;
        currentZone = target;
        mood.Value = Mood.Wander; hasNoise = false;
        walkingToZone = true;
        agent.speed = wanderSpeed * 1.3f; attention.Value = dest; SetDest(dest);
        nextWander = now + 30f;
    }

    /// <summary>Sunucu: altındaki köprü çöktü (CollapsingBridge çağırır) → navmesh'ten kopar ve düşer.</summary>
    public void ServerFall()
    {
        if (!IsServer || !agent.enabled) return;
        attackTarget = null;
        agent.enabled = false;
        fallSpeed = 0f;
    }

    /// <summary>Navmesh dışında (ör. üstündeyken köprü çöktü): yer çekimiyle düşer; uçurumun dibinde kalır.</summary>
    private void FallOffMesh(float dt)
    {
        if (fallSpeed < 0f) return; // düşüş bitti
        fallSpeed += 9.8f * dt;
        Vector3 p = transform.position + Vector3.down * fallSpeed * dt;
        if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, fallSpeed * dt + 0.25f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y < transform.position.y)
        {
            p = hit.point;
            if (fallSpeed > 6f) LandRpc(p);
            fallSpeed = -1f;
        }
        transform.position = p;
    }

    private Vector3 RandomPointIn(int zone)
    {
        Rect[] rs = RectsOf(zone);
        if (rs == null || rs.Length == 0) return transform.position + Random.insideUnitSphere * 8f;
        Rect r = rs[Random.Range(0, rs.Length)];
        return new Vector3(Random.Range(r.xMin + 1f, r.xMax - 1f), territoryY, Random.Range(r.yMin + 1f, r.yMax - 1f));
    }

    public override void OnNetworkDespawn()
    {
        Noise.Emitted -= OnNoise;
    }

    /// <summary>Şu anki avlanma bölgesinde mi (diğer bölgelerdeki sesler yok sayılır; giriş mağarası güvenli).</summary>
    private bool InTerritory(Vector3 p)
    {
        Rect[] rs = RectsOf(currentZone);
        if (rs == null || rs.Length == 0) return true;
        if (Mathf.Abs(p.y - territoryY) > 6f) return false;
        foreach (Rect r in rs) if (r.Contains(new Vector2(p.x, p.z))) return true;
        if (zones != null && currentZone >= 0 && currentZone < zones.Length && zones[currentZone].listenRects != null)
            foreach (Rect r in zones[currentZone].listenRects) if (r.Contains(new Vector2(p.x, p.z))) return true;
        return false;
    }

    // ---------------- Sunucu ----------------

    private void OnNoise(Vector3 pos, float radius)
    {
        if (!IsServer || !IsSpawned || mood.Value == Mood.Stunned) return;
        if (!InTerritory(pos)) return;
        float d = Vector3.Distance(pos, transform.position);
        float heard = radius * hearingMultiplier * (Time.time < enragedUntil ? enragedHearing : 1f) * (1f + alert * alertHearingBonus);
        if (d > heard) return;
        float strength = 1f - d / Mathf.Max(0.1f, heard);
        // daha güçlü/yakın ses öncekini ezer
        if (hasNoise && mood.Value == Mood.Investigate && strength < noiseStrength * 0.6f) return;
        bool wasCalm = mood.Value == Mood.Wander;
        walkingToZone = false;
        // iz: son iki sesin yönü (arama sırasında hedefin gittiği yöne bakar)
        if (Time.time < stalkUntil + 10f || hasNoise) { prevHeard = lastHeard; hasPrevHeard = true; } else hasPrevHeard = false;
        lastHeard = pos;
        alert = Mathf.Clamp01(alert + 0.1f + 0.25f * strength);
        noisePoint = pos; noiseStrength = strength; hasNoise = true;
        attention.Value = pos;
        mood.Value = Mood.Investigate;
        // yakın/güçlü ses → koşar; uzak/zayıf → temkinli yaklaşır (uyanıkken biraz daha hızlı)
        agent.speed = Mathf.Lerp(cautiousSpeed, huntSpeed, Mathf.Clamp01(strength * 1.6f)) * (1f + alert * alertSpeedBonus);
        pauseUntil = 0f;
        if (attackTarget == null) SetDest(pos);
        if (wasCalm) AlertRpc(pos);
    }

    private void SetDest(Vector3 p)
    {
        if (!agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerTick();
    }

    private void ServerTick()
    {
        float now = Time.time;
        alert = Mathf.Max(0f, alert - alertDecay * Time.deltaTime);
        if (mood.Value == Mood.Stunned)
        {
            if (now >= stunUntil) { mood.Value = Mood.Search; searchUntil = now + searchSeconds; }
            return;
        }

        // navmesh dışına düştüyse (ör. çöken köprü) bulunduğu bölgede bir noktadan yeniden belirir
        if (!agent.isOnNavMesh) { FallOffMesh(Time.deltaTime); return; }

        UpdateZoneFollow(now);

        // titreşim: çok yakındaki oyuncuyu sezer
        if (now >= nextFeelCheck)
        {
            nextFeelCheck = now + 0.25f;
            PlayerLife near = NearestAlive(out float nd);
            if (near != null && nd < feelRadius && InTerritory(near.transform.position)) OnNoise(near.transform.position, feelRadius + 0.5f);
        }

        // saldırı: hazırlık (kollar kalkar, durur, hedefe döner) → vuruş anında hâlâ menzildeyse isabet
        if (attackTarget != null)
        {
            Vector3 to = attackTarget.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            if (now >= attackAt) ResolveAttack(now);
            return;
        }
        if (mood.Value != Mood.Wander && now >= nextAttack)
        {
            PlayerLife p = NearestAlive(out float d);
            if (p != null && d <= attackRange)
            {
                attackTarget = p; attackAt = now + windupSeconds;
                if (agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
                attention.Value = p.transform.position;
                WindupRpc(windupSeconds);
                return;
            }
        }

        // takılma: yolu var ama ilerlemiyorsa yeni hedef
        if (agent.isOnNavMesh && agent.hasPath && !agent.pathPending && agent.remainingDistance > 1.5f && now >= pauseUntil)
        {
            stuckTime = (transform.position - lastPos).sqrMagnitude < 0.0004f ? stuckTime + Time.deltaTime : 0f;
            if (stuckTime > 2f) { stuckTime = 0f; SetDest(RandomTerritoryPoint()); }
        }
        lastPos = transform.position;

        switch (mood.Value)
        {
            case Mood.Investigate:
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 1.2f)
                {
                    mood.Value = Mood.Search; searchUntil = now + searchSeconds + 2f + alert * 3f; hasNoise = false;
                    agent.ResetPath(); nextSniff = now + 0.8f;
                    // ilk koklama: ses hareket ediyorduysa gittiği yöne doğru (kaçanın peşinden)
                    if (hasPrevHeard)
                    {
                        Vector3 dir = lastHeard - prevHeard; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.5f && dir.sqrMagnitude < 400f)
                        {
                            Vector3 ahead = noisePoint + dir.normalized * 3.5f;
                            if (InTerritory(ahead)) { agent.speed = wanderSpeed * 1.8f; attention.Value = ahead; SetDest(ahead); nextSniff = now + 1.6f; }
                        }
                    }
                }
                break;
            case Mood.Search:
                // sesin geldiği yerin çevresini koklayarak tarar (kısa hamleler + duruş)
                if (now >= nextSniff)
                {
                    nextSniff = now + Random.Range(1.4f, 2.4f);
                    Vector2 c = Random.insideUnitCircle * sniffRadius;
                    Vector3 p = noisePoint + new Vector3(c.x, 0f, c.y);
                    if (InTerritory(p)) { agent.speed = wanderSpeed * 1.6f; attention.Value = p; SetDest(p); }
                }
                if (now >= searchUntil) { mood.Value = Mood.Wander; agent.speed = wanderSpeed; nextWander = now + 1.5f; stalkUntil = now + stalkSeconds; }
                break;
            case Mood.Wander:
                if (walkingToZone)
                {
                    // yeni bölgeye yürüyor: varınca normal gezinme
                    if (!agent.pathPending && agent.remainingDistance < 1f) { walkingToZone = false; agent.speed = wanderSpeed; nextWander = now + 1f; }
                    else { if (now >= nextClick) { nextClick = now + Random.Range(3f, 6f); ClickRpc(); } break; }
                }
                // her noktada durup dinler, sonra yeni noktaya (uyanıkken daha kısa durur)
                bool arrived = agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 0.8f;
                if (arrived && pauseUntil <= 0f) pauseUntil = now + Random.Range(wanderPause.x, wanderPause.y) * (1f - 0.5f * alert);
                if (now >= nextWander || (pauseUntil > 0f && now >= pauseUntil))
                {
                    pauseUntil = 0f;
                    nextWander = now + Random.Range(8f, 13f);
                    // iz sürme: son duyduğu yerin çevresinde dolaşır
                    Vector3 p = RandomTerritoryPoint();
                    if (now < stalkUntil)
                    {
                        Vector2 c = Random.insideUnitCircle * stalkRadius;
                        Vector3 sp = lastHeard + new Vector3(c.x, 0f, c.y);
                        if (InTerritory(sp)) p = sp;
                    }
                    attention.Value = p;
                    agent.speed = wanderSpeed;
                    SetDest(p);
                }
                if (now >= nextClick) { nextClick = now + Random.Range(4f, 8f); ClickRpc(); }
                break;
        }
    }

    private void ResolveAttack(float now)
    {
        PlayerLife p = attackTarget; attackTarget = null;
        if (agent.isOnNavMesh) agent.isStopped = false;
        nextAttack = now + attackCooldown;
        if (p != null && p.State == PlayerLifeState.Alive && Vector3.Distance(p.transform.position, transform.position) <= hitReach)
        {
            PoisonSystem poison = p.GetComponent<PoisonSystem>();
            if (poison != null) poison.AddPoisonIgnoringProtection(attackPoison); // taşınan totem yumruğu engellemez
            Vector3 dir = p.transform.position - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            AttackRpc(p.transform.position, p.OwnerClientId, dir * knockbackSpeed);
        }
        else WhiffRpc();
    }

    private Vector3 RandomTerritoryPoint() => RandomPointIn(currentZone);

    private PlayerLife NearestAlive(out float dist)
    {
        PlayerLife best = null; dist = float.MaxValue;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            float d = Vector3.Distance(p.transform.position, transform.position);
            if (d < dist) { dist = d; best = p; }
        }
        return best;
    }

    public void OnSpearHit(ulong attackerClientId, Vector3 point, Vector3 direction)
    {
        if (!IsServer || mood.Value == Mood.Stunned) return;
        Vector3 dir = direction; dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = -transform.forward; dir.Normalize();
        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            Vector3 to = transform.position + dir * pushDistance;
            if (NavMesh.SamplePosition(to, out NavMeshHit hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }
        mood.Value = Mood.Stunned;
        stunUntil = Time.time + stunSeconds;
        nextAttack = Time.time + stunSeconds + 0.8f;
        enragedUntil = Time.time + stunSeconds + enragedSeconds;
        attackTarget = null; if (agent.isOnNavMesh) agent.isStopped = false; // hazırlık bozulur
        HurtRpc();
    }

    // ---------------- Tüm kopyalar: ses ----------------

    private Vector3 Head => transform.position + Vector3.up * 2.1f;

    [Rpc(SendTo.Everyone)]
    private void AlertRpc(Vector3 at)
    {
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        // keskin tık dizisi + boğuk hırıltı: "seni duydum"
        Sfx.At(lib.flashlightClick, Head, 1f, 0.55f, 0.05f, 3f, 35f);
        Sfx.At(lib.spiderAttack, Head, 0.8f, 0.55f, 0.04f, 3f, 35f);
        Sfx.At(lib.flashlightClick, Head, 0.9f, 0.7f, 0.05f, 3f, 35f);
    }

    [Rpc(SendTo.Everyone)]
    private void ClickRpc()
    {
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        Sfx.At(lib.flashlightClick, Head, 0.55f, Random.Range(0.45f, 0.6f), 0.05f, 2f, 22f);
    }

    [Rpc(SendTo.Everyone)]
    private void WindupRpc(float seconds)
    {
        var drv = GetComponentInChildren<GolemAnimatorDriver>(); if (drv != null) drv.PlayAttack(seconds);
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        Sfx.At(lib.spiderAttack, Head, 0.9f, 0.5f, 0.04f, 3f, 30f); // derin hırıltı: "geliyor"
    }

    [Rpc(SendTo.Everyone)]
    private void AttackRpc(Vector3 at, ulong targetClientId, Vector3 push)
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib != null)
        {
            Sfx.At(lib.rockHit, at + Vector3.up * 0.5f, 1f, 0.6f, 0.05f, 3f, 30f);
            Sfx.At(lib.spearHit, at + Vector3.up, 0.8f, 0.7f, 0.05f, 2f, 20f);
        }
        // vurulan oyuncu kendi ekranında geri savrulur + kamera sarsılır
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || !p.IsOwner || p.OwnerClientId != targetClientId) continue;
            var pc = p.GetComponent<PlayerController>(); if (pc != null) pc.AddKnockback(push);
            var feel = p.GetComponent<PlayerFeel>(); if (feel != null) feel.Kick(new Vector3(-9f, Random.Range(-5f, 5f), Random.Range(-6f, 6f)));
        }
    }

    [Rpc(SendTo.Everyone)]
    private void WhiffRpc()
    {
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        Sfx.At(lib.rockHit, transform.position + transform.forward * 1.5f, 0.8f, 0.55f, 0.05f, 3f, 28f); // yumruk yere çarptı
    }

    /// <summary>Uçurumun dibine çarpma (köprüyle birlikte düştüyse).</summary>
    [Rpc(SendTo.Everyone)]
    private void LandRpc(Vector3 at)
    {
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        Sfx.At(lib.impactBoom, at + Vector3.up, 0.8f, 0.7f, 0.05f, 4f, 50f);
        Sfx.At(lib.spiderAttack, at + Vector3.up * 2f, 0.7f, 0.45f, 0.05f, 4f, 50f);
    }

    [Rpc(SendTo.Everyone)]
    private void HurtRpc()
    {
        SfxLibrary lib = Sfx.Lib; if (lib == null) return;
        Sfx.At(lib.spiderAttack, Head, 0.9f, 1.1f, 0.05f, 3f, 30f);
    }
}
