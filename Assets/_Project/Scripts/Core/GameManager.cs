using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum GameState : byte { Playing = 0, Failed = 1 }

/// <summary>
/// G-18: Bölüm durumu, fail koşulu ve checkpoint (GDD 13.2). Sahne içi NetworkObject olarak durur.
/// Fail: tüm oyuncular bayılmış veya ölmüşse (hiçbiri Alive değil) kısa bir süre sonra bölüm başarısız olur;
/// başarısızlık ekranından sonra herkes son checkpoint'te zehir 0 ile canlanır.
/// Win/bölüm sonu G-30/G-31'de eklenecek.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Tooltip("Son oyuncu bayıldıktan/öldükten sonra bölümün başarısız sayılması için beklenen süre (sn).")]
    [SerializeField] private float failGraceSeconds = 1.5f;
    [Tooltip("Başarısızlık ekranının gösterildiği süre (sn); sonra checkpoint'ten yeniden başlanır.")]
    [SerializeField] private float failScreenSeconds = 5f;

    private readonly NetworkVariable<GameState> state = new NetworkVariable<GameState>(
        GameState.Playing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> restartTime = new NetworkVariable<double>(
        0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> activeCheckpointId = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private double allDownSince = -1.0;

    public GameState State => state.Value;
    public int ActiveCheckpointId => activeCheckpointId.Value;

    /// <summary>Başarısızlık ekranında yeniden başlamaya kalan süre (sn).</summary>
    public float RestartRemaining
    {
        get
        {
            if (!IsSpawned || state.Value != GameState.Failed) return 0f;
            return Mathf.Max(0f, (float)(restartTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>Tüm kopyalarda: etkin checkpoint değişince (yeni kimlik).</summary>
    public event Action<int> ActiveCheckpointChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        activeCheckpointId.OnValueChanged += OnCheckpointChanged;
    }

    public override void OnNetworkDespawn()
    {
        activeCheckpointId.OnValueChanged -= OnCheckpointChanged;
        if (Instance == this) Instance = null;
    }

    private void OnCheckpointChanged(int oldId, int newId) { ActiveCheckpointChanged?.Invoke(newId); }

    /// <summary>Sunucu: etkin checkpoint'i ayarlar (Checkpoint yakınlık ile; totem G-20 de çağıracak).</summary>
    public void SetCheckpoint(Checkpoint checkpoint)
    {
        if (!IsServer || checkpoint == null) return;
        activeCheckpointId.Value = checkpoint.Id;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        double now = NetworkManager.ServerTime.Time;

        if (state.Value == GameState.Playing)
        {
            bool anyPlayer = false, anyAlive = false;
            foreach (PlayerLife p in PlayerLife.All)
            {
                if (p == null) continue;
                anyPlayer = true;
                if (p.State == PlayerLifeState.Alive) { anyAlive = true; break; }
            }

            if (anyPlayer && !anyAlive)
            {
                if (allDownSince < 0.0) allDownSince = now;
                else if (now - allDownSince >= failGraceSeconds)
                {
                    state.Value = GameState.Failed;
                    restartTime.Value = now + failScreenSeconds;
                    allDownSince = -1.0;
                }
            }
            else
            {
                allDownSince = -1.0;
            }
        }
        else if (state.Value == GameState.Failed && now >= restartTime.Value)
        {
            RestartFromCheckpoint();
        }
    }

    private void RestartFromCheckpoint()
    {
        var players = new List<PlayerLife>();
        foreach (PlayerLife p in PlayerLife.All) if (p != null) players.Add(p);
        players.Sort((x, y) => x.OwnerClientId.CompareTo(y.OwnerClientId));

        Checkpoint cp = Checkpoint.Find(activeCheckpointId.Value);
        PlayerSpawnPoint[] spawns = FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
        Array.Sort(spawns, (x, y) => x.Index.CompareTo(y.Index));

        for (int i = 0; i < players.Count; i++)
        {
            Vector3 pos;
            Quaternion rot;
            if (cp != null) cp.GetSpawnPose(i, out pos, out rot);
            else if (spawns.Length > 0)
            {
                Transform s = spawns[i % spawns.Length].transform;
                pos = s.position;
                rot = s.rotation;
            }
            else
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
            }
            players[i].ServerRespawn(pos, rot);
        }

        state.Value = GameState.Playing;
    }
}
