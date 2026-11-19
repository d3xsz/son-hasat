using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-18: Checkpoint. Sunucuda, yakınına hayatta bir oyuncu gelince etkin checkpoint olur (GameManager.SetCheckpoint).
/// Herkeste görsel: etkin değilken sönük, etkinken turuncu parlar. Bölüm başarısız olunca oyuncular burada canlanır.
/// Totemler (G-20) bu bileşeni kullanacak.
/// </summary>
public class Checkpoint : MonoBehaviour
{
    public static readonly List<Checkpoint> All = new List<Checkpoint>();

    [Tooltip("Sahnede benzersiz kimlik.")]
    [SerializeField] private int id;
    [SerializeField] private float activationRadius = 3f;
    [Tooltip("Canlanma noktalarının checkpoint'ten ileriye kayması (m). 0'dan büyükse oyuncular merkeze bakar (örn. totem).")]
    [SerializeField] private float spawnForwardOffset = 0f;

    [Header("Görsel")]
    [SerializeField] private Renderer beaconRenderer;
    [SerializeField, ColorUsage(false, true)] private Color inactiveEmission = new Color(0.05f, 0.08f, 0.15f);
    [SerializeField, ColorUsage(false, true)] private Color activeEmission = new Color(1f, 0.5f, 0.1f);
    [SerializeField] private float activeIntensity = 2.5f;

    private MaterialPropertyBlock block;
    private float blend;

    public int Id => id;

    /// <summary>G-20: false ise yakınlık ile etkinleşmez (örn. sönmüş totem); kaydı ve canlanma noktası olarak geçerliliği sürer.</summary>
    public bool CanActivate { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { All.Clear(); }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        block = new MaterialPropertyBlock();
    }

    private void OnDisable() { All.Remove(this); }

    public static Checkpoint Find(int checkpointId)
    {
        foreach (Checkpoint c in All) if (c != null && c.id == checkpointId) return c;
        return null;
    }

    /// <summary>Oyuncu sırasına göre (0-3) bu checkpoint çevresindeki canlanma noktası.</summary>
    public void GetSpawnPose(int index, out Vector3 position, out Quaternion rotation)
    {
        position = transform.position + transform.forward * spawnForwardOffset + transform.right * ((index - 1.5f) * 1.2f);
        rotation = spawnForwardOffset > 0.01f ? Quaternion.LookRotation(-transform.forward, Vector3.up) : transform.rotation;
    }

    private void Update()
    {
        TryActivateOnServer();
        UpdateVisual();
    }

    private void TryActivateOnServer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        GameManager gm = GameManager.Instance;
        if (!CanActivate || nm == null || !nm.IsServer || gm == null || gm.ActiveCheckpointId == id) return;

        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            Vector3 d = p.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 8f) continue; // Bölüm 2 (aynı sahne grubunda, ~300 m aşağıda) Bölüm 1 checkpoint'lerini tetiklemesin
            d.y = 0f;
            if (d.sqrMagnitude <= activationRadius * activationRadius)
            {
                gm.SetCheckpoint(this);
                return;
            }
        }
    }

    private void UpdateVisual()
    {
        if (beaconRenderer == null) return;
        GameManager gm = GameManager.Instance;
        bool active = gm != null && gm.ActiveCheckpointId == id;
        blend = Mathf.MoveTowards(blend, active ? 1f : 0f, 2f * Time.deltaTime);
        Color c = Color.Lerp(inactiveEmission, activeEmission * activeIntensity, blend);
        beaconRenderer.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", c);
        beaconRenderer.SetPropertyBlock(block);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, activationRadius);
    }
}
