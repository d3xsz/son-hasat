using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum CollapsePhase : byte { Idle = 0, Shaking = 1, Sliding = 2, Finished = 3, Arrived = 4 }

/// <summary>
/// G-29: Mağara çöküşü ve düşüş sekansı (GDD 6.7, 5.6).
/// Sunucu evreyi yönetir (Idle → Shaking → Sliding(=düşüş) → Finished).
/// KULLANICI (cila): kıvrımlı tünelde kayma kaldırıldı; oyuncu kuyudan DİMDİK, derine düşer. Düşüş KİNEMATİK: her sahip
/// kendi oyuncusunu yerçekimiyle hızlanarak aşağı indirir (son hız sınırlı), yalnızca kuyu içinde hafifçe sağa-sola yön verir;
/// NetworkTransform (sahip yetkili) konumu dağıtır. Düşerken: rüzgar uğultusu (hızla yükselir), yükselen gerilim uğultusu,
/// hızlanan kalp atışı, kamera sarsıntısı + görüş açısı genişler, yukarı akan toz çizgileri. Dibe çarpınca gümbürtü ve sarsıntı;
/// herkes dibe inince ekran kararır (Finished). Uzak oyuncuların gövdesi yalnızca görsel olarak savrulur.
/// </summary>
[DefaultExecutionOrder(100)]
public class CollapseSequence : NetworkBehaviour
{
    [Header("Bağlantılar")]
    [SerializeField] private InputActionAsset inputActions;
    [Tooltip("Kuyunun ekseni (XZ merkezi). Düşüş bu dikey eksen boyunca olur.")]
    [SerializeField] private Transform shaftAxis;
    [Tooltip("Çöküşte taşların düştüğü alanın merkezi (mağara ağzı).")]
    [SerializeField] private Transform rockArea;
    [SerializeField] private Material rockMaterial;
    [Tooltip("Düşerken yukarı akan toz çizgilerinin materyali (yumuşak parçacık).")]
    [SerializeField] private Material streakMaterial;

    [Header("Zamanlama")]
    [SerializeField] private float shakeDuration = 4f;
    [Tooltip("İlk oyuncu dibe indikten sonra en çok bu kadar beklenir (sn).")]
    [SerializeField] private float slideTimeout = 60f;
    [Tooltip("Bu yüksekliğin altı 'mağaraya düşmüş' sayılır.")]
    [SerializeField] private float undergroundY = -8f;
    [Tooltip("Mağara ağzını kapatan duvar; Öfke 100'de çöker (kaybolur), arkasındaki uçurum açılır.")]
    [SerializeField] private GameObject caveSeal;

    [Header("Düşüş (KULLANICI: dimdik)")]
    [Tooltip("Kinematik düşüşün başladığı yükseklik (kuyunun içinde).")]
    [SerializeField] private float fallStartY = -5f;
    [Tooltip("Kuyunun dibi: buraya varan oyuncu 'indi' sayılır.")]
    [SerializeField] private float bottomY = -120f;
    [SerializeField] private float fallGravity = 20f;
    [SerializeField] private float terminalSpeed = 30f;
    [SerializeField] private float lateralSpeed = 3f;
    [SerializeField] private float shaftHalfX = 2.9f;
    [SerializeField] private float shaftHalfZ = 3.1f;
    [Tooltip("Herkes indikten sonra kararmadan önce beklenen süre (çarpmanın hissedilmesi için).")]
    [SerializeField] private float landHoldSeconds = 1.4f;
    [SerializeField] private float fovBoost = 18f;

    [Header("Bitiş")]
    [SerializeField] private string chapterTitle = "BÖLÜM 2: Derinlikler";
    [SerializeField] private float fadeSeconds = 1.5f;

