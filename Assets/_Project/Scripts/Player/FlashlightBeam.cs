using UnityEngine;

/// <summary>
/// V-01 (görsel cila): Fener huzmesini siste görünür kılar (Goblin/LightBeam). Spot ışığın üstünde durur;
/// ışık açıkken koni görünür, rengi/şiddeti ışığı izler (ısınınca turuncuya kayar, sönünce kaybolur).
/// Yalnızca görsel: ağ, oynanış ve ışık ayarlarına dokunmaz. Kaldırmak için bu bileşeni silmek yeterli.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlashlightBeam : MonoBehaviour
{
    [SerializeField] private Material beamMaterial;
    [Tooltip("Huzmenin görünen boyu (m). Sis zaten ~15 m'den sonrasını yutar.")]
    [SerializeField] private float length = 16f;
    [Tooltip("Işığın bu şiddetinde huzme tam parlaklıkta olur.")]
    [SerializeField] private float referenceIntensity = 160f;
    [SerializeField, Range(0f, 3f)] private float brightness = 1f;
    [Tooltip("Kendi fenerinde huzme daha soluk olur (görüşü kapatmasın).")]
    [SerializeField, Range(0f, 1f)] private float ownerBrightness = 0.45f;

    private static Mesh coneMesh;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Light spot;
    private Transform beam;
    private MeshRenderer beamRenderer;
    private MaterialPropertyBlock block;
    private Unity.Netcode.NetworkObject netObj;

    private void Awake()
    {
        spot = GetComponent<Light>();
        netObj = GetComponentInParent<Unity.Netcode.NetworkObject>();
        block = new MaterialPropertyBlock();
        if (beamMaterial == null) { enabled = false; return; }

        GameObject go = new GameObject("Beam");
        go.layer = gameObject.layer;
        beam = go.transform;
        beam.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = GetConeMesh();
        beamRenderer = go.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = beamMaterial;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        beamRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        beamRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        beamRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        if (beamRenderer == null) return;
        bool on = spot.enabled && spot.gameObject.activeInHierarchy && spot.intensity > 0.5f;
        if (beamRenderer.enabled != on) beamRenderer.enabled = on;
        if (!on) return;

        float radius = length * Mathf.Tan(spot.spotAngle * 0.5f * Mathf.Deg2Rad);
        beam.localScale = new Vector3(radius, radius, length);

        bool own = netObj == null || !netObj.IsSpawned || netObj.IsOwner;
        float k = Mathf.Clamp(spot.intensity / Mathf.Max(1f, referenceIntensity), 0f, 1.5f) * brightness * (own ? ownerBrightness : 1f);
        Color c = spot.color * k;
        beamRenderer.GetPropertyBlock(block);
        block.SetColor(ColorId, c);
        beamRenderer.SetPropertyBlock(block);
    }

    /// <summary>Birim koni: tepe orijinde, eksen +Z, z=1'de yarıçap 1 (kapaklı).</summary>
    private static Mesh GetConeMesh()
    {
        if (coneMesh != null) return coneMesh;
        const int seg = 24;
        Vector3[] v = new Vector3[seg + 2];
        int[] tris = new int[seg * 6];
        v[0] = Vector3.zero;
        v[seg + 1] = new Vector3(0f, 0f, 1f);
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f);
        }
        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;
            tris[i * 6] = 0; tris[i * 6 + 1] = n + 1; tris[i * 6 + 2] = i + 1;         // yan yüz (dışa bakar)
            tris[i * 6 + 3] = seg + 1; tris[i * 6 + 4] = i + 1; tris[i * 6 + 5] = n + 1; // kapak (dışa bakar)
        }
        coneMesh = new Mesh { name = "LightBeamCone", hideFlags = HideFlags.HideAndDontSave };
        coneMesh.vertices = v;
        coneMesh.triangles = tris;
        coneMesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2f, 2f, 1f));
        return coneMesh;
    }
}
