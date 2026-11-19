using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// G-09: Lobi arayüzü. IP/port girilir; Host ol veya Katıl.
/// Oyuncu listesi (adlarla) host ve tüm istemcilerde görünür (ConnectionManager.LobbyPlayers).
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [SerializeField] private ConnectionManager connection;
    [SerializeField] private InputField addressField;
    [SerializeField] private InputField portField;
    [Tooltip("Oyuncu adı (diğer oyuncuların ekranında başının üstünde görünür).")]
    [SerializeField] private InputField nameField;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Text statusText;
    [SerializeField] private Text playersText;
    [SerializeField] private Text localIpText;
    [Tooltip("G-38: ana menüye dönüş (bağlı değilken).")]
    [SerializeField] private Button backButton;

    [Header("Oyuncu kartı (4 yuva)")]
    [SerializeField] private Text[] slotNames = new Text[0];
    [SerializeField] private Text[] slotTags = new Text[0];
    [SerializeField] private Graphic[] slotBands = new Graphic[0];
    [Tooltip("İstemcide başlat butonu yerine gösterilir.")]
    [SerializeField] private Text waitingText;

    private static readonly Color StatusNeutral = new Color(0.25f, 0.17f, 0.1f);
    private static readonly Color StatusGood = new Color(0.16f, 0.42f, 0.14f);
    private static readonly Color StatusBad = new Color(0.62f, 0.13f, 0.1f);
    private static readonly Color StatusWait = new Color(0.58f, 0.4f, 0.06f);
    [SerializeField] private string mainMenuScene = "MainMenu";

    // KULLANICI (2026-11-15): sunum için başlangıç bölümü seçimi (yalnızca host; "Oyunu Başlat"ın altında, aynı görünümde)
    private Button chapterButton;
    private Text chapterLabel;

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        addressField.text = connection.DefaultAddress;
        portField.text = connection.DefaultPort.ToString();
        if (nameField != null)
        {
            nameField.characterLimit = PlayerIdentity.MaxLength;
            nameField.text = PlayerIdentity.LocalName;
            nameField.onEndEdit.AddListener(v => PlayerIdentity.LocalName = v);
        }
        localIpText.text = "Bu bilgisayarın IP'si: " + ConnectionManager.GetLocalIPv4List() + "   (arkadaşların bu adrese bağlanır)";

        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        disconnectButton.onClick.AddListener(connection.Disconnect);
        BuildChapterButton();
        startButton.onClick.AddListener(connection.StartGame);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        connection.StatusChanged += OnStatusChanged;
        statusText.text = connection.Status;
        ColorStatus();
    }

    private void OnDestroy()
    {
        if (connection != null) connection.StatusChanged -= OnStatusChanged;
    }

    private void BuildChapterButton()
    {
        GameObject go = Instantiate(startButton.gameObject, startButton.transform.parent);
        go.name = "ChapterButton";
        go.transform.SetSiblingIndex(startButton.transform.GetSiblingIndex() + 1);
        RectTransform rt = (RectTransform)go.transform, srt = (RectTransform)startButton.transform;
        rt.sizeDelta = new Vector2(srt.sizeDelta.x, 56f);
        rt.anchoredPosition = srt.anchoredPosition + new Vector2(0f, -(srt.sizeDelta.y * 0.5f + 28f + 30f));
        chapterButton = go.GetComponent<Button>();
        chapterButton.onClick = new Button.ButtonClickedEvent();          // başlat düğmesinden kopyalanan olaylar gitsin
        chapterButton.onClick.AddListener(() => { ChapterSelect.Next(); RefreshChapterLabel(); });
        chapterLabel = go.GetComponentInChildren<Text>();
        if (chapterLabel != null) chapterLabel.fontSize = Mathf.Max(18, Mathf.RoundToInt(chapterLabel.fontSize * 0.68f));
        RefreshChapterLabel();
    }

    private void RefreshChapterLabel()
    {
        if (chapterLabel != null) chapterLabel.text = ChapterSelect.Label + "   ›";
    }

    private void OnStatusChanged(string text) { statusText.text = text; ColorStatus(); }

    /// <summary>Durum yazısı rengi: bağlandı/katıldı yeşil, hata kırmızı, bekleme sarı.</summary>
    private void ColorStatus()
    {
        string t = (statusText.text ?? "").ToLowerInvariant();
        Color c = StatusNeutral;
        if (t.Contains("hata") || t.Contains("kesildi") || t.Contains("kurulamadı") || t.Contains("başlatılamadı") || t.Contains("geçersiz") || t.Contains("dolu") || t.Contains("ayrıldı")) c = StatusBad;
        else if (t.Contains("bağlanılıyor") || t.Contains("bekle") || t.Contains("başlatılıyor")) c = StatusWait;
        else if (t.Contains("bağlandın") || t.Contains("katıldı") || t.Contains("lobi açık") || t.Contains("başladı")) c = StatusGood;
        statusText.color = c;
    }

    private bool TryReadPort(out ushort port)
    {
        if (ushort.TryParse(portField.text.Trim(), out port) && port >= 1024) return true;
        statusText.text = "Geçersiz port (1024-65535 arası bir sayı gir)";
        return false;
    }

    /// <summary>Ana menüye dön: kalıcı (DontDestroyOnLoad) NetworkManager'ı kapat, lobiye tekrar girince yenisi oluşsun.</summary>
    private void OnBackClicked()
    {
        if (connection.IsActive) return;
        SaveName();
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null) Destroy(nm.gameObject);
        ScreenFader.LoadScene(mainMenuScene);
    }

    private void SaveName() { if (nameField != null) PlayerIdentity.LocalName = nameField.text; }

    private void OnHostClicked()
    {
        SaveName();
        if (!TryReadPort(out ushort port)) return;
        connection.StartHost(port);
    }

    private void OnJoinClicked()
    {
        SaveName();
        string address = addressField.text.Trim();
        if (string.IsNullOrEmpty(address)) address = connection.DefaultAddress;
        if (!IPAddress.TryParse(address, out _)) { statusText.text = "Geçersiz IP adresi"; return; }
        if (!TryReadPort(out ushort port)) return;
        connection.StartClient(address, port);
    }

    private void Update()
    {
        bool active = connection.IsActive;
        hostButton.interactable = !active;
        joinButton.interactable = !active;
        disconnectButton.interactable = active;
        startButton.interactable = connection.IsHost;
        addressField.interactable = !active;
        portField.interactable = !active;
        if (nameField != null) nameField.interactable = !active;
        if (backButton != null) backButton.interactable = !active;
        // istemci: başlat butonu yerine bekleme yazısı
        bool client = connection.IsClientOnly;
        if (startButton.gameObject.activeSelf == client) startButton.gameObject.SetActive(!client);
        if (chapterButton != null)
        {
            if (chapterButton.gameObject.activeSelf == client) chapterButton.gameObject.SetActive(!client);
            chapterButton.interactable = connection.IsHost;
        }
        if (waitingText != null && waitingText.gameObject.activeSelf != client) waitingText.gameObject.SetActive(client);
        if (slotNames != null && slotNames.Length > 0) { UpdateSlots(); if (playersText != null) playersText.text = ""; }
        else playersText.text = BuildPlayersText();
    }

    private void UpdateSlots()
    {
        var list = (connection.IsHost || connection.IsClientConnected) ? connection.LobbyPlayers : null;
        for (int i = 0; i < slotNames.Length; i++)
        {
            bool has = list != null && i < list.Count;
            var nameText = slotNames[i];
            var tag = i < slotTags.Length ? slotTags[i] : null;
            if (has)
            {
                var p = list[i];
                nameText.text = p.Value;
                nameText.fontStyle = FontStyle.Normal;
                nameText.color = new Color(0.22f, 0.14f, 0.08f);
                bool me = p.Key == connection.LocalClientId, host = p.Key == connection.ServerClientId;
                if (tag != null) tag.text = host && me ? "host · sen" : host ? "host" : me ? "sen" : "";
            }
            else
            {
                nameText.text = connection.IsActive ? "Bekleniyor…" : "—";
                nameText.fontStyle = FontStyle.Italic;
                nameText.color = new Color(0.22f, 0.14f, 0.08f, 0.45f);
                if (tag != null) tag.text = "";
            }
            if (i < slotBands.Length && slotBands[i] != null)
            {
                var c = slotBands[i].color; c.a = has ? 0.28f : 0.12f; slotBands[i].color = c;
            }
        }
    }

    private string BuildPlayersText()
    {
        if (!connection.IsActive) return "";
        if (!connection.IsHost && !connection.IsClientConnected) return "";
        var list = connection.LobbyPlayers;
        var sb = new StringBuilder();
        sb.Append("Oyuncular: ").Append(list.Count).Append(" / ").Append(ConnectionManager.MaxPlayers);
        foreach (var p in list)
        {
            sb.Append('\n').Append(p.Value);
            bool me = p.Key == connection.LocalClientId, host = p.Key == connection.ServerClientId;
            if (host && me) sb.Append("  (host, sen)");
            else if (host) sb.Append("  (host)");
            else if (me) sb.Append("  (sen)");
        }
        return sb.ToString();
    }
}
