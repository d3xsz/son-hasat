using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-26: Fiziksel yaratık (GDD 8.1, 3. kademe; şimdilik örümcek placeholder). Yapay zeka SUNUCUDA çalışır, konum NetworkTransform (sunucu yetkili) ile herkese gider.
/// En yakın hayattaki oyuncuyu sürünerek yaklaşır, yakınında kovalar; yetişince HASAR YERİNE ZEHİR ekler (PoisonSystem.AddPoison).
/// Mızrakla itilir (ISpearTarget): geri savrulur ve kısa süre sersemler. Fener ışığına yakalanınca bir süre kaçar. Güvenli alanlara (totem) girmez.
/// Fener ısısını artırmaz. Oyuncu hızlarına göre kaçılabilir olacak şekilde ayarlıdır (yürüme 4, koşu 6.5 m/sn).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Creature : NetworkBehaviour, ISpearTarget
{
    private enum State { Stalk, Stunned, Flee }

    [Header("Hareket (m/sn)")]
    [SerializeField] private float stalkSpeed = 2.2f;
    [SerializeField] private float chaseSpeed = 4.8f;
    [SerializeField] private float fleeSpeed = 5.5f;
    [SerializeField] private float turnSpeed = 8f;
    [SerializeField] private float gravity = -20f;

    [Header("Algı")]
    [SerializeField] private float aggroRange = 30f;
    [Tooltip("Hedefe bu mesafeden yakınsa hızlı kovalar.")]
    [SerializeField] private float chaseRange = 14f;

    [Header("Saldırı (zehir ekler)")]
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private float attackCooldown = 1.6f;
    [SerializeField] private float attackPoison = 10f;

    [Header("Mızrak")]
    [Tooltip("KULLANICI KARARI: açık. Mızrak yaratığı tek vuruşta öldürür (kapalıysa sadece geri iter ve sersemletir).")]
    [SerializeField] private bool spearKills = true;
    [SerializeField] private float pushSpeed = 8f;
    [SerializeField] private float pushDecay = 16f;
    [SerializeField] private float stunSeconds = 1.2f;
    [Tooltip("Sersemlemeden sonra saldırmadan önceki bekleme (sn).")]
    [SerializeField] private float postStunGrace = 1f;
    [Tooltip("Ölünce yerde kalma süresi (sn); SpiderAnimator bu sürede devrilme + gömülme oynatır.")]
    [SerializeField] private float deathDespawnDelay = 4.5f;

    private readonly NetworkVariable<bool> dead = new NetworkVariable<bool>(false);
    private float despawnAt;
    private SpiderAnimator spiderAnimator;
    public bool IsDead => dead.Value;

    [Header("Işıktan kaçış (GDD: denge testiyle karar verilecek)")]
    [Tooltip("KULLANICI KARARI: kapalı (örümcek fenerden kaçmasın, hep üstüne gelsin).")]
    [SerializeField] private bool fleesFromLight = false;
    [SerializeField] private float lightConeAngle = 28f;
    [SerializeField] private float lightRange = 14f;
    [SerializeField] private float lightAccumSeconds = 0.35f;
    [SerializeField] private float fleeSeconds = 3f;
    [SerializeField] private float lightCooldown = 4f;

    private CharacterController controller;
    private State state = State.Stalk;
    private PlayerLife target;
    private Vector3 knock;
    private Vector3 faceDirection = Vector3.forward;
    private Vector3 fleeFrom;
    private float verticalVelocity;
    private float stunUntil;
    private float nextAttack;
    private float fleeUntil;
    private float nextFleeAllowed;
    private float litTimer;
    private float nextTargetCheck;
    private float circleSide = 1f;
    private Vector3 lastStuckPos;
    private float nextStuckCheck;
    private float detourUntil;
    private float detourSide = 1f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        spiderAnimator = GetComponent<SpiderAnimator>();
        circleSide = Random.value < 0.5f ? -1f : 1f;
    }

    public override void OnNetworkSpawn()
    {
        // Fizik/hareket yalnızca sunucuda; istemciler NetworkTransform'u izler
        controller.enabled = IsServer;
        lastStuckPos = transform.position;
        dead.OnValueChanged += OnDeadChanged;
        if (dead.Value) OnDeadChanged(false, true);
    }

    public override void OnNetworkDespawn()
    {
        dead.OnValueChanged -= OnDeadChanged;
    }

    private void OnDeadChanged(bool oldValue, bool isDead)
    {
        if (!isDead) return;
        // mızrak artık değmesin
        foreach (Collider c in GetComponentsInChildren<Collider>()) if (!(c is CharacterController)) c.enabled = false;
        if (spiderAnimator != null) spiderAnimator.PlayDeath(deathDespawnDelay);
        if (!oldValue && Sfx.Lib != null) // KULLANICI (cila): ölüm çatırtısı + kısa çığlık
        {
            Sfx.At(Sfx.Lib.spiderDeath, transform.position + Vector3.up * 0.3f, 0.8f, 0.9f, 0.06f, 2f, 22f);
            Sfx.At(Sfx.Lib.spiderAttack, transform.position + Vector3.up * 0.3f, 0.45f, 1.9f, 0.05f, 2f, 22f);
        }
    }

    // ---------- Mızrak ----------

    public void OnSpearHit(ulong attackerClientId, Vector3 point, Vector3 direction)
    {
        if (!IsServer) return;
        if (dead.Value) return;
        Vector3 d = direction;
        if (spearKills)
        {
            // KULLANICI: anında yok olmasın — devrilir, kıpırdar, gömülür, sonra kaldırılır
            d.y = 0f;
            knock = (d.sqrMagnitude > 0.01f ? d.normalized : -transform.forward) * pushSpeed * 0.5f;
            dead.Value = true;
            despawnAt = Time.time + deathDespawnDelay;
            return;
        }
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) d = -transform.forward;
        d.Normalize();
        knock = d * pushSpeed;
        state = State.Stunned;
        stunUntil = Time.time + stunSeconds;
    }

    // ---------- Yapay zeka (sunucu) ----------

    private void Update()
    {
        if (!IsServer || !IsSpawned || !controller.enabled) return;
        float dt = Time.deltaTime;

        if (dead.Value)
        {
            // ölü: yalnızca savrulmanın sönümü + yerçekimi; süre dolunca kaldır
            knock = Vector3.MoveTowards(knock, Vector3.zero, pushDecay * dt);
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            else verticalVelocity += gravity * dt;
            controller.Move(new Vector3(knock.x, verticalVelocity, knock.z) * dt);
            if (Time.time >= despawnAt) NetworkObject.Despawn(true);
            return;
        }

        UpdateTarget();
        UpdateLightFear(dt);

        Vector3 desired = Vector3.zero;
        switch (state)
        {
            case State.Stunned:
                if (Time.time >= stunUntil)
                {
                    state = State.Stalk;
                    nextAttack = Time.time + postStunGrace;
                }
                break;
            case State.Flee:
                if (Time.time >= fleeUntil) state = State.Stalk;
                else desired = FleeVelocity();
                break;
            default:
                desired = StalkVelocity();
                TryAttack();
                break;
        }

        knock = Vector3.MoveTowards(knock, Vector3.zero, pushDecay * dt);
        Vector3 move = desired + knock;

        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        else verticalVelocity += gravity * dt;
        move.y = verticalVelocity;
        controller.Move(move * dt);

        if (faceDirection.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDirection, Vector3.up), turnSpeed * dt);
    }

    private void UpdateTarget()
    {
        if (Time.time < nextTargetCheck) return;
        nextTargetCheck = Time.time + 0.4f;

        PlayerLife best = null;
        float bestDist = aggroRange;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || !p.CanAct) continue;
            float d = Vector3.Distance(p.transform.position, transform.position);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        target = best;
    }

    private Vector3 StalkVelocity()
    {
        if (target == null) return Vector3.zero;
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        if (dist < 0.01f) return Vector3.zero;

        Vector3 dir = toTarget / dist;
        faceDirection = dir;
        if (dist <= attackRange * 0.8f) return Vector3.zero;

        float speed = dist < chaseRange ? chaseSpeed : stalkSpeed;

        // Güvenli alanlara (totem) girmez: sınırında yan yan dolaşır
        Vector3 ahead = transform.position + dir * (speed * 0.3f);
        if (SafeZones.IsInsideProtected(ahead))
        {
            dir = Vector3.Cross(Vector3.up, dir) * circleSide;
            speed = stalkSpeed;
        }

        // Engele takılırsa kısa süre yana sapar
        if (Time.time >= nextStuckCheck)
        {
            nextStuckCheck = Time.time + 0.6f;
            if (dist > attackRange && (transform.position - lastStuckPos).sqrMagnitude < 0.06f)
            {
                detourUntil = Time.time + 0.8f;
                detourSide = Random.value < 0.5f ? -1f : 1f;
            }
            lastStuckPos = transform.position;
        }
        if (Time.time < detourUntil) dir = Quaternion.Euler(0f, detourSide * 40f, 0f) * dir; // takılınca kısa yana kayma (geri dönüyormuş gibi görünmesin)

        return dir * speed;
    }

    private void TryAttack()
    {
        if (target == null || Time.time < nextAttack) return;
        Vector3 d = target.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > attackRange) return;

        PoisonSystem poison = target.GetComponent<PoisonSystem>();
        if (poison != null) poison.AddPoison(attackPoison); // güvenli alandaki oyuncuda otomatik yok sayılır
        nextAttack = Time.time + attackCooldown;
        AttackSoundRpc();
    }

    /// <summary>KULLANICI (cila): saldırı sesi herkeste (tiz, tıslamaya benzer çığlık).</summary>
    [Rpc(SendTo.Everyone)]
    private void AttackSoundRpc()
    {
        if (Sfx.Lib != null) Sfx.At(Sfx.Lib.spiderAttack, transform.position + Vector3.up * 0.4f, 0.7f, 1.55f, 0.1f, 2f, 25f);
        if (spiderAnimator != null) spiderAnimator.PlayLunge(); // M-04: saldırıda ileri atılma
    }

    private Vector3 FleeVelocity()
    {
        Vector3 away = transform.position - fleeFrom;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();
        faceDirection = away;
        return away * fleeSpeed;
    }

    private void UpdateLightFear(float dt)
    {
        if (!fleesFromLight) return;
        bool lit = IsLitByAnyPlayer(out Vector3 from);
        litTimer = lit ? litTimer + dt : Mathf.Max(0f, litTimer - dt);

        if (state != State.Flee && state != State.Stunned && litTimer >= lightAccumSeconds && Time.time >= nextFleeAllowed)
        {
            state = State.Flee;
            fleeFrom = from;
            fleeUntil = Time.time + fleeSeconds;
            nextFleeAllowed = fleeUntil + lightCooldown;
            litTimer = 0f;
        }
    }

    private bool IsLitByAnyPlayer(out Vector3 lightSource)
    {
        lightSource = Vector3.zero;
        Vector3 center = transform.position + Vector3.up * 0.4f;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || !p.CanAct || p.CameraPivot == null) continue;
            FlashlightNetworkSync fl = p.GetComponent<FlashlightNetworkSync>();
            if (fl == null || !fl.IsOn) continue;

            Transform pivot = p.CameraPivot;
            Vector3 to = center - pivot.position;
            if (to.magnitude > lightRange) continue;
            if (Vector3.Angle(pivot.forward, to) > lightConeAngle) continue;
            if (Physics.Linecast(pivot.position, center, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(p.transform)) continue;

            lightSource = p.transform.position;
            return true;
        }
        return false;
    }
}
