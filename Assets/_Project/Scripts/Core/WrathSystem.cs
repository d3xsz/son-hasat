using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-22: Ada Öfkesi (PROJECT.md). Yalnızca sabit bir totem yerinden alınınca artar: her totem +20 (0/20/40/60/80/100).
/// Sunucu yazar, herkes okur. Hava, ses, tehdit ve yönlendirme sistemleri (G-23..G-27) Level / LevelReached ile bağlanır;
/// mağara tetikleyicisi (G-29) Wrath == 100'e bakar. Sahne içi NetworkObject olarak durur.
/// </summary>
public class WrathSystem : NetworkBehaviour
{
    public static WrathSystem Instance { get; private set; }

    public const int MaxWrath = 100;
    /// <summary>Seviye basamağı: Level = Wrath / 20 (0-5).</summary>
    public const int LevelStep = 20;

    [Tooltip("Her totem alınışında eklenen Öfke (PROJECT.md: +20).")]
    [SerializeField] private int wrathPerTotem = 20;

    private readonly NetworkVariable<int> wrath = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int Wrath => wrath.Value;
    /// <summary>0-5: 0 hafif rüzgar, 1 hafif yağmur, 2 güçlü yağmur..., 5 mağara tetikleyicisi aktif.</summary>
    public int Level => wrath.Value / LevelStep;
    public float Wrath01 => wrath.Value / (float)MaxWrath;

    /// <summary>Tüm kopyalarda: Öfke değeri değişince (yeni değer).</summary>
    public event Action<int> Changed;

    /// <summary>Tüm kopyalarda: Öfke yeni bir seviyeye çıkınca (yeni seviye 1-5).</summary>
    public event Action<int> LevelReached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        wrath.OnValueChanged += OnWrathChanged;
        if (IsServer) Totem.TotemTaken += OnTotemTaken;
    }

    public override void OnNetworkDespawn()
    {
        wrath.OnValueChanged -= OnWrathChanged;
        Totem.TotemTaken -= OnTotemTaken;
        if (Instance == this) Instance = null;
    }

    private void OnTotemTaken(Totem totem) { ServerAdd(wrathPerTotem); }

    /// <summary>Sunucu: Öfke ekler (0-100 arasına sıkıştırılır).</summary>
    public void ServerAdd(int amount)
    {
        if (!IsServer || amount <= 0) return;
        wrath.Value = Mathf.Clamp(wrath.Value + amount, 0, MaxWrath);
    }

    /// <summary>Sunucu: Öfkeyi doğrudan ayarlar (test/bölüm sıfırlama için).</summary>
    public void ServerSet(int value)
    {
        if (!IsServer) return;
        wrath.Value = Mathf.Clamp(value, 0, MaxWrath);
    }

    private void OnWrathChanged(int oldValue, int newValue)
    {
        Changed?.Invoke(newValue);
        int oldLevel = oldValue / LevelStep;
        int newLevel = newValue / LevelStep;
        if (newLevel > oldLevel) LevelReached?.Invoke(newLevel);
    }
}
