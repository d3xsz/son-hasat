using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-35: Diğer oyuncuların ekranında bu goblinin bakış yönü: sahibin dikey bakışı (NetworkPlayer.ShownPitch, zaten senkron)
/// gövdenin üst kısmına ve kafaya dağıtılarak uygulanır (Animator'dan sonra). Yalnızca sahibi olmayan kopyada, hayattayken.
/// </summary>
[DefaultExecutionOrder(150)] // Animator ve PlayerLife'tan sonra, RemoteSpearVisual'dan (200) önce
public class RemoteLookPose : MonoBehaviour
{
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("Bakış açısının kemiklere dağılımı: Spine1, Spine2, Neck, Head (toplam ~1).")]
    [SerializeField] private float[] weights = { 0.15f, 0.2f, 0.25f, 0.4f };

    private NetworkObject netObj;
    private NetworkPlayer netPlayer;
    private PlayerLife life;
    private Transform[] bones;
    private bool initialized;

    public float CurrentPitch { get; private set; }

    private void Awake() { Init(); }

    public void Init()
    {
        if (initialized) return;
        initialized = true;
        netObj = GetComponent<NetworkObject>();
        netPlayer = GetComponent<NetworkPlayer>();
        life = GetComponent<PlayerLife>();
        bones = new Transform[4];
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.EndsWith(":Spine1")) bones[0] = t;
            else if (n.EndsWith(":Spine2")) bones[1] = t;
            else if (n.EndsWith(":Neck")) bones[2] = t;
            else if (n.EndsWith(":Head")) bones[3] = t;
        }
    }

    private void LateUpdate()
    {
        CurrentPitch = 0f;
        if (netObj == null || !netObj.IsSpawned || netObj.IsOwner || netPlayer == null) return;
        if (life != null && life.State != PlayerLifeState.Alive) return;
        ApplyPose(netPlayer.ShownPitch);
    }

    /// <summary>Verilen dikey bakış açısını (derece, + = aşağı) gövde üstü ve kafaya uygular.</summary>
    public void ApplyPose(float pitch)
    {
        Init();
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        CurrentPitch = pitch;
        Vector3 axis = transform.right;
        for (int i = 0; i < bones.Length && i < weights.Length; i++)
            if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(pitch * weights[i], axis) * bones[i].rotation;
    }
}
