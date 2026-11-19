using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// G-09/G-10: LAN/IP bağlantı yöneticisi (Relay/Lobby yok). NetworkManager ile aynı objede durur.
/// Host: tüm arayüzlerde dinler. Client: verilen IP:port'a bağlanır. En fazla 4 oyuncu (host dahil).
/// G-10: Host StartGame ile oyun sahnesini yükler; sahne herkeste yüklenince oyuncular spawn noktalarında oluşturulur.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class ConnectionManager : MonoBehaviour
{
    public const int MaxPlayers = 4;

    [Header("Bağlantı")]
    [SerializeField] private string defaultAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;
    [Tooltip("Client bağlanma denemesi sayısı (her deneme ~1 sn). Dolunca bağlantı başarısız sayılır.")]
    [SerializeField, Min(1)] private int maxConnectAttempts = 5;

    [Header("Oyun")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private string gameSceneName = "Ch1_Island";

    private NetworkManager networkManager;
    private UnityTransport transport;
    private bool pendingShutdown;
    private bool gameStarted;

    public event Action<string> StatusChanged;

    public string Status { get; private set; } = "Bağlı değil";
    public string DefaultAddress => defaultAddress;
    public ushort DefaultPort => defaultPort;
    public bool IsActive => networkManager != null && networkManager.IsListening;
    public bool IsHost => IsActive && networkManager.IsHost;
    public bool IsClientOnly => IsActive && networkManager.IsClient && !networkManager.IsHost;
    public bool IsClientConnected => IsClientOnly && networkManager.IsConnectedClient;

    /// <summary>Sadece host/sunucuda bilinir.</summary>
    public IReadOnlyList<ulong> ConnectedClientIds => networkManager.IsServer ? networkManager.ConnectedClientsIds : null;
    public ulong LocalClientId => networkManager.LocalClientId;

    // ---------- Lobi oyuncu listesi (adlarla, herkeste) ----------
    private const string LobbyMessage = "LobbyNames";
    private readonly Dictionary<ulong, string> serverNames = new Dictionary<ulong, string>();
    private readonly List<KeyValuePair<ulong, string>> lobbyPlayers = new List<KeyValuePair<ulong, string>>();

    /// <summary>Herkes okuyabilir: lobideki oyuncular (id, ad), host ilk sırada.</summary>
    public IReadOnlyList<KeyValuePair<ulong, string>> LobbyPlayers => lobbyPlayers;
    public ulong ServerClientId => NetworkManager.ServerClientId;

    private static string CleanName(string n, ulong id)
    {
        n = (n ?? "").Trim();
        if (n.Length > PlayerIdentity.MaxLength) n = n.Substring(0, PlayerIdentity.MaxLength);
        return string.IsNullOrEmpty(n) ? "Oyuncu " + (id + 1) : n;
    }

    private void RegisterLobbyMessages()
    {
        if (networkManager.CustomMessagingManager == null) return;
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyMessage);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyMessage, OnLobbyMessage);
    }

    private string NameOf(ulong id) => serverNames.TryGetValue(id, out string n) ? n : "Oyuncu " + (id + 1);

    /// <summary>Sunucu: listeyi yeniden kurar ve tüm istemcilere yollar.</summary>
    private void BroadcastLobby()
    {
        if (!networkManager.IsServer) return;
        lobbyPlayers.Clear();
        foreach (ulong id in networkManager.ConnectedClientsIds) lobbyPlayers.Add(new KeyValuePair<ulong, string>(id, NameOf(id)));
        lobbyPlayers.Sort((a, b) => a.Key.CompareTo(b.Key));
        using (var w = new FastBufferWriter(1024, Unity.Collections.Allocator.Temp))
        {
            w.WriteValueSafe(lobbyPlayers.Count);
            foreach (var p in lobbyPlayers) { w.WriteValueSafe(p.Key); w.WriteValueSafe(p.Value); }
            networkManager.CustomMessagingManager.SendNamedMessageToAll(LobbyMessage, w);
        }
    }

    private void OnLobbyMessage(ulong sender, FastBufferReader reader)
    {
        if (networkManager.IsServer) return; // host listeyi kendisi kurar
        lobbyPlayers.Clear();
        reader.ReadValueSafe(out int count);
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong id);
            reader.ReadValueSafe(out string n);
            lobbyPlayers.Add(new KeyValuePair<ulong, string>(id, n));
        }
    }

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
    }

    private void OnEnable()
    {
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        networkManager.OnTransportFailure += OnTransportFailure;
        networkManager.OnServerStarted += OnServerStarted;
    }

    private void OnDisable()
    {
        if (networkManager == null) return;
        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        networkManager.OnTransportFailure -= OnTransportFailure;
        networkManager.OnServerStarted -= OnServerStarted;
    }

    private void Update()
    {
        if (gameStarted && !networkManager.IsServer) gameStarted = false;

        if (pendingShutdown && networkManager.IsListening)
        {
            pendingShutdown = false;
            networkManager.Shutdown();
        }
    }

    public void StartHost(ushort port)
    {
        if (IsActive) return;
        if (transport == null) { SetStatus("Hata: UnityTransport bulunamadı"); return; }

        Configure();
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        serverNames.Clear(); lobbyPlayers.Clear();
        bool ok = networkManager.StartHost();
        if (ok)
        {
            RegisterLobbyMessages();
            serverNames[networkManager.LocalClientId] = CleanName(PlayerIdentity.LocalName, networkManager.LocalClientId);
            BroadcastLobby();
        }
        SetStatus(ok ? "Lobi açık — arkadaşlarının katılmasını bekle (port " + port + ")" : "Host başlatılamadı (port dolu olabilir)");
    }

    public void StartClient(string address, ushort port)
    {
        if (IsActive) return;
        if (transport == null) { SetStatus("Hata: UnityTransport bulunamadı"); return; }

        Configure();
        transport.SetConnectionData(address, port);
        transport.MaxConnectAttempts = maxConnectAttempts;
        lobbyPlayers.Clear();
        // adımız bağlantı isteğiyle gider; host lobide herkese gösterir
        networkManager.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(PlayerIdentity.LocalName ?? "");
        bool ok = networkManager.StartClient();
        if (ok) RegisterLobbyMessages();
        SetStatus(ok ? "Bağlanılıyor: " + address + ":" + port + " ..." : "Client başlatılamadı");
    }

    public void Disconnect()
    {
        if (networkManager.IsListening) networkManager.Shutdown();
        serverNames.Clear(); lobbyPlayers.Clear();
        SetStatus("Bağlantı kesildi");
    }

    /// <summary>Sadece host: oyun sahnesini herkese yükletir. Oyuncular sahne yüklenince spawn olur.</summary>
    public void StartGame()
    {
        if (!networkManager.IsServer) { SetStatus("Oyunu sadece host başlatabilir"); return; }
        if (playerPrefab == null) { SetStatus("Hata: oyuncu prefab'ı atanmamış"); return; }

        var status = networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
            SetStatus("Sahne yüklenemedi (" + status + ")");
        else
        {
            gameStarted = true;
            loadStartedAt = Time.realtimeSinceStartup;
            SetStatus("Oyun başlatılıyor...");
        }
    }

    private void Configure()
    {
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = Approve;
    }

    private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        if (gameStarted)
        {
            response.Approved = false;
            response.Reason = "Oyun başladı, sonradan katılınamaz";
            return;
        }

        if (networkManager.ConnectedClientsIds.Count >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "Oyun dolu (en fazla " + MaxPlayers + " oyuncu)";
            return;
        }
        response.Approved = true;
        if (request.ClientNetworkId != networkManager.LocalClientId || !networkManager.IsHost)
        {
            string n = request.Payload != null && request.Payload.Length > 0 ? System.Text.Encoding.UTF8.GetString(request.Payload) : "";
            serverNames[request.ClientNetworkId] = CleanName(n, request.ClientNetworkId);
        }
        response.CreatePlayerObject = false; // oyuncular oyun sahnesi yüklenince SpawnPlayers ile oluşturulur
    }

    private void OnServerStarted()
    {
        networkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        HookLoadingScreen();
    }

    // ---------- Yükleme ekranı (lobi → oyun) ----------
    private NetworkSceneManager hookedSceneManager; // yeniden bağlanınca yeni örnek gelir
    private float loadStartedAt;
    private void HookLoadingScreen()
    {
        var sm = networkManager.SceneManager;
        if (sm == null || sm == hookedSceneManager) return;
        hookedSceneManager = sm;
        sm.OnSceneEvent += OnSceneEvent;
    }

    private void OnSceneEvent(SceneEvent e)
    {
        if (e.SceneName != gameSceneName) return;
        if (e.SceneEventType == SceneEventType.Load && e.ClientId == networkManager.LocalClientId) LoadingScreen.Show();
        else if (e.SceneEventType == SceneEventType.LoadEventCompleted) LoadingScreen.Hide();
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!networkManager.IsServer || sceneName != gameSceneName) return;
        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            Debug.LogWarning("[Ağ] Sahneyi zamanında yükleyemeyen oyuncu sayısı: " + clientsTimedOut.Count);
        SpawnPlayers(clientsCompleted);
        // sinematik, yükleme çizgisi dolup ekran açıldıktan sonra başlar (istemcilerin gecikmesi için küçük pay)
        float elapsed = Time.realtimeSinceStartup - loadStartedAt;
        float delay = Mathf.Max(0f, LoadingScreen.MinShowSeconds - elapsed) + LoadingScreen.HoldSeconds + LoadingScreen.FadeOutSeconds + 0.4f;
        // KULLANICI (2026-11-15): lobide Bölüm 2/3 seçildiyse sinematik oynatılmaz, bölüm geçişleri kendiliğinden yapılır
        if (ChapterSelect.StartChapter > 1) StartCoroutine(RunChapterSelect(ChapterSelect.StartChapter, delay));
        else if (IntroCinematic.Instance != null) IntroCinematic.Instance.ServerBegin(delay);
    }

    /// <summary>Sunucu: normal oyundaki geçişleri sırayla tetikler (Bölüm 1 sonu → Bölüm 2 → gerekirse Bölüm 2 sonu → Bölüm 3).
    /// Giriş sinematiğinin bekleme ekranı (siyah) açılmadan kararma başlar, böylece ada görünmeden Bölüm 2 başlığına geçilir.</summary>
    private IEnumerator RunChapterSelect(int chapter, float loadDelay)
    {
        Debug.Log("[Bölüm seçimi] Bölüm " + chapter + " seçildi, geçişler başlıyor");
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, loadDelay - 0.6f));   // yükleme ekranı kapanırken

        CollapseSequence collapse = null;
        float waited = 0f;
        while (collapse == null && waited < 10f)
        {
            collapse = FindFirstObjectByType<CollapseSequence>();
            if (collapse == null) { waited += Time.unscaledDeltaTime; yield return null; }
        }
        if (collapse == null || !collapse.ServerSkipToChapter2(chapter >= 3))
        {
            Debug.LogError("[Bölüm seçimi] Bölüm 1 bitirilemedi; oyun Bölüm 1'den devam ediyor");
            if (IntroCinematic.Instance != null) IntroCinematic.Instance.ServerBegin(0f);
            yield break;
        }
        yield return new WaitForSeconds(2.5f);                                        // kararma tamamlansın
        if (IntroCinematic.Instance != null) IntroCinematic.Instance.ServerSkip();      // siyah bekleme ekranının arkasında
        if (chapter < 3) yield break;

        float start = Time.time;
        while ((ChapterLoader.Instance == null || ChapterLoader.Instance.CurrentChapter == null) && Time.time - start < 90f) yield return null;
        yield return new WaitForSeconds(0.5f);
        ChapterEnd end = null;
        foreach (ChapterEnd e in FindObjectsByType<ChapterEnd>(FindObjectsSortMode.None))
            if (e != null && e.IsSpawned && e.LeadsToNextChapter) { end = e; break; }
        if (end != null) end.ServerEnd(true);
        else Debug.LogError("[Bölüm seçimi] Bölüm 2 çıkışı bulunamadı; oyun Bölüm 2'den devam ediyor");
    }

    private void SpawnPlayers(List<ulong> clients)
    {
        if (playerPrefab == null) { Debug.LogError("[Ağ] Oyuncu prefab'ı atanmamış"); return; }

        PlayerSpawnPoint[] points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
        Array.Sort(points, (x, y) => x.Index.CompareTo(y.Index));
        if (points.Length == 0) Debug.LogWarning("[Ağ] Sahnede PlayerSpawnPoint yok, oyuncular (0,0,0)'da doğacak.");

        int n = 0;
        foreach (ulong id in clients)
        {
            if (networkManager.ConnectedClients.TryGetValue(id, out NetworkClient c) && c.PlayerObject != null) continue;

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            if (points.Length > 0)
            {
                Transform p = points[n % points.Length].transform;
                pos = p.position;
                rot = p.rotation;
            }
            NetworkObject instance = Instantiate(playerPrefab, pos, rot);
            instance.SpawnAsPlayerObject(id, true);
            n++;
        }
        SetStatus("Oyun başladı - oyuncu sayısı: " + clients.Count);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (networkManager.IsHost)
        {
            BroadcastLobby();
            if (clientId != networkManager.LocalClientId) SetStatus(NameOf(clientId) + " katıldı");
        }
        else if (clientId == networkManager.LocalClientId)
        {
            SetStatus("Host'a bağlandın — oyunun başlatılması bekleniyor");
            HookLoadingScreen();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (networkManager.IsHost)
        {
            if (clientId != networkManager.LocalClientId)
            {
                string who = NameOf(clientId);
                serverNames.Remove(clientId);
                BroadcastLobby();
                SetStatus(who + " ayrıldı");
            }
            return;
        }

        if (clientId == networkManager.LocalClientId || !networkManager.IsConnectedClient)
        {
            string reason = networkManager.DisconnectReason;
            SetStatus(string.IsNullOrEmpty(reason) ? "Bağlantı kurulamadı veya kesildi" : "Bağlantı kesildi: " + reason);
            lobbyPlayers.Clear();
            pendingShutdown = true;
        }
    }

    private void OnTransportFailure()
    {
        SetStatus("Ağ hatası: bağlantı kurulamadı");
        pendingShutdown = true;
    }

    private void SetStatus(string text)
    {
        Status = text;
        StatusChanged?.Invoke(text);
    }

    /// <summary>Bu bilgisayarın yerel IPv4 adresleri (diğer oyuncular host'a bunlardan biriyle bağlanır).</summary>
    public static string GetLocalIPv4List()
    {
        try
        {
            var list = new List<string>();
            foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) list.Add(ip.ToString());
            return list.Count > 0 ? string.Join(", ", list) : "bulunamadı";
        }
        catch (Exception)
        {
            return "bulunamadı";
        }
    }
}
