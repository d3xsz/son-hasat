using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerLifeState : byte { Alive = 0, Fainted = 1, Dead = 2 }

/// <summary>
/// G-17: Oyuncunun yaşam durumu (GDD 5.4). Zehir dolunca bayılır; 45 sn içinde kaldırılmazsa ölür ve izleyici olur.
/// Durum sunucuda yönetilir ve herkese senkronlanır. Bayılma pozu (placeholder ragdoll) her kopyada yerel olarak çizilir, ağda gönderilmez.
/// Bu bileşen sahibi olmayan kopyalarda da AÇIK kalmalıdır (ownerOnlyBehaviours listesinde değil).
/// </summary>
public class PlayerLife : NetworkBehaviour, IHoldInteractable
{
    /// <summary>Sahnedeki tüm oyuncuların yaşam bileşenleri (izleyici modu ve bölüm başarısızlığı kontrolü için).</summary>
    public static readonly List<PlayerLife> All = new List<PlayerLife>();

    [Header("Süreler (GDD 5.4)")]
    [Tooltip("Bayılma süresi; dolunca ölüm.")]
    [SerializeField] private float faintDuration = 45f;
    [Tooltip("Bayılan oyuncuyu kaldırmak için E'nin basılı tutulma süresi. GDD'de sayı yok: geçici varsayım.")]
    [SerializeField] private float reviveHoldSeconds = 3f;
    [Tooltip("Kaldırılınca zehir değeri (GDD: %50).")]
    [SerializeField, Range(0f, 100f)] private float revivePoison = 50f;
    [SerializeField] private float maxReviveDistance = 4f;
    [Tooltip("Ölü oyuncuyu güvenli alanda diriltmek için E'nin basılı tutulma süresi (GDD: ~5 sn).")]
    [SerializeField] private float reviveDeadHoldSeconds = 5f;

