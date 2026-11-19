using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// G-08: Basit HUD (placeholder görseller). Oyuncunun bileşenlerini okur, kendisi oyun durumu tutmaz.
/// Nişan, etkileşim ipucu, mesaj, 4 slot, fener ısı barı, zehir barı.
/// Zehir şimdilik placeholder: G-14'te PoisonSystem SetPoison'ı çağıracak.
/// Ağda yerel oyuncuya Bind(...) ile bağlanır (G-10).
/// </summary>
public class HudController : MonoBehaviour
{
    [Header("Oyuncu (boşsa sahnedeki PlayerInteractor'lı obje bulunur)")]
    [SerializeField] private GameObject player;

    [Header("Nişan ve ipucu")]
    [SerializeField] private RectTransform crosshair;
    [SerializeField] private Text promptText;
    [SerializeField] private Text messageText;

    [Header("Slotlar (4 adet)")]
    [SerializeField] private Image[] slotBackgrounds;
    [SerializeField] private Text[] slotLabels;
    [Tooltip("C-04: doluysa ikonlu slot görünümü (etiket yalnızca numara).")]
    [SerializeField] private Image[] slotIcons;
    [SerializeField] private Image[] slotFrames;
    [SerializeField] private Text selectedNameText;
    private int lastSelectedIndex = -1;

    [Header("Fener ısısı")]
    [SerializeField] private RectTransform heatFill;
    [SerializeField] private Image heatFillImage;
    [SerializeField] private Text heatLabel;

    [Header("Geçici test göstergesi (G-22)")]
    [SerializeField] private Text wrathDebugText;

    [Tooltip("Test: sol üstte Öfke değerini göster (oyunda kapalı; oyuncu Öfkeyi hava/seslerden sezer).")]
    [SerializeField] private bool showWrathDebug = false;

    [Header("G-31 Hedefler")]
    [SerializeField] private Text objectiveText;
    [Tooltip("Hedef listesini ekranda göster (kullanıcı kararı: kapalı, oyuncu keşfeder).")]
    [SerializeField] private bool showObjectives = false;

    [Header("Yaşam durumu ve basılı tutma (G-17)")]
    [SerializeField] private Text statusText;
    [SerializeField] private GameObject holdRoot;
    [SerializeField] private RectTransform holdFill;

    [Header("Sağlık (zehir değerinden türetilir, GDD 5.4)")]
    [SerializeField] private RectTransform poisonFill;
    [SerializeField] private Text poisonLabel;

    [Header("Renkler")]
    [SerializeField] private Color slotNormal = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color slotSelected = new Color(0.95f, 0.75f, 0.2f, 0.9f);
    [Tooltip("Seçili slot arka planının tonu (sprite rengiyle çarpılır).")]
    [SerializeField] private Color slotSelectedTint = new Color(0.12f, 0.11f, 0.09f, 0.8f);
    [SerializeField] private Color heatCool = new Color(1f, 0.93f, 0.78f);
    [SerializeField] private Color heatHot = new Color(1f, 0.45f, 0.1f);
    [SerializeField] private Color heatOverheated = new Color(0.9f, 0.15f, 0.1f);
    [SerializeField] private Color healthGood = new Color(0.45f, 0.85f, 0.25f);
    [SerializeField] private Color healthBad = new Color(0.85f, 0.15f, 0.1f);

    private PlayerInteractor interactor;
    private InventorySystem inventory;
    private FlashlightController flashlight;
    private PoisonSystem poisonSystem;
    private PlayerLife life;
    private Image poisonFillImage;
    private float messageUntil;
    private GameObject boundPlayer;
    private float nextBindTime;
    private bool wasConnected;
    private bool connectionLostShown;

