using UnityEngine;

/// <summary>
/// M-03: El feneri modeli. Yalnızca envanterde fener seçiliyken görünür (ışık da yalnızca o zaman açılır —
/// FlashlightController). Sahip birinci şahıs görünümünü (kamera altı, sol alt), diğer oyuncular sol eldeki feneri görür:
/// fener başparmak ile işaret parmağı arasında durur ve bakış yönüne (CameraPivot) döner. Diğer oyuncuların ekranında
/// ışık huzmesi de fenerin ucundan çıkar ve lens parlar. Seçili eşya ağda senkron (InventoryNetworkSync.SelectedItemId).
/// Ölüyken / ağır eşya taşırken gizli.
/// </summary>
[DefaultExecutionOrder(210)] // Animator ve kol pozlarından sonra
public class FlashlightVisual : MonoBehaviour
{
    [SerializeField] private GameObject viewVisual;
    [SerializeField] private GameObject bodyVisual;
    [SerializeField] private Transform hand;
    [SerializeField] private Transform cameraPivot;
    [Tooltip("Tutuş noktası bulunamazsa elin yerel uzayında kullanılacak nokta.")]
    [SerializeField] private Vector3 handOffset = new Vector3(0f, 0.08f, 0.03f);
    [SerializeField] private ItemData flashlightItem;

    [Header("Işık / lens")]
    [SerializeField] private Light spotLight;
    [Tooltip("Lens parıltıları (görünüm + eldeki); fener açıkken yanar.")]
    [SerializeField] private Renderer[] lensGlows = new Renderer[0];
    [Tooltip("Eldeki fenerin lens ucu: diğer oyuncularda ışık buradan çıkar.")]
    [SerializeField] private Transform bodyLens;

    private InventorySystem inventory;
    private InventoryNetworkSync net;
    private Unity.Netcode.NetworkObject netObj;
    private PlayerLife life;
    private SkinnedMeshRenderer bodyRenderer;
    private Transform thumb, index;
    private Vector3 lightLocalPos;
    private Quaternion lightLocalRot;
    private MaterialPropertyBlock block;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
        net = GetComponent<InventoryNetworkSync>();
        netObj = GetComponent<Unity.Netcode.NetworkObject>();
        life = GetComponent<PlayerLife>();
        bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        block = new MaterialPropertyBlock();
        if (spotLight != null) { lightLocalPos = spotLight.transform.localPosition; lightLocalRot = spotLight.transform.localRotation; }
        ResolveHand();
    }

    /// <summary>Görünen gövdenin (SkinnedMeshRenderer) iskeletindeki sol eli, başparmağı ve işaret parmağını bulur.</summary>
    private void ResolveHand()
    {
        if (hand == null || !hand.gameObject.activeInHierarchy)
        {
            hand = null;
            if (bodyRenderer != null)
                foreach (Transform b in bodyRenderer.bones)
                    if (b != null && b.name.EndsWith(":LeftHand")) { hand = b; break; }
            if (hand == null)
                foreach (Transform t in GetComponentsInChildren<Transform>())
                    if (t.name.EndsWith(":LeftHand")) { hand = t; break; }
        }
        if (hand != null && (thumb == null || index == null))
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("LeftHandThumb2")) thumb = t;
                else if (t.name.EndsWith("LeftHandIndex1")) index = t;
            }
    }

    private bool IsLocal => netObj == null || !netObj.IsSpawned || netObj.IsOwner;

    private bool Selected()
    {
        if (flashlightItem == null) return true;
        if (IsLocal) return inventory != null && inventory.SelectedItem == flashlightItem;
        return net != null && net.IsSpawned && net.ItemOf(net.SelectedItemId) == flashlightItem;
    }

    private void LateUpdate()
    {
        bool alive = life == null || life.State != PlayerLifeState.Dead;
        bool heavy = net != null && net.IsSpawned && net.CarriesHeavy();
        bool show = alive && !heavy && Selected();
        bool local = IsLocal;
        if (viewVisual != null && viewVisual.activeSelf != (show && local)) viewVisual.SetActive(show && local);

        bool b = bodyVisual != null && show && !local && (bodyRenderer == null || bodyRenderer.enabled);
        if (b) ResolveHand();
        if (b && hand == null) b = false;
        if (bodyVisual != null && bodyVisual.activeSelf != b) bodyVisual.SetActive(b);
        Quaternion look = cameraPivot != null ? cameraPivot.rotation : transform.rotation;
        if (b)
        {
            // başparmak ile işaret parmağının arası (yoksa elin yerel noktası)
            Vector3 grip = thumb != null && index != null ? Vector3.Lerp(thumb.position, index.position, 0.5f) : hand.TransformPoint(handOffset);
            bodyVisual.transform.rotation = look;
            bodyVisual.transform.position = grip;
        }

        // diğer oyuncuların ekranında huzme fenerin ucundan çıkar; sahipte kamera altındaki yerinde kalır
        if (spotLight != null)
        {
            if (b && bodyLens != null) { spotLight.transform.SetPositionAndRotation(bodyLens.position, look); }
            else { spotLight.transform.localPosition = lightLocalPos; spotLight.transform.localRotation = lightLocalRot; }
        }

        // lens parıltısı: fener açıkken ışığın renginde
        bool lit = spotLight != null && spotLight.enabled;
        foreach (Renderer r in lensGlows)
        {
            if (r == null) continue;
            if (r.enabled != lit) r.enabled = lit;
            if (!lit) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", spotLight.color);
            r.SetPropertyBlock(block);
        }
    }
}
