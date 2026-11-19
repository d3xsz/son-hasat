using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-14: Oyuncunun tek zehir değeri (0-100, GDD 5.4). Sunucu yazar, herkes okur.
/// Kaynaklar (çiçek/bölge: PoisonZone G-15, yaratık G-26) ve tedavi (totem G-20, iksir) sunucu tarafında
/// AddPoison/RemovePoison çağırır. Bayılma/ölüm G-17'de bu sınıfın Filled olayını dinleyecek.
/// </summary>
public class PoisonSystem : NetworkBehaviour
{
    public const float MaxPoison = 100f;

    public enum PoisonStage { None = 0, Stage1 = 1, Stage2 = 2, Stage3 = 3, Fainted = 4 }

    [Header("Evre eşikleri (GDD 6.4'te sayı yok; kullanıcı onaylı: 25/50/75/100, denge testinde ayarlanır)")]
    [SerializeField] private float stage1Threshold = 25f;
    [SerializeField] private float stage2Threshold = 50f;
    [SerializeField] private float stage3Threshold = 75f;

    [Header("Evre etkileri (GDD 6.4)")]
    [Tooltip("Evre 2 ve 3'te hareket hızı çarpanı (GDD: %20 yavaş).")]
    [SerializeField, Range(0.3f, 1f)] private float slowMultiplier = 0.8f;
    [Tooltip("Evre 3'te zehrin kendiliğinden artış hızı (saniyede). GDD 'sürekli hasar'; kullanıcı onaylı yorum: tek değer yavaşça artar.")]
    [SerializeField] private float stage3DriftPerSecond = 0.5f;

    // G-20: totem güvenli alanı korumasi (sunucu yazar, herkes okur: HUD "GÜVENLİ BÖLGE")
    private readonly NetworkVariable<bool> safeZone = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private float protectedUntil;

    private readonly NetworkVariable<float> poison = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float Value => poison.Value;
    public float Stage1Threshold => stage1Threshold;
    public float Stage2Threshold => stage2Threshold;
    public float Stage3Threshold => stage3Threshold;
    public float Value01 => poison.Value / MaxPoison;
    public bool IsFull => poison.Value >= MaxPoison;

    public PoisonStage Stage
    {
        get
        {
            float v = poison.Value;
            if (v >= MaxPoison) return PoisonStage.Fainted;
            if (v >= stage3Threshold) return PoisonStage.Stage3;
            if (v >= stage2Threshold) return PoisonStage.Stage2;
            if (v >= stage1Threshold) return PoisonStage.Stage1;
            return PoisonStage.None;
        }
    }

    /// <summary>Hareket hızı çarpanı: evre 2 ve 3'te yavaşlar (GDD 6.4), diğer evrelerde 1.</summary>
    public float SpeedMultiplier
    {
        get
        {
            PoisonStage s = Stage;
            return (s == PoisonStage.Stage2 || s == PoisonStage.Stage3) ? slowMultiplier : 1f;
        }
    }

    /// <summary>Tüm kopyalarda: zehir değeri değişince (yeni değer).</summary>
    public event Action<float> ValueChanged;

    /// <summary>Tüm kopyalarda: zehir 100'e ulaşınca (bayılma tetikleyicisi, G-17).</summary>
    public event Action Filled;

    public override void OnNetworkSpawn()
    {
        poison.OnValueChanged += OnPoisonChanged;
    }

    public override void OnNetworkDespawn()
    {
        poison.OnValueChanged -= OnPoisonChanged;
    }

    private void OnPoisonChanged(float oldValue, float newValue)
    {
        ValueChanged?.Invoke(newValue);
        if (oldValue < MaxPoison && newValue >= MaxPoison) Filled?.Invoke();
    }

    /// <summary>Tüm kopyalarda: oyuncu bir totemin güvenli alanında mı?</summary>
    public bool InSafeZone => safeZone.Value;

    private bool IsProtected => Time.time < protectedUntil;

    /// <summary>Sunucu: güvenli alan (totem) her karede çağırır; kısa süre zehir eklenmesini engeller.</summary>
    public void ServerProtect(float duration = 0.25f)
    {
        if (!IsServer) return;
        protectedUntil = Time.time + duration;
        if (!safeZone.Value) safeZone.Value = true;
    }

    // Evre 3: zehir kendiliğinden yavaşça artar (yalnızca sunucuda)
    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (safeZone.Value && !IsProtected) safeZone.Value = false;
        if (Stage == PoisonStage.Stage3) AddPoison(stage3DriftPerSecond * Time.deltaTime);
    }

    // ---------- Sunucu API ----------

    public void AddPoison(float amount)
    {
        if (!IsServer || amount <= 0f || IsProtected) return;
        SetInternal(poison.Value + amount);
    }

    /// <summary>M-12: fiziksel darbe (Golem yumruğu) — totem güvenli alanı engellemez (totem hastalığa karşı korur, yumruğa değil).</summary>
    public void AddPoisonIgnoringProtection(float amount)
    {
        if (!IsServer || amount <= 0f) return;
        SetInternal(poison.Value + amount);
    }

    public void RemovePoison(float amount)
    {
        if (!IsServer || amount <= 0f) return;
        SetInternal(poison.Value - amount);
    }

    public void SetPoison(float value)
    {
        if (!IsServer) return;
        SetInternal(value);
    }

    private void SetInternal(float value)
    {
        poison.Value = Mathf.Clamp(value, 0f, MaxPoison);
    }
}
