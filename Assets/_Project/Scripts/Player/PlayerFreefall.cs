using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// B3-01 (KULLANICI 2026-11-14): Bölüm 3 girişindeki serbest düşüş. FreefallSequence etkinken ve oyuncu düşüş
/// sütunundayken çalışır; yere değince kendiliğinden biter ve oyun birinci şahısa döner.
/// - Sahip kopya: WASD ile havada süzülme, üçüncü şahıs kamera (fareyle bakış aynı), rüzgâr sesi, FOV.
/// - Tüm kopyalar: gövde yüzüstü yatar, kollar/bacaklar açılır (kodla poz; animasyon klibi yok), yönlendirince yatar.
/// Konum ağda NetworkTransform ile (sahip yetkili) zaten senkron; ayrıca ağ verisi yok.
/// </summary>
[DefaultExecutionOrder(220)] // Animator, PlayerLife (kamera yüksekliği) ve FlashlightVisual'dan sonra
public class PlayerFreefall : MonoBehaviour
{
    [SerializeField] private Transform cameraPivot;

    [Header("Düşüş")]
    [SerializeField] private float terminalSpeed = 42f;
    [SerializeField] private float fallAcceleration = 24f;
    [SerializeField] private float steerSpeed = 14f;
    [SerializeField] private float steerAcceleration = 18f;
    [Tooltip("Yere bu kadar kala (m) düşüş yavaşlamaya başlar (rüya gibi yumuşak iniş).")]
    [SerializeField] private float slowHeight = 38f;
    [SerializeField] private float landingSpeed = 4.5f;

    [Header("Üçüncü şahıs kamera")]
    [SerializeField] private float cameraDistance = 3.6f;
    [SerializeField] private float cameraHeight = 0.8f;
    [SerializeField] private float fovBoost = 14f;

    [Header("Poz")]
    [SerializeField] private float bodyTilt = 82f;
    [SerializeField] private float rollPerSpeed = 2.2f;
    [SerializeField] private float pitchPerSpeed = 1.3f;
    [Tooltip("Düşüşte model bu EŞİT ölçeğe geçer. Modelin normal ölçeği eşit değil (geniş/kısa); kemikler döndürülünce uzuvlar yamuluyordu.")]
    [SerializeField] private float fallUniformScale = 1.46f;

    [Header("Rüzgar hissi (yalnızca sahip)")]
    [SerializeField] private Material windStreakMaterial;
    [SerializeField] private float windStreakRate = 85f;
    [SerializeField] private float cameraShake = 0.035f;

    /// <summary>Bu oyuncu şu an düşüyor mu (her kopyada).</summary>
    public bool IsFalling { get; private set; }

    private PlayerController controllerLogic;
    private CharacterController controller;
    private NetworkPlayer netPlayer;
    private NetworkObject netObject;
    private PlayerLife life;
    private Camera cam;

    private Transform model;
    private Vector3 modelBasePos, modelBaseScale;
    private ParticleSystem streaks;
    private Quaternion modelBaseRot;
    private Transform armL, foreL, handL, armR, foreR, handR, legL, shinL, footL, legR, shinR, footR, spine, head;

    private Vector3 horizontal;
    private float vertical;
    private Vector3 lastPos, smoothVelocity;
    private float blend;
    private bool ownerActive, posed, landed, bodyShown;
    private FreefallSequence seenSequence;
    private float baseFov = -1f;
    private Renderer[] viewRenderers = new Renderer[0];
    private AudioSource wind;
    private float wobblePhase;

    private bool IsOwnerCopy => netObject == null || !netObject.IsSpawned || netObject.IsOwner;

    private void Awake() { Init(); }

#if UNITY_EDITOR
    /// <summary>Yalnızca editör: pozu Play'e girmeden önizlemek için (V01Builder / MCP).</summary>
    public void EditorPreviewPose(float w)
    {
        if (model == null) Init();
        ApplyPose(w);
    }
#endif

