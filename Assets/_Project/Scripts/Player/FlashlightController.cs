using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-05: El feneri. Pil yok; ısınma + cooldown (GDD 5.3).
/// Açıkken ısı yükselir, kapalıyken düşer. Isı maksimuma ulaşınca fener söner,
/// cooldown boyunca açılamaz; cooldown sonunda ısı heatAfterCooldown değerine iner.
/// Gösterge HudController'da (G-08).
/// </summary>
public class FlashlightController : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Light spotLight;

    [Header("Isı")]
    [SerializeField] private float maxHeat = 100f;
    [SerializeField] private float heatPerSecond = 4f;
    [SerializeField] private float coolPerSecond = 3f;
    [SerializeField] private float cooldownDuration = 7f;
    [SerializeField] private float heatAfterCooldown = 40f;

    [Header("Renk (ısıya göre)")]
    [SerializeField] private Color coolColor = new Color(1f, 0.93f, 0.78f);
    [SerializeField] private Color hotColor = new Color(1f, 0.45f, 0.1f);
    [Tooltip("Isının bu oranından sonra ışık sararmaya başlar (KULLANICI: sararma son kısımda).")]
    [SerializeField, Range(0f, 1f)] private float yellowStart = 0.7f;

    private Color HeatColor(float heat01) => Color.Lerp(coolColor, hotColor, Mathf.InverseLerp(yellowStart, 1f, Mathf.Clamp01(heat01)));

    [Header("Envanter")]
    [Tooltip("KULLANICI: fener yalnızca envanterde bu eşya seçiliyken açılabilir; başka slota geçince söner.")]
    [SerializeField] private ItemData flashlightItem;

    private InputAction flashlightAction;
    private PlayerLife life;
    private InventorySystem inventory;

    // ---------- B3-03 boyut ışığı (KULLANICI 2026-11-14) ----------
    public const int ModeNormal = 0, ModeGrow = 1, ModeShrink = 2;
    public static readonly Color GrowColor = new Color(0.45f, 1f, 0.5f);
    public static readonly Color ShrinkColor = new Color(0.85f, 0.42f, 1f);

    /// <summary>0 normal, 1 büyüten, 2 küçülten. Yalnızca rüyalar aleminde (Bölüm 3 yüklüyken) değiştirilebilir; R ile döner.</summary>
    public int Mode { get; private set; }
    /// <summary>Boyut ışığı şu an kullanılabilir mi (Bölüm 3 sahnesi yüklü).</summary>
    public static bool SizeLightAvailable => FreefallSequence.Instance != null;

    private static Color ModeColor(int mode, Color normal) => mode == ModeGrow ? GrowColor : mode == ModeShrink ? ShrinkColor : normal;

    private bool IsSelected => inventory == null || flashlightItem == null || inventory.SelectedItem == flashlightItem;

    public float Heat { get; private set; }
    public float Heat01 => maxHeat > 0f ? Heat / maxHeat : 0f;
    public bool IsOn { get; private set; }
    public bool IsOverheated { get; private set; }
    public float CooldownRemaining { get; private set; }

    /// <summary>Bölüme göre ısınma hızı çarpanı (GDD: yağmurda yavaş, mağarada hızlı). Şimdilik 1.</summary>
    public float HeatRateMultiplier { get; set; } = 1f;

    private void Awake()
    {
        flashlightAction = inputActions.FindActionMap("Player", true).FindAction("Flashlight", true);
        life = GetComponent<PlayerLife>();
        inventory = GetComponent<InventorySystem>();
        ApplyLight();
    }

    private void OnEnable() { flashlightAction.Enable(); }
    private void OnDisable() { flashlightAction.Disable(); }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (IsOverheated)
        {
            CooldownRemaining -= dt;
            float k = 1f - Mathf.Clamp01(CooldownRemaining / cooldownDuration);
            Heat = Mathf.Lerp(maxHeat, heatAfterCooldown, k);
            if (CooldownRemaining <= 0f)
            {
                IsOverheated = false;
                CooldownRemaining = 0f;
                Heat = heatAfterCooldown;
            }
        }
        else
        {
            if (life != null && !life.CanAct) IsOn = false; // bayılan/ölü oyuncunun feneri söner
            else if (!IsSelected) IsOn = false;              // fener elde değilse kapalı
            else if (flashlightAction.WasPressedThisFrame()) IsOn = !IsOn;

            // boyut ışığı: R ile normal → büyüten → küçülten (yalnızca rüyada, fener eldeyken)
            if (!SizeLightAvailable) Mode = ModeNormal;
            else if (IsSelected && (life == null || life.CanAct) && !PlayerController.InputLocked && !PauseMenu.IsOpen)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.rKey.wasPressedThisFrame) Mode = (Mode + 1) % 3;
            }

            if (IsOn)
            {
                Heat += heatPerSecond * HeatRateMultiplier * dt;
                if (Heat >= maxHeat)
                {
                    Heat = maxHeat;
                    IsOn = false;
                    IsOverheated = true;
                    CooldownRemaining = cooldownDuration;
                }
            }
            else
            {
                Heat = Mathf.Max(0f, Heat - coolPerSecond * dt);
            }
        }

        ApplyLight();
    }

    /// <summary>G-11: Sahibi olmayan kopyada (bu bileşen kapalıyken) ışığı ağdan gelen duruma göre ayarlar.</summary>
    public void ApplyRemoteState(bool on, float heat01, int mode = ModeNormal)
    {
        Mode = mode;
        if (spotLight == null) return;
        spotLight.enabled = on;
        spotLight.color = ModeColor(mode, HeatColor(heat01));
    }

    private void ApplyLight()
    {
        if (spotLight == null) return;
        spotLight.enabled = IsOn;
        spotLight.color = ModeColor(Mode, HeatColor(Heat01));
    }
}
