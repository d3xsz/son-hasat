using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-11: Fener durumunun ağ senkronu. Fener mantığı (FlashlightController) yalnızca sahipte çalışır;
/// sahip burada açık/kapalı ve ısıyı NetworkVariable'lara yazar, diğer kopyalar ışığı buna göre ayarlar.
/// Bu bileşen sahibi olmayan kopyalarda da AÇIK kalmalıdır (ownerOnlyBehaviours listesinde değil).
/// </summary>
[RequireComponent(typeof(FlashlightController))]
public class FlashlightNetworkSync : NetworkBehaviour
{
    [Tooltip("Isı bu kadar değişince ağa yazılır (0-1). Işık rengi için yeterince ince.")]
    [SerializeField] private float heatSendThreshold = 0.01f;

    private readonly NetworkVariable<bool> isOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> heat01 = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // B3-03: boyut ışığı modu (0 normal, 1 büyüten, 2 küçülten)
    private readonly NetworkVariable<byte> mode = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private FlashlightController flashlight;

    /// <summary>Herkes okuyabilir: fenerin boyut ışığı modu (sunucu boyutu değişen nesnelerde kullanır).</summary>
    public int Mode => mode.Value;

    /// <summary>Herkes okuyabilir: fener açık mı? (yaratıklar ışıktan kaçarken sunucuda kullanır)</summary>
    public bool IsOn => isOn.Value;

    private void Awake()
    {
        flashlight = GetComponent<FlashlightController>();
    }

    private bool lastOn;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) flashlight.ApplyRemoteState(isOn.Value, heat01.Value, mode.Value);
        lastOn = IsOwner ? flashlight.IsOn : isOn.Value;
    }

    private void Update()
    {
        if (!IsSpawned) return;

        // KULLANICI (cila): fener açılıp kapanınca "tık" (sahip ve diğer oyuncular duyar)
        bool on = IsOwner ? flashlight.IsOn : isOn.Value;
        if (on != lastOn)
        {
            lastOn = on;
            SfxLibrary lib = Sfx.Lib;
            if (lib != null && lib.flashlightClick != null && lib.flashlightClick.Length > 0)
                Sfx.At(new[] { lib.flashlightClick[on ? 0 : lib.flashlightClick.Length - 1] }, transform.position + Vector3.up * 1.3f, IsOwner ? 0.35f : 0.45f, on ? 1.05f : 0.92f, 0.03f, 1f, 10f);
        }

        if (IsOwner)
        {
            if (isOn.Value != flashlight.IsOn) isOn.Value = flashlight.IsOn;
            float h = flashlight.Heat01;
            if (Mathf.Abs(heat01.Value - h) > heatSendThreshold) heat01.Value = h;
            if (mode.Value != flashlight.Mode)
            {
                mode.Value = (byte)flashlight.Mode;
                SfxLibrary l2 = Sfx.Lib;   // mod değişince ince tık
                if (l2 != null && l2.flashlightClick != null && l2.flashlightClick.Length > 0)
                    Sfx.At(new[] { l2.flashlightClick[0] }, transform.position + Vector3.up * 1.3f, 0.3f, 1.5f + 0.25f * flashlight.Mode, 0.02f, 1f, 10f);
            }
        }
        else
        {
            flashlight.ApplyRemoteState(isOn.Value, heat01.Value, mode.Value);
        }
    }
}
