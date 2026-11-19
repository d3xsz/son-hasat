using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (cila): Oyun içi duraklatma menüsü + ayarlar paneli. Kendini kurar (Resources/UI/PauseMenu prefab'ı, kalıcı).
/// Ortak oyun olduğu için zaman DURMAZ; menü açıkken yerel oyuncu hareket edemez/bakamaz.
/// Esc: aç/kapat (ayarlar/onay açıksa bir geri). Ana menüde "Ayarlar" butonu yalnızca ayarlar panelini açar.
/// Ana menüye dönüş: ağ kapatılır, kalıcı NetworkManager yok edilir (lobi yenisini kurar). Host ayrılırsa
/// diğer oyuncular bağlantı kopunca otomatik ana menüye döner.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Paneller")]
    [SerializeField] private GameObject root;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject confirmPanel;

    [Header("Ana panel")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;

    [Header("Ayarlar")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Text sensitivityValue;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Text volumeValue;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Text musicValue;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Text sfxValue;
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private Text brightnessValue;
    [SerializeField] private Button qualityButton;
    [SerializeField] private Text qualityLabel;
    [SerializeField] private Button fullscreenButton;
    [SerializeField] private Text fullscreenLabel;
    [SerializeField] private Button defaultsButton;
    [SerializeField] private Button backButton;

    [Header("Onay")]
    [SerializeField] private Text confirmText;
    [SerializeField] private Button confirmYes;
    [SerializeField] private Button confirmNo;

    [SerializeField] private string mainMenuScene = "MainMenu";
    [SerializeField] private string[] menuScenes = { "MainMenu", "Lobby" };

    public static PauseMenu Instance { get; private set; }
    /// <summary>Oyun içinde menü (veya ayarlar) açık: yerel oyuncu girdisi kapalı.</summary>
    public static bool IsOpen => Instance != null && Instance.root != null && Instance.root.activeSelf && Instance.InGame;

    private Action confirmAction;
    private bool settingsOnly;      // ana menüden açıldı (duraklatma paneli yok)
    private bool wasInSession;
    private bool leaving;
    private GameObject ownEventSystem;
    private bool refreshing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Instance != null) return;
        var prefab = Resources.Load<PauseMenu>("UI/PauseMenu");
        if (prefab == null) { Debug.LogWarning("[Menü] Resources/UI/PauseMenu bulunamadı."); return; }
        var menu = Instantiate(prefab);
        menu.name = "PauseMenu";
        DontDestroyOnLoad(menu.gameObject);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        resumeButton.onClick.AddListener(Close);
        settingsButton.onClick.AddListener(ShowSettings);
        mainMenuButton.onClick.AddListener(() => Confirm(LeaveText(), ReturnToMainMenu));
        quitButton.onClick.AddListener(() => Confirm("Oyundan çıkılsın mı?", QuitGame));

        sensitivitySlider.minValue = GameSettings.SensitivityMin; sensitivitySlider.maxValue = GameSettings.SensitivityMax;
        volumeSlider.minValue = 0f; volumeSlider.maxValue = 1f;
        brightnessSlider.minValue = -1f; brightnessSlider.maxValue = 1f;
        sensitivitySlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SetSensitivity(v); });
        volumeSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SetMasterVolume(v); });
        if (musicSlider != null)
        {
            musicSlider.minValue = 0f; musicSlider.maxValue = 1f;
            musicSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SetMusicVolume(v); });
        }
        if (sfxSlider != null)
        {
            sfxSlider.minValue = 0f; sfxSlider.maxValue = 1f;
            sfxSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SetSfxVolume(v); });
        }
        brightnessSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SetBrightness(v); });
        qualityButton.onClick.AddListener(() => GameSettings.SetQuality((GameSettings.Quality + 1) % GameSettings.QualityLevels));
        fullscreenButton.onClick.AddListener(() => GameSettings.SetFullscreen(!GameSettings.Fullscreen));
        defaultsButton.onClick.AddListener(GameSettings.ResetDefaults);
        backButton.onClick.AddListener(Back);

        confirmYes.onClick.AddListener(() => { var a = confirmAction; confirmAction = null; confirmPanel.SetActive(false); a?.Invoke(); });
        confirmNo.onClick.AddListener(() => { confirmAction = null; confirmPanel.SetActive(false); mainPanel.SetActive(!settingsOnly); });

        GameSettings.Changed += Refresh;
        SceneManager.sceneLoaded += OnSceneLoaded;
        root.SetActive(false);
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= Refresh;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        CloseAll();
        leaving = false;
        wasInSession = false;
    }

    // ---------- Durum ----------

    private bool InMenuScene
    {
        get
        {
            string n = SceneManager.GetActiveScene().name;
            foreach (string s in menuScenes) if (s == n) return true;
            return false;
        }
    }

    /// <summary>Yerel oyuncunun olduğu bir oyun sahnesindeyiz.</summary>
    private bool InGame
    {
        get
        {
            if (InMenuScene) return false;
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening && nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
        }
    }

    private void Update()
    {
        bool inGame = InGame;
        if (inGame) wasInSession = true;

        // host ayrıldı / bağlantı koptu: oyun sahnesinde kaldıysak ana menüye dön
        if (wasInSession && !leaving && !InMenuScene)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { ReturnToMainMenu(); return; }
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !NoteReader.IsOpen && NoteReader.ClosedFrame != Time.frameCount)
        {
            if (root.activeSelf) Back();
            else if (inGame) Open();
        }

        if (root.activeSelf)
        {
            if (!inGame && !settingsOnly) { CloseAll(); return; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ---------- Paneller ----------

    public void Open()
    {
        settingsOnly = false;
        EnsureEventSystem();
        root.SetActive(true);
        mainPanel.SetActive(true);
        settingsPanel.SetActive(false);
        confirmPanel.SetActive(false);
    }

    /// <summary>Ana menüdeki "Ayarlar" butonu: yalnızca ayarlar paneli.</summary>
    public static void OpenSettingsOnly()
    {
        if (Instance == null) return;
        Instance.settingsOnly = true;
        Instance.EnsureEventSystem();
        Instance.root.SetActive(true);
        Instance.mainPanel.SetActive(false);
        Instance.confirmPanel.SetActive(false);
        Instance.ShowSettings();
    }

    private void ShowSettings()
    {
        mainPanel.SetActive(false);
        settingsPanel.SetActive(true);
        Refresh();
    }

    private void Back()
    {
        if (confirmPanel.activeSelf) { confirmAction = null; confirmPanel.SetActive(false); mainPanel.SetActive(!settingsOnly); return; }
        if (settingsPanel.activeSelf)
        {
            GameSettings.Save();
            if (settingsOnly) { CloseAll(); return; }
            settingsPanel.SetActive(false);
            mainPanel.SetActive(true);
            return;
        }
        Close();
    }

    public void Close()
    {
        GameSettings.Save();
        bool lockCursor = InGame;
        CloseAll();
        if (lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    private void CloseAll()
    {
        root.SetActive(false);
        settingsOnly = false;
        confirmAction = null;
        // kendi EventSystem'imiz yalnızca menü açıkken (sahnede yoksa) yaşar; sahne değişince çift olmasın
        if (ownEventSystem != null) { Destroy(ownEventSystem); ownEventSystem = null; }
    }

    private void Confirm(string text, Action action)
    {
        confirmText.text = text;
        confirmAction = action;
        mainPanel.SetActive(false);
        confirmPanel.SetActive(true);
    }

    private static string LeaveText()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool host = nm != null && nm.IsHost && nm.ConnectedClientsIds.Count > 1;
        return host ? "Ana menüye dönülsün mü?\nOyunu sen kurdun: herkes için biter." : "Ana menüye dönülsün mü?";
    }

    private void Refresh()
    {
        refreshing = true;
        sensitivitySlider.value = GameSettings.Sensitivity;
        volumeSlider.value = GameSettings.MasterVolume;
        brightnessSlider.value = GameSettings.Brightness;
        if (musicSlider != null) musicSlider.value = GameSettings.MusicVolume;
        if (sfxSlider != null) sfxSlider.value = GameSettings.SfxVolume;
        refreshing = false;
        sensitivityValue.text = GameSettings.Sensitivity.ToString("0.0") + "x";
        volumeValue.text = "%" + Mathf.RoundToInt(GameSettings.MasterVolume * 100f);
        if (musicValue != null) musicValue.text = "%" + Mathf.RoundToInt(GameSettings.MusicVolume * 100f);
        if (sfxValue != null) sfxValue.text = "%" + Mathf.RoundToInt(GameSettings.SfxVolume * 100f);
        float b = GameSettings.Brightness;
        brightnessValue.text = Mathf.Abs(b) < 0.005f ? "0" : (b > 0 ? "+" : "") + Mathf.RoundToInt(b * 100f);
        qualityLabel.text = "Grafik: " + GameSettings.QualityName(GameSettings.Quality);
        fullscreenLabel.text = "Tam ekran: " + (GameSettings.Fullscreen ? "Açık" : "Kapalı");
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null && EventSystem.current.gameObject != ownEventSystem) return;
        if (ownEventSystem != null) return;
        ownEventSystem = new GameObject("PauseEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        ownEventSystem.transform.SetParent(transform, false);
    }

    private void LateUpdate()
    {
        // sahnenin kendi EventSystem'i varsa bizimkini kaldır (çift EventSystem uyarısı olmasın)
        if (ownEventSystem != null)
        {
            foreach (EventSystem es in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                if (es.gameObject != ownEventSystem) { Destroy(ownEventSystem); ownEventSystem = null; break; }
        }
    }

    // ---------- Çıkışlar ----------

    public void ReturnToMainMenu()
    {
        if (leaving) return;
        leaving = true;
        CloseAll();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject); // lobi sahnesi yenisini kurar
        }
        PlayerController.InputLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ScreenFader.LoadScene(mainMenuScene);
    }

    private static void QuitGame()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
