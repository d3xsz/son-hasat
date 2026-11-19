using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Diğer oyuncuların başının üstünde adları (yalnızca sahibi olmayan kopyada). Yakındayken görünür,
/// uzaklaştıkça solar ve belli bir mesafeden sonra gizlenir; her zaman yerel kameraya döner.
/// Gövde gizliyse (izleyici modunda izlenen oyuncu) veya oyuncu ölüyse görünmez.
/// </summary>
[DefaultExecutionOrder(300)]
public class NameTag : MonoBehaviour
{
    [SerializeField] private float height = 2.15f;
    [Tooltip("Bu mesafeye kadar tam görünür (m).")]
    [SerializeField] private float fullDistance = 5f;
    [Tooltip("Bu mesafeden sonra görünmez (m).")]
    [SerializeField] private float hideDistance = 9f;
    [SerializeField] private float characterSize = 0.035f;
    [SerializeField] private Color color = new Color(0.95f, 0.92f, 0.85f);

    private NetworkObject netObj;
    private NetworkPlayer netPlayer;
    private PlayerLife life;
    private Renderer bodyRenderer;
    private TextMesh text;
    private MeshRenderer textRenderer;

    private void Awake()
    {
        netObj = GetComponent<NetworkObject>();
        netPlayer = GetComponent<NetworkPlayer>();
        life = GetComponent<PlayerLife>();
        bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);

        var go = new GameObject("NameTag");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * height;
        text = go.AddComponent<TextMesh>();
        text.font = GameFonts.Body;
        go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = characterSize;
        text.color = color;
        textRenderer = go.GetComponent<MeshRenderer>();
        textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        textRenderer.receiveShadows = false;
        textRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        Camera cam = LocalCamera();
        bool show = netObj != null && netObj.IsSpawned && !netObj.IsOwner && netPlayer != null && cam != null
                    && (life == null || life.State != PlayerLifeState.Dead)
                    && (bodyRenderer == null || bodyRenderer.enabled);
        float a = 0f;
        if (show)
        {
            float d = Vector3.Distance(cam.transform.position, text.transform.position);
            a = 1f - Mathf.InverseLerp(fullDistance, hideDistance, d);
        }
        textRenderer.enabled = a > 0.01f;
        if (!textRenderer.enabled) return;

        if (text.text != netPlayer.DisplayName) text.text = netPlayer.DisplayName;
        text.color = new Color(color.r, color.g, color.b, a);
        // bayılınca yerde yatan gövdenin üstüne iner
        float h = life != null && life.State == PlayerLifeState.Fainted ? 0.8f : height;
        text.transform.localPosition = Vector3.up * h;
        text.transform.rotation = Quaternion.LookRotation(text.transform.position - cam.transform.position, Vector3.up);
    }

    private static Camera LocalCamera()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return null;
        Camera c = nm.LocalClient.PlayerObject.GetComponentInChildren<Camera>();
        return c != null && c.isActiveAndEnabled ? c : Camera.main;
    }
}