    [Header("Referanslar")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Transform bodyVisual;
    [Tooltip("Bayılan/ölü oyuncuya bakıp etkileşebilmek için kutu collider (yalnızca sahibi olmayan kopyada açılır).")]
    [SerializeField] private Collider reviveCollider;

    [Header("Bayılma pozu (placeholder ragdoll)")]
    [SerializeField] private Vector3 faintedPivotLocalPosition = new Vector3(0f, 0.35f, 0.6f);
    [SerializeField] private float faintedRoll = 75f;
    [SerializeField] private float poseSpeed = 4f;

    private readonly NetworkVariable<PlayerLifeState> state = new NetworkVariable<PlayerLifeState>(
        PlayerLifeState.Alive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> faintEndTime = new NetworkVariable<double>(
        0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PoisonSystem poison;
    private PlayerController controller;
    private NetworkPlayer netPlayer;
    private float poseBlend;
    private Vector3 pivotStandPos;
    private Vector3 bodyStandPos;
    private Quaternion bodyStandRot;
    private int spectateIndex;
    private PlayerLife spectated;

    public PlayerLifeState State => state.Value;
    public Transform CameraPivot => cameraPivot;

    /// <summary>C-03: yerel kameraya eklenen konum ofseti (baş sallanması / iniş çökmesi). PlayerFeel yazar.</summary>
    public Vector3 PivotOffset { get; set; }

    /// <summary>Eğilme: kamera/baş bu kadar aşağı iner (m). NetworkPlayer yazar (her kopyada).</summary>
    public float CrouchDrop { get; set; }
    public bool CanAct => state.Value == PlayerLifeState.Alive;

    public float FaintRemaining
    {
        get
        {
            if (!IsSpawned || state.Value != PlayerLifeState.Fainted) return 0f;
            return Mathf.Max(0f, (float)(faintEndTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>Tüm kopyalarda: yaşam durumu değişince.</summary>
    public event Action<PlayerLifeState> StateChanged;

    /// <summary>G-29: gövde görseli (kayma sekansında uzak kopyada görsel 'ragdoll' pozu için).</summary>
    public Transform BodyVisual => bodyVisual;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { All.Clear(); }

    private void Awake()
    {
        poison = GetComponent<PoisonSystem>();
        controller = GetComponent<PlayerController>();
        netPlayer = GetComponent<NetworkPlayer>();
        if (cameraPivot != null) pivotStandPos = cameraPivot.localPosition;
        if (bodyVisual != null)
        {
            bodyStandPos = bodyVisual.localPosition;
            bodyStandRot = bodyVisual.localRotation;
        }
        if (reviveCollider != null) reviveCollider.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this)) All.Add(this);
        state.OnValueChanged += OnStateChanged;
        if (IsServer) poison.Filled += OnPoisonFilled;
        ApplyStateEffects(state.Value);
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= OnStateChanged;
        if (poison != null) poison.Filled -= OnPoisonFilled;
        StopSpectating();
        All.Remove(this);
    }

    private void OnStateChanged(PlayerLifeState oldState, PlayerLifeState newState)
    {
        ApplyStateEffects(newState);
        StateChanged?.Invoke(newState);
    }

    private void ApplyStateEffects(PlayerLifeState s)
    {
        if (reviveCollider != null)
            reviveCollider.enabled = !IsOwner && s != PlayerLifeState.Alive;
        if (IsOwner && s == PlayerLifeState.Alive) StopSpectating();
    }

    // ---------- Sunucu ----------

    private void OnPoisonFilled()
    {
        if (!IsServer || state.Value != PlayerLifeState.Alive) return;
        faintEndTime.Value = NetworkManager.ServerTime.Time + faintDuration;
        state.Value = PlayerLifeState.Fainted;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || state.Value != PlayerLifeState.Fainted) return;
        if (NetworkManager.ServerTime.Time >= faintEndTime.Value) state.Value = PlayerLifeState.Dead;
    }

    /// <summary>Sunucu: oyuncuyu ayağa kaldırır (bayılandan kaldırma; ileride totemde ölüden diriltme, G-21).</summary>
    public void ServerRevive(float poisonAfter)
    {
        if (!IsServer || state.Value == PlayerLifeState.Alive) return;
        poison.SetPoison(poisonAfter);
        state.Value = PlayerLifeState.Alive;
    }

    /// <summary>Sunucu: oyuncuyu verilen noktada, zehir 0 ile canlandırır (G-18 bölüm yeniden başlatma).</summary>
    public void ServerRespawn(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        poison.SetPoison(0f);
        state.Value = PlayerLifeState.Alive;
        TeleportOwnerRpc(position, rotation);
    }

    /// <summary>Sunucu: oyuncuyu yalnızca taşır (durum ve zehir değişmez). B3-05 ev kapıları kullanır.</summary>
    public void ServerTeleport(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        TeleportOwnerRpc(position, rotation);
    }

    /// <summary>Sunucu: oyuncu doğrudan ölür (B2-08 uçuruma düşme). Ölü oyuncu izleyici olur (zehirden ölümle aynı).</summary>
    public void ServerKill()
    {
        if (!IsServer || state.Value == PlayerLifeState.Dead) return;
        state.Value = PlayerLifeState.Dead;
    }

    /// <summary>Sahip tarafında ışınlanma (NetworkTransform sahip yetkili olduğu için sunucu doğrudan taşıyamaz).</summary>
    [Rpc(SendTo.Owner)]
    private void TeleportOwnerRpc(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        bool wasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = wasEnabled;
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.Teleport(position, rotation, transform.localScale);
    }

    // ---------- Kaldırma etkileşimi (E basılı tut) ----------

    public string InteractionPrompt => state.Value == PlayerLifeState.Dead
        ? "Diriltmek için basılı tut: Oyuncu " + OwnerClientId + " (güvenli alan)"
        : "Kaldır: Oyuncu " + OwnerClientId + "  (" + Mathf.CeilToInt(FaintRemaining) + " sn)";

    public float HoldDuration => state.Value == PlayerLifeState.Dead ? reviveDeadHoldSeconds : reviveHoldSeconds;

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (IsOwner || interactor == null) return false;
        if (!interactor.TryGetComponent(out PlayerLife me) || !me.CanAct) return false;
        PlayerLifeState s = state.Value;
        if (s == PlayerLifeState.Fainted) return true;
        // G-21: ölü oyuncu yalnızca bir güvenli alanın (sabit totem / taşınan veya yerdeki totem) içindeyken diriltilebilir
        return s == PlayerLifeState.Dead && SafeZones.IsInside(transform.position);
    }

    public void Interact(PlayerInteractor interactor)
    {
        if (!IsSpawned || state.Value == PlayerLifeState.Alive) return;
        ReviveRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReviveRpc(RpcParams rpcParams = default)
    {
        PlayerLifeState current = state.Value;
        if (current == PlayerLifeState.Alive) return;
        if (current == PlayerLifeState.Dead && !SafeZones.IsInside(transform.position)) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender == OwnerClientId) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null) return;
        PlayerLife reviver = client.PlayerObject.GetComponent<PlayerLife>();
        if (reviver == null || !reviver.CanAct) return;
        if (Vector3.Distance(reviver.transform.position, transform.position) > maxReviveDistance) return;
        ServerRevive(revivePoison);
    }

    // ---------- Görsel poz ve izleyici modu (yerel) ----------

    private void LateUpdate()
    {
        if (!IsSpawned) return;

        float target = state.Value == PlayerLifeState.Alive ? 0f : 1f;
        poseBlend = Mathf.MoveTowards(poseBlend, target, poseSpeed * Time.deltaTime);

        if (IsOwner)
        {
            if (state.Value == PlayerLifeState.Dead)
            {
                UpdateSpectate();
                return;
            }
            if (cameraPivot != null) cameraPivot.localPosition = Vector3.Lerp(pivotStandPos + Vector3.down * CrouchDrop, faintedPivotLocalPosition, poseBlend) + PivotOffset * (1f - poseBlend);
            if (controller != null) controller.ExtraRoll = faintedRoll * poseBlend;
        }
        else
        {
            if (bodyVisual != null)
            {
                bodyVisual.localRotation = Quaternion.Slerp(bodyStandRot, Quaternion.Euler(90f, 0f, 0f), poseBlend);
                bodyVisual.localPosition = Vector3.Lerp(bodyStandPos, new Vector3(0f, 0.3f, 0f), poseBlend);
            }
            if (cameraPivot != null) cameraPivot.localPosition = Vector3.Lerp(pivotStandPos + Vector3.down * CrouchDrop, faintedPivotLocalPosition, poseBlend);
        }
    }

    private void UpdateSpectate()
    {
        if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            spectateIndex++;

        PlayerLife target = PickSpectateTarget();
        if (target != spectated)
        {
            if (spectated != null) spectated.SetSpectatedVisual(false);
            spectated = target;
            if (spectated != null) spectated.SetSpectatedVisual(true);
        }
        if (spectated != null && spectated.cameraPivot != null && cameraPivot != null)
            cameraPivot.SetPositionAndRotation(spectated.cameraPivot.position, spectated.cameraPivot.rotation);
    }

    private PlayerLife PickSpectateTarget()
    {
        int count = 0;
        foreach (PlayerLife other in All)
            if (other != this && other != null && other.state.Value != PlayerLifeState.Dead) count++;
        if (count == 0) return null;

        int want = ((spectateIndex % count) + count) % count;
        int k = 0;
        foreach (PlayerLife other in All)
        {
            if (other == this || other == null || other.state.Value == PlayerLifeState.Dead) continue;
            if (k == want) return other;
            k++;
        }
        return null;
    }

    private void StopSpectating()
    {
        if (spectated != null) spectated.SetSpectatedVisual(false);
        spectated = null;
    }

    private void SetSpectatedVisual(bool value)
    {
        if (netPlayer != null) netPlayer.SetSpectated(value);
    }
}
