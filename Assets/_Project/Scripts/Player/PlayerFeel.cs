using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// C-03: Oyuncu hissi (yalnızca görsel, oynanışı değiştirmez).
/// - Yerel oyuncu: yürüme/koşu baş sallanması, iniş çökmesi, mızrak ve darbe kamera tepmesi,
///   darbe (ani zehir artışı) kırmızımsı vinyet nabzı, bayılınca renk solması + kararma.
/// KULLANICI: yürüme/koşu baş sallanması ve adım/iniş tozu kaldırıldı.
/// </summary>
public class PlayerFeel : MonoBehaviour
{
    [Header("İniş")]
    [SerializeField] private float landDipPerSpeed = 0.022f;
    [SerializeField] private float maxLandDip = 0.16f;

    [Header("Parçacıklar")]
    [SerializeField] private Material particleMaterial;
    [SerializeField] private Color creatureHitColor = new Color(0.16f, 0.05f, 0.2f, 0.9f);
    [SerializeField] private Color wallHitColor = new Color(0.5f, 0.48f, 0.44f, 0.6f);

    [Header("Darbe / bayılma ekranı")]
    [SerializeField] private float hurtThreshold = 4f;
    [SerializeField] private Color hurtVignetteColor = new Color(0.35f, 0.02f, 0.03f);

    private NetworkObject netObj;
    private PlayerController controller;
    private PlayerLife life;
    private PoisonSystem poison;

    private Vector3 lastPos;
    private bool wasGrounded = true;
    private float airTime;
    private float lastFallSpeed;
    private float landDip;
    private Vector3 kick;
    private float lastPoison = -1f;

    private ParticleSystem hits;

    private Volume hurtVolume, faintVolume;
    private float hurtPulse, faintBlend;

    private readonly RaycastHit[] rayHits = new RaycastHit[6];

    private bool IsLocal => netObj == null || !netObj.IsSpawned || netObj.IsOwner;

    private void Awake()
    {
        netObj = GetComponent<NetworkObject>();
        controller = GetComponent<PlayerController>();
        life = GetComponent<PlayerLife>();
        poison = GetComponent<PoisonSystem>();
        lastPos = transform.position;
        hits =MakeSystem("FeelHits", 0.35f, 0.7f, 1.2f, 3.2f, 0.03f, 0.09f, 0.8f, 0.05f);
    }

    private void OnDestroy()
    {
        if (hits != null) Destroy(hits.gameObject);
        if (hurtVolume != null) { Destroy(hurtVolume.profile); Destroy(hurtVolume.gameObject); }
        if (faintVolume != null) { Destroy(faintVolume.profile); Destroy(faintVolume.gameObject); }
    }

    // ---------- kurulum ----------

