using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// G-19: Mızrakla uzaktan basılan düğme (GDD 6.9 bulmaca taslağı: çiçek alanının ardındaki düğme).
/// Durum sunucuda tutulur ve herkese senkronlanır; basılınca/bırakılınca UnityEvent tetiklenir (kapı vb. bağlamak için, G-32).
/// Objede NetworkObject ve (tetikleyici olmayan) bir Collider bulunmalı.
/// </summary>
public class SpearButton : NetworkBehaviour, ISpearTarget
{
    [SerializeField] private Renderer visual;
    [SerializeField, ColorUsage(false, true)] private Color upEmission = new Color(1.5f, 0.2f, 0.12f);
    [SerializeField, ColorUsage(false, true)] private Color downEmission = new Color(0.3f, 2f, 0.5f);
    [Tooltip("Açıksa bir kez basılınca kalır; kapalıysa her vuruşta aç/kapa.")]
    [SerializeField] private bool latched = true;
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onReleased;

    private readonly NetworkVariable<bool> pressed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private MaterialPropertyBlock block;

    public bool IsPressed => pressed.Value;

    /// <summary>Tüm kopyalarda: düğme durumu değişince.</summary>
    public event Action<bool> StateChanged;

    public override void OnNetworkSpawn()
    {
        pressed.OnValueChanged += OnPressedChanged;
        ApplyVisual(pressed.Value);
    }

    public override void OnNetworkDespawn()
    {
        pressed.OnValueChanged -= OnPressedChanged;
    }

    public void OnSpearHit(ulong attackerClientId, Vector3 point, Vector3 direction)
    {
        if (!IsServer) return;
        pressed.Value = latched ? true : !pressed.Value;
    }

    private void OnPressedChanged(bool oldValue, bool newValue)
    {
        ApplyVisual(newValue);
        StateChanged?.Invoke(newValue);
        if (newValue) onPressed?.Invoke(); else onReleased?.Invoke();
    }

    private void ApplyVisual(bool isPressed)
    {
        if (visual == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        visual.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", isPressed ? downEmission : upEmission);
        visual.SetPropertyBlock(block);
    }
}