    public static CollapseSequence Instance { get; private set; }
    public static bool InProgress => Instance != null && Instance.Phase != CollapsePhase.Idle;
    /// <summary>Yalnızca sarsıntı/düşüş anı (bittikten sonra false). InProgress ise Bölüm 2'de de true kalır (Bölüm 1 tehditleri doğmasın).</summary>
    public static bool IsFalling => Instance != null && (Instance.Phase == CollapsePhase.Shaking || Instance.Phase == CollapsePhase.Sliding);
    /// <summary>Yerel oyuncu için HUD durum metni (boşsa gösterilmez).</summary>
    public static string LocalStatus { get; private set; } = "";

    /// <summary>Tüm kopyalarda: sekans bitti (ekran karardı). G-30 ChapterLoader buna bağlanacak.</summary>
    public event Action SequenceFinished;

    private readonly NetworkVariable<CollapsePhase> phase = new NetworkVariable<CollapsePhase>(CollapsePhase.Idle);
    // KULLANICI (2026-11-15): bölüm seçiminde Bölüm 3'e giderken Bölüm 2 başlığı görünmesin, ekran Bölüm 3'e kadar siyah kalsın
    private readonly NetworkVariable<bool> silentSkip = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<double> phaseStart = new NetworkVariable<double>(0);

    public CollapsePhase Phase => phase.Value;
    private float PhaseTime => IsSpawned ? (float)(NetworkManager.ServerTime.Time - phaseStart.Value) : 0f;

    // yerel düşüş durumu (yalnızca sahip oyuncu) — "sliding" adı eski evre adıyla uyum için korundu
    private bool sliding;
    private bool landed;
    private float landTime = -1f;
    private float firstArrivalTime = -1f;
    private float allLandedTime = -1f;
    private float fallSpeed;
    private InputAction moveAction;
    private PlayerController localController;
    private bool shakeApplied;

    // düşüş efektleri (yerel)
    private Camera fallCamera;
    private float baseFov = -1f;
    private ParticleSystem streaks;
    private AudioSource windSource, droneSource;
    private float nextHeartbeat;

    // görsel
    private readonly List<GameObject> rocks = new List<GameObject>();
    private float nextRock;
    private Image fadeImage;
    private Text titleText;
    private float fade;

    private void Awake()
    {
        if (inputActions != null)
        {
            InputActionMap map = inputActions.FindActionMap("Player", true);
            moveAction = map.FindAction("Move", true);
        }
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        phase.OnValueChanged += OnPhaseChanged;
        OnPhaseChanged(phase.Value, phase.Value); // aynı değer = anlık uygula (geç katılan)
    }

    public override void OnNetworkDespawn()
    {
        phase.OnValueChanged -= OnPhaseChanged;
        ResetStatics();
        if (Instance == this) Instance = null;
    }

    public override void OnDestroy()
    {
        ResetStatics();
        base.OnDestroy();
    }

    private static void ResetStatics()
    {
        WeatherController.ForceCalm = false;
        PortableSafeZone.LightsOut = false;
        LocalStatus = "";
    }

    // ---------------- Sunucu ----------------

    public void ServerBegin()
    {
        if (!IsServer || phase.Value != CollapsePhase.Idle) return;
        foreach (Totem t in Totem.All) if (t != null) t.ServerSetActive(false);
        SetPhase(CollapsePhase.Shaking);
    }

    /// <summary>G-30: ChapterLoader yeni bölümü yükleyip oyuncuları taşıyınca çağırır; kayma kontrolü bırakılır, ekran açılır.</summary>
    public void ServerMarkArrived()
    {
        if (!IsServer || phase.Value != CollapsePhase.Finished) return;
        SetPhase(CollapsePhase.Arrived);
    }

    /// <summary>TEST KISAYOLU (yalnızca Editor / Development Build): host F9 → Bölüm 1 atlanır, ekran kararır ve
    /// Bölüm 2 normal geçiş yoluyla (ChapterLoader) yüklenir. Sürüm build'inde derlenmez.</summary>
    private void DebugSkipToChapter2()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsServer || phase.Value != CollapsePhase.Idle) return;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || !kb.f9Key.wasPressedThisFrame) return;
        Debug.Log("[TEST] F9: Bölüm 1 atlandı → Bölüm 2 yükleniyor");
        ServerSkipToChapter2();