    private void Init()
    {
        controllerLogic = GetComponent<PlayerController>();
        controller = GetComponent<CharacterController>();
        netPlayer = GetComponent<NetworkPlayer>();
        netObject = GetComponent<NetworkObject>();
        life = GetComponent<PlayerLife>();
        if (cameraPivot != null) cam = cameraPivot.GetComponent<Camera>();
        wobblePhase = Random.value * 10f;

        Animator anim = GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            model = anim.transform;
            modelBasePos = model.localPosition;
            modelBaseRot = model.localRotation;
            modelBaseScale = model.localScale;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.EndsWith(":LeftArm")) armL = t; else if (n.EndsWith(":LeftForeArm")) foreL = t; else if (n.EndsWith(":LeftHand")) handL = t;
                else if (n.EndsWith(":RightArm")) armR = t; else if (n.EndsWith(":RightForeArm")) foreR = t; else if (n.EndsWith(":RightHand")) handR = t;
                else if (n.EndsWith(":LeftUpLeg")) legL = t; else if (n.EndsWith(":LeftLeg")) shinL = t; else if (n.EndsWith(":LeftFoot")) footL = t;
                else if (n.EndsWith(":RightUpLeg")) legR = t; else if (n.EndsWith(":RightLeg")) shinR = t; else if (n.EndsWith(":RightFoot")) footR = t;
                else if (n.EndsWith(":Spine1")) spine = t; else if (n.EndsWith(":Head")) head = t;
            }
        }
        lastPos = transform.position;
    }

    private void OnDisable()
    {
        if (ownerActive) EndOwner();
        RestoreModel();
    }

    private void OnDestroy()
    {
        if (streaks != null) Destroy(streaks.gameObject);
    }

    private void Update()
    {
        // yeni bir düşüş sekansı geldiyse "indi" mandalı sıfırlanır
        if (seenSequence != FreefallSequence.Instance) { seenSequence = FreefallSequence.Instance; landed = false; }

        bool alive = life == null || life.State == PlayerLifeState.Alive;
        bool inAir = FreefallSequence.Active && FreefallSequence.InColumn(transform.position)
                     && transform.position.y > FreefallSequence.GroundY + 0.45f;
        if (FreefallSequence.Active && FreefallSequence.InColumn(transform.position) && !inAir) landed = true;
        bool should = alive && inAir && !landed;

        if (IsOwnerCopy)
        {
            if (should && !ownerActive) BeginOwner();
            if (ownerActive)
            {
                if (!should) EndOwner();
                else StepOwner();
            }
            IsFalling = ownerActive;
        }
        else IsFalling = should;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 v = (transform.position - lastPos) / dt;
        if (v.sqrMagnitude > 200f * 200f) v = Vector3.zero;          // ışınlanma karesi
        smoothVelocity = Vector3.Lerp(smoothVelocity, v, 1f - Mathf.Exp(-8f * dt));
        lastPos = transform.position;
        UpdateWind();
    }

    private void BeginOwner()
    {
        ownerActive = true;
        horizontal = Vector3.zero;
        vertical = -3f;
        if (controllerLogic != null) controllerLogic.ExternalMotion = true;
        if (netPlayer != null) { netPlayer.SetOwnerBodyVisible(true); bodyShown = true; }
        if (cam != null && baseFov < 0f) baseFov = cam.fieldOfView;
        // birinci şahıs görselleri (eldeki fener, mızrak, huzme) üçüncü şahısta kameranın önünde asılı kalmasın
        if (cameraPivot != null)
        {
            viewRenderers = cameraPivot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in viewRenderers) if (r != null) r.forceRenderingOff = true;
        }
    }

    private void EndOwner()
    {
        ownerActive = false;
        if (controllerLogic != null) controllerLogic.ExternalMotion = false;
        foreach (Renderer r in viewRenderers) if (r != null) r.forceRenderingOff = false;
        viewRenderers = new Renderer[0];
        // gövde, poz tamamen çözülünce gizlenir (LateUpdate)
    }

    private void StepOwner()
    {
        float dt = Time.deltaTime;
        Vector2 input = ReadMove();
        Vector3 wish = (transform.right * input.x + transform.forward * input.y) * steerSpeed;

        // sütunun dışına süzülmesin: kenara yaklaşınca merkeze doğru itilir
        Vector3 fromAxis = transform.position - FreefallSequence.Axis;
        fromAxis.y = 0f;
        float edge = FreefallSequence.SteerRadius;
        if (fromAxis.magnitude > edge) wish -= fromAxis.normalized * steerSpeed * Mathf.Clamp01((fromAxis.magnitude - edge) / 10f) * 2f;

        horizontal = Vector3.MoveTowards(horizontal, wish, steerAcceleration * dt);

        float height = transform.position.y - FreefallSequence.GroundY;
        float targetSpeed = height < slowHeight ? Mathf.Lerp(landingSpeed, terminalSpeed, Mathf.Clamp01(height / slowHeight)) : terminalSpeed;
        float rate = -vertical > targetSpeed ? 70f : fallAcceleration;     // yere yaklaşırken hızlı frenler
        vertical = Mathf.MoveTowards(vertical, -targetSpeed, rate * dt);

        if (controller != null && controller.enabled)
        {
            controller.Move((horizontal + Vector3.up * vertical) * dt);
            if (controller.isGrounded) landed = true;
        }
        else transform.position += (horizontal + Vector3.up * vertical) * dt;
    }

    private static Vector2 ReadMove()
    {
        if (PlayerController.InputLocked || PauseMenu.IsOpen || NoteReader.IsOpen) return Vector2.zero;
        Keyboard kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
    }

    private void UpdateWind()
    {
        if (!IsOwnerCopy) return;
        float target = ownerActive ? Mathf.Clamp01(-vertical / terminalSpeed) * 0.55f : 0f;
        if (wind == null)
        {
            if (target <= 0f || Sfx.Lib == null || Sfx.Lib.fallWind == null) return;
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = Sfx.Lib.fallWind; wind.loop = true; wind.spatialBlend = 0f; wind.volume = 0f; wind.playOnAwake = false;
            wind.Play();
        }
        wind.volume = Mathf.MoveTowards(wind.volume, target, Time.deltaTime * 0.8f);
        wind.pitch = 0.8f + 0.5f * Mathf.Clamp01(-vertical / terminalSpeed);
        if (!ownerActive && wind.volume <= 0.001f && wind.isPlaying) wind.Stop();
        else if (ownerActive && !wind.isPlaying) wind.Play();
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        blend = Mathf.MoveTowards(blend, IsFalling ? 1f : 0f, dt * (IsFalling ? 2.5f : 1.8f));
        bool owner = IsOwnerCopy;

        if (owner && cam != null && baseFov > 0f)
        {
            float speed01 = ownerActive ? Mathf.Clamp01(-vertical / terminalSpeed) : 0f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + fovBoost * speed01 * blend, 1f - Mathf.Exp(-4f * dt));
        }

        if (owner) UpdateStreaks();

        if (blend <= 0.001f)
        {
            if (posed) RestoreModel();
            if (bodyShown && owner && netPlayer != null) { netPlayer.SetOwnerBodyVisible(false); bodyShown = false; }
            return;
        }

        float w = Mathf.SmoothStep(0f, 1f, blend);
        ApplyPose(w);

        // üçüncü şahıs: kamera, bakış yönünün gerisine ve biraz yukarısına çekilir (PlayerLife yüksekliği her kare sıfırlar)
        if (owner && cameraPivot != null)
        {
            Vector3 offset = cameraPivot.localRotation * new Vector3(0f, cameraHeight, -cameraDistance) * w;
            cameraPivot.localPosition += offset;
            // rüzgar sarsıntısı: hızla artan küçük titreme
            float shake = cameraShake * w * (ownerActive ? Mathf.Clamp01(-vertical / terminalSpeed) : 0f);
            float st = Time.time * 13f;
            cameraPivot.localPosition += cameraPivot.localRotation * new Vector3(Mathf.PerlinNoise(st, 1.7f) - 0.5f, Mathf.PerlinNoise(4.3f, st) - 0.5f, 0f) * (shake * 2f);
            float floor = FreefallSequence.GroundY + 0.35f;
            if (FreefallSequence.Instance != null && cameraPivot.position.y < floor)
                cameraPivot.position = new Vector3(cameraPivot.position.x, floor, cameraPivot.position.z);
        }
    }

    private void RestoreModel()
    {
        posed = false;
        if (model == null) return;
        model.localPosition = modelBasePos;
        model.localRotation = modelBaseRot;
        model.localScale = modelBaseScale;
    }

    /// <summary>Sahibin çevresinden yukarı akan rüzgar çizgileri (dünya uzayı; oyuncu içlerinden düşer).</summary>
    private void UpdateStreaks()
    {
        float speed01 = ownerActive ? Mathf.Clamp01(-vertical / terminalSpeed) : 0f;
        if (streaks == null)
        {
            if (speed01 <= 0f) return;
            var go = new GameObject("FallWindStreaks");
            streaks = go.AddComponent<ParticleSystem>();
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = streaks.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.88f, 1f, 0.22f), new Color(1f, 1f, 1f, 0.5f));
            main.maxParticles = 400;
            var shape = streaks.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 9f; shape.radiusThickness = 0.8f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var vel = streaks.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(10f, 18f);
            var col = streaks.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.16f; rend.lengthScale = 2f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            if (windStreakMaterial != null) rend.sharedMaterial = windStreakMaterial;
            streaks.Play();
        }
        // çizgiler oyuncunun altında doğar, ~0.3 sn sonra yanından geçer
        streaks.transform.position = transform.position + (Vector3.up * vertical + horizontal) * 0.32f;
        var em = streaks.emission;
        em.rateOverTime = windStreakRate * speed01 * speed01;
    }

    private void ApplyPose(float w)
    {
        if (model == null) return;
        posed = true;

        // gövde yüzüstü yatar; yönlendirme hızına göre yana ve öne yatar
        Vector3 lv = transform.InverseTransformDirection(smoothVelocity);
        float roll = Mathf.Clamp(-lv.x * rollPerSpeed, -32f, 32f);
        float lean = Mathf.Clamp(lv.z * pitchPerSpeed, -18f, 18f);
        float t = Time.time + wobblePhase;
        float sway = Mathf.Sin(t * 1.7f) * 2.5f;
        Quaternion target = Quaternion.AngleAxis(roll + sway, Vector3.forward) * Quaternion.Euler(bodyTilt + lean, 0f, 0f) * modelBaseRot;
        model.localRotation = Quaternion.Slerp(modelBaseRot, target, w);
        model.localPosition = modelBasePos + new Vector3(0f, 0.9f, -0.85f) * w;
        // eşit ölçek: kemik dönüşleri uzuvları yamultmasın
        model.localScale = Vector3.Lerp(modelBaseScale, Vector3.one * fallUniformScale, w);

        // sırt hafif kavisli, baş ileri bakar
        if (spine != null) spine.rotation = Quaternion.AngleAxis(-10f * w, model.right) * spine.rotation;
        if (head != null) head.rotation = Quaternion.AngleAxis(-28f * w, model.right) * head.rotation;

        // uzuvlar: model uzayında hedef yönler (model +Z göğüs, +Y baş, +X sağ); kemik eksenlerinden bağımsız
        float f1 = Mathf.Sin(t * 5.3f) * 0.07f, f2 = Mathf.Sin(t * 4.1f + 1.3f) * 0.07f;
        // KULLANICI (2026-11-14): oranlar garip duruyordu → kollar daha açık/düz, bacaklar neredeyse düz (dizden kameraya kıvrılmıyor)
        Aim(armL, foreL, new Vector3(-0.95f, 0.22f + f1, -0.12f), w);
        Aim(foreL, handL, new Vector3(-0.72f, 0.62f, -0.22f + f2), w);
        Aim(armR, foreR, new Vector3(0.95f, 0.22f + f2, -0.12f), w);
        Aim(foreR, handR, new Vector3(0.72f, 0.62f, -0.22f + f1), w);
        Aim(legL, shinL, new Vector3(-0.4f, -0.9f, -0.1f + f2), w);
        Aim(shinL, footL, new Vector3(-0.3f, -0.9f, -0.3f + f1), w);
        Aim(legR, shinR, new Vector3(0.4f, -0.9f, -0.1f + f1), w);
        Aim(shinR, footR, new Vector3(0.3f, -0.9f, -0.3f + f2), w);
    }

    /// <summary>Kemiği, çocuk kemiğine uzanan yönü hedef yöne gelecek şekilde döndürür.</summary>
    private void Aim(Transform bone, Transform child, Vector3 modelDir, float w)
    {
        if (bone == null || child == null) return;
        Vector3 current = child.position - bone.position;
        if (current.sqrMagnitude < 1e-8f) return;
        Vector3 target = model.TransformDirection(modelDir.normalized);
        Quaternion delta = Quaternion.FromToRotation(current.normalized, target);
        bone.rotation = Quaternion.Slerp(bone.rotation, delta * bone.rotation, w);
    }
}
