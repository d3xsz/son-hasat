using UnityEngine;

/// <summary>
/// KULLANICI (cila): Ayak sesi ve iniş sesi. Her kopyada her oyuncu için yerel çalışır (ağ mesajı yok):
/// hareket konum değişiminden ölçülür, böylece diğer oyuncuların adımları da duyulur.
/// Yürüme/koşma adım aralığı yatay hıza göre; çöküş/kayma sırasında ve bayılmışken çalmaz.
/// </summary>
public class Footsteps : MonoBehaviour
{
    [SerializeField] private float walkStride = 1.9f;
    [SerializeField] private float runStride = 2.4f;
    [SerializeField] private float runSpeed = 5.2f;
    [SerializeField] private float localVolume = 0.32f;
    [SerializeField] private float remoteVolume = 0.55f;
    [SerializeField] private float landMinFallSpeed = 4f;

    private PlayerController controller;
    private PlayerLife life;
    private Unity.Netcode.NetworkObject netObj;
    private Vector3 lastPos;
    private float distance;
    private float verticalSpeed;
    private float minVerticalSpeed;
    private bool wasGrounded = true;
    private float lastStepTime;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        life = GetComponent<PlayerLife>();
        netObj = GetComponent<Unity.Netcode.NetworkObject>();
        netPlayer = GetComponent<NetworkPlayer>();
    }

    private NetworkPlayer netPlayer;
    private bool Crouching => netPlayer != null && netPlayer.IsCrouching;

    private void OnEnable() { lastPos = transform.position; distance = 0f; }

    private bool IsLocal => netObj == null || !netObj.IsSpawned || netObj.IsOwner;

    /// <summary>Taş zemin mi: mağara derinliği (Bölüm 2) ya da altındaki yüzeyin materyali kaya/mağara.</summary>
    private static bool OnStone(Vector3 pos)
    {
        if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.name.StartsWith("Plank_")) return false; // B2-08 köprü tahtası
            if (pos.y < -40f) return true; // Bölüm 2 mağarası
            Renderer r = hit.collider.GetComponent<Renderer>();
            string m = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : hit.collider.name;
            return m.Contains("Rock") || m.Contains("Cave") || m.Contains("Cliff") || m.Contains("Stone");
        }
        return pos.y < -40f; // Bölüm 2 mağarası
    }

    private bool Grounded()
    {
        if (IsLocal && controller != null && controller.enabled) return controller.IsGrounded;
        return Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.45f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 pos = transform.position;
        Vector3 delta = pos - lastPos;
        lastPos = pos;

        // ışınlanma / büyük sıçrama: sayma
        if (delta.sqrMagnitude > 4f) { distance = 0f; return; }

        // yalnızca düşüş anında sus (Bölüm 2'de faz Finished/Arrived kalır; o zaman adımlar duyulmalı — Dinleyen bunları dinler)
        bool blocked = CollapseSequence.IsFalling ||(life != null && !life.CanAct) || (controller != null && controller.ExternalMotion && IsLocal);
        bool grounded = Grounded();
        verticalSpeed = delta.y / dt;
        if (!grounded) minVerticalSpeed = Mathf.Min(minVerticalSpeed, verticalSpeed);

        float vol = IsLocal ? localVolume : remoteVolume;
        SfxLibrary lib = Sfx.Lib;
        if (lib == null) return;

        // iniş
        if (grounded && !wasGrounded && !blocked && minVerticalSpeed < -landMinFallSpeed)
        {
            float k = Mathf.InverseLerp(landMinFallSpeed, 12f, -minVerticalSpeed);
            Sfx.At(lib.jumpLand, pos, vol * Mathf.Lerp(0.7f, 1.2f, k), 1f, 0.08f, 1.5f, 25f);
            Noise.Emit(pos, Noise.Landing * Mathf.Lerp(0.7f, 1.2f, k)); // B2-04: Dinleyen duyar
            distance = 0f;
            lastStepTime = Time.time;
        }
        if (grounded) minVerticalSpeed = 0f;
        wasGrounded = grounded;

        if (!grounded || blocked) return;

        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float speed = flat.magnitude / dt;
        if (speed < 0.6f) { distance = Mathf.Min(distance, walkStride * 0.5f); return; }

        bool running = speed > runSpeed;
        bool crouch = Crouching;
        distance += flat.magnitude;
        float stride = running ? runStride : crouch ? walkStride * 0.6f : walkStride;
        if (distance >= stride && Time.time - lastStepTime > 0.18f)
        {
            distance = 0f;
            lastStepTime = Time.time;
            bool stone = OnStone(pos) && lib.stepStoneWalk != null && lib.stepStoneWalk.Length > 0;
            AudioClip[] clips = stone ? (running && lib.stepStoneRun != null && lib.stepStoneRun.Length > 0 ? lib.stepStoneRun : lib.stepStoneWalk)
                                      : (running ? lib.stepRun : lib.stepWalk);
            Sfx.At(clips, pos, vol * (running ? 1.1f : crouch ? 0.35f : 1f) * (stone ? 0.85f : 1f), running ? 1.02f : crouch ? 1.08f : 0.95f, 0.07f, 1.2f, crouch ? 10f : 22f);
            // B2-04: koşmak gürültülü, yürümek sessiz, eğilerek yürümek neredeyse duyulmaz
            Noise.Emit(pos, running ? Noise.RunStep : crouch ? Noise.CrouchStep : Noise.WalkStep);
        }
    }
}