    private ParticleSystem MakeSystem(string name, float lifeMin, float lifeMax, float speedMin, float speedMax,
                                      float sizeMin, float sizeMax, float gravity, float shapeRadius)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.gravityModifier = gravity;
        main.maxParticles = 300;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = shapeRadius;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.5f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = particleMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    private Volume MakeVolume(string name, float priority, out VolumeProfile profile)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave;
        var v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = priority;
        v.weight = 0f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        v.profile = profile;
        return v;
    }

    private void EnsureVolumes()
    {
        if (hurtVolume != null) return;
        hurtVolume = MakeVolume("FeelHurtVolume", 60f, out var hp);
        var hv = hp.Add<Vignette>(true);
        hv.intensity.Override(0.5f); hv.smoothness.Override(0.55f); hv.color.Override(hurtVignetteColor);
        var hc = hp.Add<ColorAdjustments>(true);
        hc.colorFilter.Override(new Color(1f, 0.75f, 0.72f));

        faintVolume = MakeVolume("FeelFaintVolume", 61f, out var fp);
        var fv = fp.Add<Vignette>(true);
        fv.intensity.Override(0.6f); fv.smoothness.Override(0.7f); fv.color.Override(Color.black);
        var fc = fp.Add<ColorAdjustments>(true);
        fc.saturation.Override(-85f); fc.postExposure.Override(-1.1f); fc.contrast.Override(10f);
    }

    // ---------- dış çağrılar ----------

    /// <summary>Kamera tepmesi (derece: x=pitch, y=yaw, z=roll). Yalnızca yerel oyuncuda etkilidir.</summary>
    public void Kick(Vector3 euler)
    {
        if (IsLocal) kick += euler;
    }

    /// <summary>Spear: mızrak bir şeye değdi.</summary>
    public void OnSpearImpact(Vector3 point, Vector3 normal, bool creature)
    {
        Burst(hits, point, normal, creature ? 18 : 10, creature ? creatureHitColor : wallHitColor);
        Kick(creature ? new Vector3(-1.8f, Random.Range(-1f, 1f), Random.Range(-1.5f, 1.5f))
                      : new Vector3(-0.9f, 0f, Random.Range(-0.6f, 0.6f)));
    }

    private static void Burst(ParticleSystem ps, Vector3 point, Vector3 normal, int count, Color color)
    {
        if (ps == null) return;
        ps.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal.sqrMagnitude > 0.01f ? normal : Vector3.up));
        var ep = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
        ps.Emit(ep, count);
    }

    // ---------- döngü ----------

    private bool GroundedHere()
    {
        if (IsLocal && controller != null) return controller.IsGrounded;
        int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 0.25f, Vector3.down, rayHits, 0.45f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!rayHits[i].collider.transform.IsChildOf(transform)) return true;
        return false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool alive = life == null || life.State == PlayerLifeState.Alive;

        if (!IsLocal) return;
        float vertical = (transform.position.y - lastPos.y) / dt;
        lastPos = transform.position;
        bool grounded = GroundedHere();

        // KULLANICI: yürüme/koşu sallanması ve adım/iniş tozu yok; yalnızca iniş çökmesi kalır
        if (!grounded) { airTime += dt; lastFallSpeed = Mathf.Min(lastFallSpeed, vertical); }
        else
        {
            if (!wasGrounded && airTime > 0.2f && alive)
            {
                float fall = -lastFallSpeed;
                if (IsLocal)
                {
                    landDip = Mathf.Min(maxLandDip, fall * landDipPerSpeed);
                    kick += new Vector3(Mathf.Min(3f, fall * 0.35f), 0f, 0f);
                }
            }
            airTime = 0f;
            lastFallSpeed = 0f;
        }
        wasGrounded = grounded;

        UpdateCamera(dt);
        UpdateScreen(dt);
    }

    private void UpdateCamera(float dt)
    {
        landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-6f * dt));
        if (life != null) life.PivotOffset = new Vector3(0f, -landDip, 0f);

        kick = Vector3.Lerp(kick, Vector3.zero, 1f - Mathf.Exp(-9f * dt));
        if (controller != null) controller.FeelEuler = kick;
    }

    private void UpdateScreen(float dt)
    {
        EnsureVolumes();

        // darbe: zehirde ani artış (yaratık saldırısı vb.)
        if (poison != null && poison.IsSpawned)
        {
            float v = poison.Value;
            if (lastPoison >= 0f && v - lastPoison >= hurtThreshold && (life == null || life.CanAct))
            {
                float k = Mathf.Clamp01(0.55f + (v - lastPoison) / 25f);
                hurtPulse = Mathf.Max(hurtPulse, k);
                kick += new Vector3(-2.5f * k, Random.Range(-2f, 2f) * k, Random.Range(-3f, 3f) * k);
            }
            lastPoison = v;
        }
        hurtPulse = Mathf.MoveTowards(hurtPulse, 0f, 1.4f * dt);
        hurtVolume.weight = hurtPulse;

        float faintTarget = 0f;
        if (life != null)
        {
            if (life.State == PlayerLifeState.Fainted) faintTarget = 1f;
            else if (life.State == PlayerLifeState.Dead) faintTarget = 0.55f;
        }
        faintBlend = Mathf.MoveTowards(faintBlend, faintTarget, (faintTarget > faintBlend ? 0.9f : 0.6f) * dt);
        faintVolume.weight = faintBlend;
    }
}