#endif
    }

    /// <summary>Sunucu: Bölüm 1'i bitirir (ekran kararır, Bölüm 2 normal geçiş yoluyla yüklenir). F9 ve lobideki bölüm seçimi kullanır (build'de de).</summary>
    public bool ServerSkipToChapter2(bool silent = false)
    {
        if (!IsServer || phase.Value != CollapsePhase.Idle) return false;
        silentSkip.Value = silent;
        foreach (Totem t in Totem.All) if (t != null) t.ServerSetActive(false);
        SetPhase(CollapsePhase.Finished);
        return true;
    }

    private void SetPhase(CollapsePhase p)
    {
        phaseStart.Value = NetworkManager.ServerTime.Time;
        phase.Value = p;
    }

    private void ServerUpdate()
    {
        if (phase.Value == CollapsePhase.Idle) return;

        // mağaraya düşenler (kuyu/tünel) zehirlenmez; yukarıda kalanlar için daralan alan geçerli
        foreach (PlayerLife p in PlayerLife.All)
            if (p != null && p.transform.position.y < undergroundY)
            {
                PoisonSystem ps = p.GetComponent<PoisonSystem>();
                if (ps != null) ps.ServerProtect(0.5f);
            }

        if (phase.Value == CollapsePhase.Shaking && PhaseTime >= shakeDuration)
            SetPhase(CollapsePhase.Sliding);
        else if (phase.Value == CollapsePhase.Sliding)
        {
            bool allDone = true, anyArrived = false;
            foreach (PlayerLife p in PlayerLife.All)
            {
                if (p == null || p.State == PlayerLifeState.Dead) continue;
                bool arrived = p.transform.position.y <= bottomY + 1.5f;
                if (arrived) anyArrived = true; else allDone = false;
            }
            if (anyArrived && firstArrivalTime < 0f) firstArrivalTime = Time.time;
            if (allDone && allLandedTime < 0f) allLandedTime = Time.time;
            // herkes indi (kısa bekleme: çarpma hissedilsin) ya da ilk inişten sonra uzun süre geçti (yukarıda kalan bayılmış/ölmüş olabilir)
            if ((allLandedTime >= 0f && Time.time - allLandedTime >= landHoldSeconds) ||
                (firstArrivalTime >= 0f && Time.time - firstArrivalTime >= slideTimeout))
                SetPhase(CollapsePhase.Finished);
        }
    }

    // ---------------- Tüm kopyalar ----------------

    private void OnPhaseChanged(CollapsePhase oldValue, CollapsePhase newValue)
    {
        if (newValue != CollapsePhase.Idle) PortableSafeZone.LightsOut = true;
        if (caveSeal != null)
        {
            // KULLANICI: ağız taş yığınıyla kapalı; çöküş başlayınca taşlar kuyuya yuvarlanır
            CaveRubble rubble = caveSeal.GetComponent<CaveRubble>();
            if (rubble != null) rubble.SetOpen(newValue != CollapsePhase.Idle, oldValue == newValue || oldValue != CollapsePhase.Idle);
            else caveSeal.SetActive(newValue == CollapsePhase.Idle);
        }
        if (newValue == CollapsePhase.Finished)
        {
            EnsureOverlay();
            SequenceFinished?.Invoke();
        }
        if (newValue == CollapsePhase.Arrived) ReleaseLocalPlayer();
    }

    /// <summary>G-30: kayma modundan çık (normal yürüme, CharacterController açık, sarsıntı sıfır).</summary>
    private void ReleaseLocalPlayer()
    {
        sliding = false;
        landed = false;
        StopFallEffects(true);
        PlayerLife life = GetLocalLife();
        if (life == null) return;
        PlayerController pc = life.GetComponent<PlayerController>();
        if (pc != null) { pc.ExternalMotion = false; pc.ShakeEuler = Vector3.zero; }
        CharacterController cc = life.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;
        EnsureOverlay();
        fade = 1f;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) { DebugSkipToChapter2(); ServerUpdate(); }

        PlayerLife localLife = GetLocalLife();
        if (localController == null && localLife != null) localController = localLife.GetComponent<PlayerController>();

        // ani sessizlik: aktif mağaranın yanına gelince ya da olay başlayınca
        CaveTrigger trig = CaveTrigger.Instance;
        bool nearArmedCave = trig != null && trig.Armed && localLife != null &&
                             Vector3.Distance(localLife.transform.position, trig.transform.position) < 16f;
        // KULLANICI: son totemden sonra ormanda yağmur durmaz (taş yağmuru); sessizlik yalnızca mağara ağzında, kayarken ve sonrasında
        bool afterFall = phase.Value == CollapsePhase.Finished || phase.Value == CollapsePhase.Arrived;
        // KULLANICI: taş yağmuru sürerken mağara ağzında da yağmur kesilmez
        if (RockRain.Active) nearArmedCave = false;
        WeatherController.ForceCalm = nearArmedCave || sliding || afterFall;

        // kuyuya düşen yerel oyuncu dimdik düşüşe geçer
        if ((phase.Value == CollapsePhase.Shaking || phase.Value == CollapsePhase.Sliding) && !sliding)
            TryBeginLocalSlide(localLife);
        if (phase.Value == CollapsePhase.Finished) StopFallEffects(false);

        switch (phase.Value)
        {
            case CollapsePhase.Shaking: UpdateRocks(); if (sliding) UpdateSlide(localLife); break;
            case CollapsePhase.Sliding: if (sliding) UpdateSlide(localLife); break;
            case CollapsePhase.Finished: UpdateFinished(); break;
            case CollapsePhase.Arrived: UpdateArrived(); break;
        }
        UpdateShake();
        UpdateStatus(localLife, trig);
    }

    private void LateUpdate()
    {
        if (!IsSpawned || (phase.Value != CollapsePhase.Sliding && phase.Value != CollapsePhase.Shaking)) return;
        // uzak oyuncular: düşerken savrulan gövde (yalnızca görsel, PlayerLife pozunun üstüne yazar)
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.IsOwner || p.State == PlayerLifeState.Dead || p.BodyVisual == null) continue;
            if (!InShaft(p.transform.position) || p.transform.position.y <= bottomY + 1.5f) continue;
            float w = Time.time * 6f + p.OwnerClientId * 1.7f;
            p.BodyVisual.localRotation = Quaternion.Euler(-25f + Mathf.Sin(w) * 20f, Mathf.Sin(w * 0.7f) * 40f, Mathf.Sin(w * 1.3f) * 30f);
            p.BodyVisual.localPosition = new Vector3(0f, 0.6f, 0f);
        }
    }

    private static PlayerLife GetLocalLife()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<PlayerLife>();
    }

    // ---- çöküş: düşen taşlar (yerel görsel) ----
    private void UpdateRocks()
    {
        if (rockArea == null || Time.time < nextRock) return;
        nextRock = Time.time + UnityEngine.Random.Range(0.15f, 0.35f);
        GameObject r = GameObject.CreatePrimitive(PrimitiveType.Cube);
        r.name = "FallingRock";
        float sz = UnityEngine.Random.Range(0.3f, 0.9f);
        r.transform.localScale = new Vector3(sz, sz * 0.8f, sz);
        r.transform.position = rockArea.position + new Vector3(UnityEngine.Random.Range(-5f, 5f), UnityEngine.Random.Range(6f, 9f), UnityEngine.Random.Range(-4f, 3f));
        r.transform.rotation = UnityEngine.Random.rotation;
        if (rockMaterial != null) r.GetComponent<Renderer>().sharedMaterial = rockMaterial;
        Rigidbody rb = r.AddComponent<Rigidbody>();
        rb.mass = 20f;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 4f;
        rocks.Add(r);
        Destroy(r, 12f);
    }

    // ---- düşüş (yalnızca sahip) ----
    private Vector3 ShaftCenter => shaftAxis != null ? shaftAxis.position : (caveSeal != null ? caveSeal.transform.position + new Vector3(0f, 0f, 4f) : Vector3.zero);

    /// <summary>Konum, mağara kuyusunun içinde ve yer altında mı.</summary>
    private bool InShaft(Vector3 worldPosition)
    {
        if (worldPosition.y > fallStartY) return false;
        Vector3 d = worldPosition - ShaftCenter;
        return Mathf.Abs(d.x) < shaftHalfX + 2f && Mathf.Abs(d.z) < shaftHalfZ + 2f;
    }

    /// <summary>
    /// KULLANICI KARARI: mağara bir uçurum; oyuncu mağaraya girince kuyudan düşer (ışınlama yok).
    /// Kuyuya giren yerel oyuncu o konumdan kinematik dimdik düşüşe geçer.
    /// </summary>
    private void TryBeginLocalSlide(PlayerLife life)
    {
        if (life == null || life.State == PlayerLifeState.Dead) return;
        if (!InShaft(life.transform.position)) return;

        sliding = true;
        landed = false;
        landTime = -1f;
        fallSpeed = 8f;
        nextHeartbeat = Time.time + 0.4f;

        PlayerController pc = life.GetComponent<PlayerController>();
        if (pc != null) pc.ExternalMotion = true;
        CharacterController cc = life.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        StartFallEffects(life);
    }

    private void UpdateSlide(PlayerLife life)
    {
        if (life == null) return;
        if (life.State == PlayerLifeState.Dead) { sliding = false; StopFallEffects(true); return; }
        float dt = Time.deltaTime;
        Vector3 pos = life.transform.position;

        if (!landed)
        {
            fallSpeed = Mathf.Min(terminalSpeed, fallSpeed + fallGravity * dt);
            pos.y -= fallSpeed * dt;

            // kuyu içinde hafif yön (bakış yönüne göre)
            Vector2 input = life.CanAct && moveAction != null && !PauseMenu.IsOpen ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            Vector3 fwd = life.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            pos += (right * input.x + fwd * input.y) * (lateralSpeed * dt);
            Vector3 c = ShaftCenter;
            pos.x = Mathf.Clamp(pos.x, c.x - shaftHalfX, c.x + shaftHalfX);
            pos.z = Mathf.Clamp(pos.z, c.z - shaftHalfZ, c.z + shaftHalfZ);

            CharacterController cc = life.GetComponent<CharacterController>();
            float feetOffset = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
            float floorY = bottomY - feetOffset;
            if (pos.y <= floorY)
            {
                pos.y = floorY;
                landed = true;
                landTime = Time.time;
                OnLocalLanded(pos);
            }
            life.transform.position = pos;
        }
        UpdateFallEffects(life);
    }

    // ---- düşüş efektleri (yalnızca yerel oyuncu) ----
    private float FallProgress(PlayerLife life)
    {
        return life == null ? 0f : Mathf.Clamp01((fallStartY - life.transform.position.y) / Mathf.Max(1f, fallStartY - bottomY));
    }

    private void StartFallEffects(PlayerLife life)
    {
        fallCamera = Camera.main;
        if (fallCamera != null && baseFov < 0f) baseFov = fallCamera.fieldOfView;

        SfxLibrary lib = Sfx.Lib;
        if (windSource == null)
        {
            windSource = gameObject.AddComponent<AudioSource>();
            windSource.loop = true; windSource.spatialBlend = 0f; windSource.playOnAwake = false;
            droneSource = gameObject.AddComponent<AudioSource>();
            droneSource.loop = true; droneSource.spatialBlend = 0f; droneSource.playOnAwake = false;
            droneSource.clip = PlaceholderSounds.CreateDroneLoop();
        }
        if (lib != null && lib.fallWind != null) windSource.clip = lib.fallWind;
        windSource.volume = 0f; droneSource.volume = 0f;
        if (windSource.clip != null) windSource.Play();
        droneSource.Play();

        if (streaks == null && fallCamera != null)
        {
            var go = new GameObject("FallStreaks");
            go.transform.SetParent(fallCamera.transform, false);
            go.transform.localPosition = new Vector3(0f, -7f, 0f);
            go.transform.rotation = Quaternion.identity;
            streaks = go.AddComponent<ParticleSystem>();
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = streaks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.7f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.5f); main.maxParticles = 400; main.loop = true; main.playOnAwake = false;
            var shape = streaks.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(shaftHalfX * 2f, 2f, shaftHalfZ * 2f);
            var em = streaks.emission; em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f;
            if (streakMaterial != null) r.sharedMaterial = streakMaterial;
        }
        if (streaks != null) streaks.Play();
    }

    private void UpdateFallEffects(PlayerLife life)
    {
        float p = FallProgress(life);
        float speed01 = landed ? 0f : fallSpeed / Mathf.Max(1f, terminalSpeed);
        float dt = Time.deltaTime;

        if (fallCamera != null && baseFov > 0f)
            fallCamera.fieldOfView = Mathf.Lerp(fallCamera.fieldOfView, baseFov + fovBoost * speed01, dt * 4f);

        if (windSource != null)
        {
            windSource.volume = Mathf.MoveTowards(windSource.volume, landed ? 0f : 0.25f + 0.65f * speed01, dt * (landed ? 3f : 1.2f));
            windSource.pitch = 0.8f + 0.45f * speed01;
        }
        if (droneSource != null)
        {
            droneSource.volume = Mathf.MoveTowards(droneSource.volume, landed ? 0f : 0.35f + 0.35f * p, dt * (landed ? 0.8f : 0.5f));
            droneSource.pitch = 0.55f + 0.6f * p;          // gerilim: perde yükselir
        }

        // kalp atışı: düştükçe hızlanır, inince birkaç yavaş atış
        SfxLibrary lib = Sfx.Lib;
        if (lib != null && lib.heartbeat != null && Time.time >= nextHeartbeat && (!landed || Time.time - landTime < 3f))
        {
            Sfx.UI(lib.heartbeat, landed ? 0.6f : 0.55f + 0.35f * p, landed ? 0.9f : 1f, 0.02f);
            nextHeartbeat = Time.time + (landed ? 1.1f : Mathf.Lerp(0.85f, 0.33f, p));
        }

        // toz çizgileri: hızla yukarı akar
        if (streaks != null)
        {
            var em = streaks.emission; em.rateOverTime = landed ? 0f : 40f + 260f * speed01;
            var vel = streaks.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            // üç eksen aynı modda olmalı (iki sabit), yoksa Unity her karede hata verir
            vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            vel.y = new ParticleSystem.MinMaxCurve(fallSpeed * 1.1f, fallSpeed * 1.4f);
        }
    }

    private void OnLocalLanded(Vector3 pos)
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib != null)
        {
            Sfx.UI(lib.impactBoom, 1f, 0.85f, 0.03f);
            Sfx.UI(lib.treeCrash, 0.8f, 0.6f, 0.03f);
            Sfx.UI(lib.rockHit, 1f, 0.5f, 0.03f);
            Sfx.UI(lib.jumpLand, 0.9f, 0.7f, 0.03f);
        }
        nextHeartbeat = Time.time + 0.6f;
    }

    private void StopFallEffects(bool immediate)
    {
        if (streaks != null) { var em = streaks.emission; em.rateOverTime = 0f; if (immediate) streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        if (immediate)
        {
            if (windSource != null) windSource.Stop();
            if (droneSource != null) droneSource.Stop();
            if (fallCamera != null && baseFov > 0f) fallCamera.fieldOfView = baseFov;
        }
        else
        {
            float dt = Time.deltaTime;
            if (windSource != null) windSource.volume = Mathf.MoveTowards(windSource.volume, 0f, dt * 2f);
            if (droneSource != null) droneSource.volume = Mathf.MoveTowards(droneSource.volume, 0f, dt * 0.7f);
        }
    }

    private void UpdateShake()
    {
        if (localController == null) return;
        float amp = 0f;
        if (phase.Value == CollapsePhase.Shaking) amp = Mathf.Lerp(0.4f, 2.2f, Mathf.Clamp01(PhaseTime / Mathf.Max(0.1f, shakeDuration)));
        if (sliding)
        {
            if (!landed) amp = Mathf.Max(amp, 0.4f + 1.6f * (fallSpeed / Mathf.Max(1f, terminalSpeed)));
            else
            {
                float k = Time.time - landTime;
                amp = Mathf.Max(amp, k < 0.7f ? Mathf.Lerp(6f, 0f, k / 0.7f) : 0f); // dibe çarpma sarsıntısı
            }
        }

        if (amp > 0f)
        {
            float t = Time.time * 25f;
            localController.ShakeEuler = new Vector3(
                (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(t, 7.7f) - 0.5f) * 3f * amp);
            shakeApplied = true;
        }
        else if (shakeApplied)
        {
            localController.ShakeEuler = Vector3.zero;
            shakeApplied = false;
        }
    }

    private void UpdateStatus(PlayerLife life, CaveTrigger trig)
    {
        // Kullanıcı kararı: olaylarda ipucu/anlatım yazısı yok, oyuncu keşfeder.
        LocalStatus = "";
    }

    // ---- bitiş: kararma + başlık ----
    private void EnsureOverlay()
    {
        if (fadeImage != null) return;
        GameObject canvasGo = new GameObject("CollapseOverlay");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        GameObject img = new GameObject("Fade");
        img.transform.SetParent(canvasGo.transform, false);
        fadeImage = img.AddComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;
        RectTransform rt = fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

        GameObject txt = new GameObject("Title");
        txt.transform.SetParent(canvasGo.transform, false);
        titleText = txt.AddComponent<Text>();
        titleText.font = GameFonts.Title;
        titleText.fontSize = 56;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0.9f, 0.85f, 0.75f, 0f);
        titleText.text = chapterTitle;
        titleText.raycastTarget = false;
        RectTransform tr = titleText.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
    }

    private void UpdateFinished()
    {
        EnsureOverlay();
        fade = Mathf.MoveTowards(fade, 1f, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
        fadeImage.color = new Color(0f, 0f, 0f, fade);
        float titleAlpha = silentSkip.Value ? 0f : Mathf.Clamp01((PhaseTime - fadeSeconds) / 1f);
        titleText.color = new Color(0.9f, 0.85f, 0.75f, titleAlpha);
    }

    /// <summary>G-30: yeni bölümde: siyah ekran açılır, başlık biraz daha kalıp söner.</summary>
    private void UpdateArrived()
    {
        if (fadeImage == null || !fadeImage.gameObject.activeInHierarchy) return;
        float t = PhaseTime;
        if (silentSkip.Value)
        {
            // Bölüm 2 hiç gösterilmez: siyah kalır; Bölüm 3 düşüşü başlayınca (Bölüm 2 çıkışının kendi siyah ekranı üstteyken) kalkar
            fadeImage.color = Color.black;
            titleText.color = Color.clear;
            if (FreefallSequence.Active || t > 45f) fadeImage.transform.parent.gameObject.SetActive(false);
            return;
        }
        fade = Mathf.Clamp01(1f - (t - 0.5f) / Mathf.Max(0.1f, fadeSeconds * 1.5f));
        fadeImage.color = new Color(0f, 0f, 0f, fade);
        titleText.color = new Color(0.9f, 0.85f, 0.75f, Mathf.Clamp01(1f - (t - 3f) / 1.5f));
        if (t > 5f) fadeImage.transform.parent.gameObject.SetActive(false);
    }
}