    /// <summary>Ağ bağlantısı koparsa (örn. host oyundan çıktı) kalıcı bir uyarı gösterir.</summary>
    private void CheckConnection()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool connected = nm != null && nm.IsListening;
        if (connected)
        {
            wasConnected = true;
            connectionLostShown = false;
        }
        else if (wasConnected && !connectionLostShown)
        {
            connectionLostShown = true;
            if (messageText != null)
            {
                messageText.text = "Bağlantı koptu. Oyunu kapatıp lobiye dön.";
                messageUntil = float.MaxValue;
            }
        }
    }

    /// <summary>Yerel oyuncu henüz yoksa (ağda oyuncular sahne yüklenince doğar) periyodik olarak arar.</summary>
    private void EnsureBound()
    {
        if (boundPlayer != null) return;
        if (Time.unscaledTime < nextBindTime) return;
        nextBindTime = Time.unscaledTime + 0.25f;

        GameObject target = player;
        if (target == null)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                NetworkObject po = nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
                if (po != null) target = po.gameObject;
            }
            else
            {
                PlayerInteractor found = FindFirstObjectByType<PlayerInteractor>();
                if (found != null) target = found.gameObject;
            }
        }
        if (target != null) Bind(target);
    }

    public void Bind(GameObject playerObject)
    {
        Unsubscribe();
        boundPlayer = playerObject;
        interactor = playerObject.GetComponent<PlayerInteractor>();
        inventory = playerObject.GetComponent<InventorySystem>();
        flashlight = playerObject.GetComponent<FlashlightController>();
        poisonSystem = playerObject.GetComponent<PoisonSystem>();
        life = playerObject.GetComponent<PlayerLife>();
        poisonFillImage = poisonFill != null ? poisonFill.GetComponent<Image>() : null;
        if (inventory != null)
        {
            inventory.Changed += RefreshSlots;
            inventory.MessageRaised += OnMessage;
        }
        RefreshSlots();
    }

    private void OnDestroy() { Unsubscribe(); }

    private void Unsubscribe()
    {
        if (inventory == null) return;
        inventory.Changed -= RefreshSlots;
        inventory.MessageRaised -= OnMessage;
    }

    private void Update()
    {
        EnsureBound();
        CheckConnection();
        UpdateCrosshairAndPrompt();
        UpdateLifeStatus();
        UpdateWrathDebug();
        UpdateMessage();
        UpdateFlashlight();
        UpdatePoison();
        UpdateObjectives();
        AnimateSlots();
    }

    // C-04: seçili slot hafifçe büyür, seçili eşyanın adı slotların üstünde kısa süre görünür
    private float nameAlpha;
    private float nameShownUntil;

    private void AnimateSlots()
    {
        float dt = Time.unscaledDeltaTime;
        if (slotBackgrounds != null && inventory != null)
            for (int i = 0; i < slotBackgrounds.Length; i++)
            {
                if (slotBackgrounds[i] == null) continue;
                float target = i == inventory.SelectedIndex ? 1.1f : 1f;
                var rt = slotBackgrounds[i].rectTransform;
                float s = Mathf.Lerp(rt.localScale.x, target, 1f - Mathf.Exp(-14f * dt));
                rt.localScale = new Vector3(s, s, 1f);
            }
        if (selectedNameText != null)
        {
            float targetA = Time.time < nameShownUntil ? 1f : 0f;
            nameAlpha = Mathf.MoveTowards(nameAlpha, targetA, dt * (targetA > nameAlpha ? 6f : 1.5f));
            Color c = selectedNameText.color; c.a = nameAlpha; selectedNameText.color = c;
        }
    }

    /// <summary>G-31: sağ üstte bölüm hedefleri listesi.</summary>
    private void UpdateObjectives()
    {
        if (objectiveText == null) return;
        ObjectiveSystem os = ObjectiveSystem.Instance;
        objectiveText.text = showObjectives && os != null ? os.DisplayText : "";
    }

    // ---------- Etkileşim/nişangah (KULLANICI: UI düzenlemesi) ----------
    [Header("Nişangah + etkileşim (doluysa yeni görünüm)")]
    [SerializeField] private Image crosshairRing;
    [SerializeField] private Image holdRing;
    [SerializeField] private CanvasGroup promptGroup;
    [SerializeField] private Text promptLabel;
    private float ringBlend;
    private string lastPromptText = "";

    private void UpdateCrosshairCompact()
    {
        float dt = Time.unscaledDeltaTime;
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool canAct = life == null || life.CanAct;
        IInteractable target = (locked && canAct && interactor != null) ? interactor.CurrentTarget : null;

        if (crosshair != null) crosshair.gameObject.SetActive(locked && canAct);
        ringBlend = Mathf.MoveTowards(ringBlend, target != null ? 1f : 0f, dt * 7f);
        float e = ringBlend * ringBlend * (3f - 2f * ringBlend);
        if (crosshairRing != null)
        {
            crosshairRing.color = new Color(1f, 0.95f, 0.85f, 0.85f * e);
            float s = Mathf.Lerp(1.6f, 1f, e);
            crosshairRing.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        // ipucu: [E] rozeti + eylem adı (basılı tutulacaksa soluk not)
        if (target != null)
        {
            string label = target.InteractionPrompt + (target is IHoldInteractable ? "  <color=#ffffff88><size=18>(basılı tut)</size></color>" : "");
            if (label != lastPromptText && promptLabel != null) { promptLabel.text = label; lastPromptText = label; }
        }
        if (promptGroup != null)
        {
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, target != null ? 1f : 0f, dt * 8f);
            float y = Mathf.Lerp(-96f, -86f, promptGroup.alpha);
            var rt = (RectTransform)promptGroup.transform; rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        }

        float p = interactor != null ? interactor.HoldProgress01 : 0f;
        if (holdRing != null) { holdRing.fillAmount = p; holdRing.enabled = p > 0.001f; }
        if (holdRoot != null) holdRoot.SetActive(false);
    }

    private void UpdateCrosshairAndPrompt()
    {
        if (promptGroup != null) { UpdateCrosshairCompact(); return; }
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        IInteractable target = (locked && interactor != null) ? interactor.CurrentTarget : null;

        if (crosshair != null)
        {
            crosshair.gameObject.SetActive(locked);
            float size = target != null ? 9f : 6f;
            crosshair.sizeDelta = new Vector2(size, size);
        }
        if (promptText != null)
        {
            string key = target is IHoldInteractable ? "[E basılı tut] " : "[E] ";
            promptText.text = target != null ? key + target.InteractionPrompt : "";
        }

        UpdateHoldBar();
    }

    // Geçici: oyunda Öfke sayısı gösterilmez (GDD: oyuncu hava/seslerden sezer); test için, sonra kaldırılacak.
    private void UpdateWrathDebug()
    {
        if (wrathDebugText == null) return;
        if (!showWrathDebug) { wrathDebugText.text = ""; return; }
        WrathSystem w = WrathSystem.Instance;
        wrathDebugText.text = w != null ? "[test] Ada Öfkesi: " + w.Wrath + " / 100  (seviye " + w.Level + ")" : "";
    }

    private void UpdateHoldBar()
    {
        float p = interactor != null ? interactor.HoldProgress01 : 0f;
        if (holdRoot != null) holdRoot.SetActive(p > 0.001f);
        SetFill(holdFill, p);
    }

    private void UpdateLifeStatus()
    {
        if (statusText == null) return;
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.State == GameState.Failed)
        {
            statusText.text = "BÖLÜM BAŞARISIZ\nSon checkpoint'ten yeniden başlıyor: " + Mathf.CeilToInt(gm.RestartRemaining) + " sn";
            return;
        }
        if (life == null) { statusText.text = ""; return; }

        // G-29: mağara olayı durumu (eşikte bekleme, çöküş, kayma)
        string caveStatus = CollapseSequence.LocalStatus;
        if (!string.IsNullOrEmpty(caveStatus) && life.State != PlayerLifeState.Dead)
        {
            statusText.text = caveStatus;
            return;
        }

        // G-27 alan uyarısı kaldırıldı (kullanıcı kararı: ipucu yazısı yok, oyuncu keşfeder).

        switch (life.State)
        {
            case PlayerLifeState.Fainted:
                statusText.text = "BAYILDIN  -  " + Mathf.CeilToInt(life.FaintRemaining) + " sn\nBir arkadaşın seni kaldırabilir";
                break;
            case PlayerLifeState.Dead:
                statusText.text = "ÖLDÜN  -  İZLEYİCİ MODU\n[Sol tık] başka oyuncuyu izle";
                break;
            default:
                statusText.text = "";
                break;
        }
    }

    private void OnMessage(string text)
    {
        if (messageText == null) return;
        messageText.text = text;
        messageUntil = Time.time + 2f;
    }

    /// <summary>Ekranın mesaj satırında belirli bir süre bilgilendirme gösterir (ör. öğretici ipucu: "R: fener ışığını değiştir").</summary>
    public void ShowHint(string text, float seconds)
    {
        if (messageText == null) return;
        messageText.text = text;
        messageUntil = Time.time + Mathf.Max(0.5f, seconds);
    }

    private void UpdateMessage()
    {
        if (messageText != null && Time.time >= messageUntil) messageText.text = "";
    }

    private void RefreshSlots()
    {
        if (inventory == null || slotLabels == null) return;
        bool iconMode = slotIcons != null && slotIcons.Length > 0;
        for (int i = 0; i < slotLabels.Length && i < InventorySystem.SlotCount; i++)
        {
            ItemData item = inventory.GetItem(i);
            bool selected = i == inventory.SelectedIndex;
            if (iconMode)
            {
                // C-04: ikonlu slot; numara köşede, ad slotların üstünde
                slotLabels[i].text = (i + 1).ToString();
                slotLabels[i].color = selected ? slotSelected : new Color(1f, 1f, 1f, 0.55f);
                if (i < slotIcons.Length && slotIcons[i] != null)
                {
                    Sprite icon = item != null ? item.Icon : null;
                    slotIcons[i].sprite = icon;
                    slotIcons[i].enabled = icon != null;
                    slotIcons[i].color = selected ? Color.white : new Color(1f, 1f, 1f, 0.7f);
                }
                if (slotFrames != null && i < slotFrames.Length && slotFrames[i] != null) slotFrames[i].enabled = selected;
                if (slotBackgrounds != null && i < slotBackgrounds.Length && slotBackgrounds[i] != null)
                    slotBackgrounds[i].color = selected ? slotSelectedTint : slotNormal; // UI_Slot sprite kendi rengini taşır
                continue;
            }
            string text = "<b>" + (i + 1) + "</b>  " + (item != null ? item.DisplayName : "-");
            if (item != null && item.IsLocked) text += "\n<size=16>kilitli</size>";
            else if (item != null && item.IsHeavy) text += "\n<size=16>ağır</size>";
            slotLabels[i].text = text;
            slotLabels[i].color = selected ? new Color(0.1f, 0.08f, 0.02f) : Color.white;
            if (slotBackgrounds != null && i < slotBackgrounds.Length && slotBackgrounds[i] != null)
                slotBackgrounds[i].color = selected ? slotSelected : slotNormal;
        }
        if (selectedNameText != null)
        {
            ItemData sel = inventory.SelectedItem;
            string name = sel != null ? sel.DisplayName + (sel.IsHeavy ? "  ·  ağır" : "") : "";
            if (name != selectedNameText.text) { selectedNameText.text = name; nameShownUntil = Time.time + 1.6f; }
            else if (lastSelectedIndex != inventory.SelectedIndex) nameShownUntil = Time.time + 1.6f;
            lastSelectedIndex = inventory.SelectedIndex;
        }
    }

    // ---------- C-04+: kompakt ikonlu barlar (KULLANICI: sağlık ve fener barları) ----------
    [Header("Kompakt barlar (doluysa uzun etiketler yerine kullanılır)")]
    [SerializeField] private Image healthIcon;
    [SerializeField] private RectTransform healthTrail;
    [SerializeField] private Text healthValueText;
    [SerializeField] private Image flashIcon;
    [SerializeField] private Text flashValueText;
    private float trailValue = 1f;
    private float trailHoldUntil;
    private float lastHealth01 = 1f;

    private void UpdateFlashlightCompact()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
        // kalan fener süresi: soğukken dolu, ısındıkça azalır (son kısımda sararır)
        SetFill(heatFill, 1f - flashlight.Heat01);
        Color barColor = flashlight.IsOverheated ? Color.Lerp(heatOverheated, heatOverheated * 0.55f, pulse)
                                                 : Color.Lerp(heatCool, heatHot, Mathf.InverseLerp(0.55f, 1f, flashlight.Heat01));
        if (!flashlight.IsOn && !flashlight.IsOverheated) barColor = Color.Lerp(barColor, new Color(0.55f, 0.53f, 0.5f), 0.45f);
        barColor.a = 1f;
        if (heatFillImage != null) heatFillImage.color = barColor;
        if (flashIcon != null)
            flashIcon.color = flashlight.IsOverheated ? Color.Lerp(new Color(1f, 0.35f, 0.25f), new Color(0.5f, 0.2f, 0.15f), pulse)
                            : flashlight.IsOn ? new Color(1f, 0.92f, 0.7f) : new Color(0.6f, 0.6f, 0.62f, 0.75f);
        if (flashValueText != null)
            flashValueText.text = flashlight.IsOverheated ? flashlight.CooldownRemaining.ToString("0.0") + " sn" : "";
    }

    private void UpdatePoisonCompact(float health01)
    {
        float dt = Time.unscaledDeltaTime;
        if (health01 < lastHealth01 - 0.001f) trailHoldUntil = Time.unscaledTime + 0.5f; // darbe: iz bir süre bekler
        lastHealth01 = health01;
        if (trailValue < health01) trailValue = health01;
        else if (Time.unscaledTime > trailHoldUntil) trailValue = Mathf.MoveTowards(trailValue, health01, 0.6f * dt);
        SetFill(healthTrail, trailValue);

        Color c = health01 > 0.5f ? Color.Lerp(new Color(0.95f, 0.75f, 0.25f), healthGood, (health01 - 0.5f) * 2f)
                                  : Color.Lerp(healthBad, new Color(0.95f, 0.75f, 0.25f), health01 * 2f);
        if (poisonFillImage != null) poisonFillImage.color = c;
        if (healthIcon != null)
        {
            healthIcon.color = c;
            float beat = health01 < 0.35f ? 1f + 0.12f * Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.unscaledTime * (2.5f + (0.35f - health01) * 8f))), 6f) : 1f;
            healthIcon.rectTransform.localScale = new Vector3(beat, beat, 1f);
        }
        if (healthValueText != null) healthValueText.text = Mathf.CeilToInt(health01 * 100f).ToString();
    }

    private void UpdateFlashlight()
    {
        if (flashlight == null) return;
        if (flashIcon != null) { UpdateFlashlightCompact(); return; }
        SetFill(heatFill, flashlight.Heat01);
        if (heatFillImage != null)
            heatFillImage.color = flashlight.IsOverheated ? heatOverheated : Color.Lerp(heatCool, heatHot, flashlight.Heat01);
        if (heatLabel != null)
        {
            heatLabel.text = flashlight.IsOverheated
                ? "FENER  AŞIRI ISINDI  " + flashlight.CooldownRemaining.ToString("0.0") + " sn"
                : "FENER  " + (flashlight.IsOn ? "AÇIK" : "KAPALI");
        }
    }

    private void UpdatePoison()
    {
        float p = poisonSystem != null ? poisonSystem.Value : 0f;
        float health01 = 1f - Mathf.Clamp01(p / PoisonSystem.MaxPoison);
        SetFill(poisonFill, health01);
        if (healthIcon != null) { UpdatePoisonCompact(health01); return; }
        if (poisonFillImage != null) poisonFillImage.color = Color.Lerp(healthBad, healthGood, health01);
        if (poisonLabel != null)
            poisonLabel.text = "SAĞLIK  " + Mathf.CeilToInt(health01 * 100f) + " / 100   (zehir " + p.ToString("0") + ")";
    }

    private static void SetFill(RectTransform fill, float t)
    {
        if (fill == null) return;
        Vector2 max = fill.anchorMax;
        max.x = Mathf.Clamp01(t);
        fill.anchorMax = max;
    }
}
