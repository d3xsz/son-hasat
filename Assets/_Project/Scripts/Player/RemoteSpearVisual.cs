using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-35: Diğer oyuncuların ekranında bu goblinin elinde mızrak (yalnızca SAHİBİ OLMAYAN kopyalarda).
/// Seçili slottaki eşya mızraksa, oyuncu hayattaysa, ağır eşya taşımıyorsa ve gövdesi görünürse gösterilir.
/// Koltuk altı tutuşu: sağ el bel önünde sapı kavrar (parmaklar kapanır), sapın arkası sağ koltuk altına sıkışır;
/// mızrak çizgisi koltuk altından avuç içine geçer. Kol basit iki kemikli IK ile ele uzanır (Animator'dan sonra).
/// Sahip dürtünce (SpearNetworkSync.JabReceived) el ve mızrak birlikte ileri gider. Ağ yükü yok (görsel).
/// </summary>
[DefaultExecutionOrder(200)] // Animator ve gövde pozlarından (PlayerLife, CollapseSequence) sonra
public class RemoteSpearVisual : MonoBehaviour
{
    [SerializeField] private GameObject spearPrefab;
    [SerializeField] private float length = 1.5f;

    [Header("Tutuş (oyuncu köküne göre, metre)")]
    [SerializeField] private float handHeight = 0.95f;
    [SerializeField] private float handForward = 0.32f;
    [SerializeField] private float handRight = 0.14f;
    [Tooltip("Koltuk altı noktası: omuz ekleminin bu kadar altı (m).")]
    [SerializeField] private float armpitDrop = 0.12f;
    [Tooltip("Sapın arka ucunun koltuk altından ne kadar geride kaldığı (m).")]
    [SerializeField] private float tailBehindArmpit = 0.22f;
    [Tooltip("Parmakların sapın etrafında kapanma açısı (derece).")]
    [SerializeField] private float fingerCurl = 50f;
    [SerializeField] private float thumbCurl = 20f;
    [Tooltip("Bakış açısının (aşağı/yukarı) mızrak eğimine yansıma oranı.")]
    [SerializeField] private float pitchFollow = 0.6f;

    [Header("Dürtme")]
    [SerializeField] private float jabReach = 0.3f;
    [SerializeField] private float jabDuration = 0.3f;

    private NetworkObject netObj;
    private InventoryNetworkSync inv;
    private SpearNetworkSync spearNet;
    private PlayerLife life;
    private Transform rArm, rFore, rHand, rMiddle;
    private readonly List<Transform> fingers = new List<Transform>();
    private readonly List<Transform> thumb = new List<Transform>();
    private Renderer bodyRenderer;
    private RemoteLookPose look;
    private Transform spear;
    private float jabTimer = -1f;
    private bool initialized;

    private void Awake() { Init(); }

    /// <summary>Kemikleri bulur ve mızrak nesnesini oluşturur (editör önizlemesi için de çağrılabilir).</summary>
    public void Init()
    {
        if (initialized) return;
        initialized = true;
        look = GetComponent<RemoteLookPose>();
        netObj = GetComponent<NetworkObject>();
        inv = GetComponent<InventoryNetworkSync>();
        spearNet = GetComponent<SpearNetworkSync>();
        life = GetComponent<PlayerLife>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.EndsWith(":RightArm")) rArm = t;
            else if (n.EndsWith(":RightForeArm")) rFore = t;
            else if (n.EndsWith(":RightHand")) rHand = t;
            else if (n.Contains("RightHand") && !n.EndsWith("4"))
            {
                if (n.EndsWith("RightHandMiddle1")) rMiddle = t;
                if (n.Contains("Thumb")) thumb.Add(t); else fingers.Add(t);
            }
        }
        bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (spearPrefab != null)
        {
            spear = Instantiate(spearPrefab).transform; // ebeveynsiz: kemik ölçeğinden etkilenmez
            spear.name = name + "_RemoteSpear";
            spear.localScale = Vector3.one * length;
            spear.gameObject.SetActive(false);
        }
    }

    private void OnEnable() { if (spearNet != null) spearNet.JabReceived += OnJab; }
    private void OnDisable() { if (spearNet != null) spearNet.JabReceived -= OnJab; }
    private void OnDestroy() { if (spear != null) Destroy(spear.gameObject); }

    private void OnJab() { jabTimer = 0f; }

    private void LateUpdate()
    {
        if (spear == null) return;
        bool show = netObj != null && netObj.IsSpawned && !netObj.IsOwner && rHand != null && rArm != null && rFore != null
                    && inv != null && spearNet != null && spearNet.SpearItem != null
                    && inv.SelectedItemId >= 0 && inv.SelectedItemId == inv.IdOf(spearNet.SpearItem)
                    && !inv.CarriesHeavy()
                    && (life == null || life.State == PlayerLifeState.Alive)
                    && (bodyRenderer == null || bodyRenderer.enabled);
        if (!show) { if (spear.gameObject.activeSelf) spear.gameObject.SetActive(false); jabTimer = -1f; return; }

        float jab01 = 0f;
        if (jabTimer >= 0f)
        {
            float t = jabTimer / jabDuration;
            // hızlı ileri, yavaş geri
            jab01 = t < 0.35f ? Mathf.SmoothStep(0f, 1f, t / 0.35f) : Mathf.SmoothStep(1f, 0f, (t - 0.35f) / 0.65f);
            jabTimer += Time.deltaTime;
            if (t >= 1f) jabTimer = -1f;
        }

        ApplyPose(look != null ? look.CurrentPitch : 0f, jab01);
    }

    /// <summary>Tutuş pozunu ve mızrağı uygular (pitch: dikey bakış, + = aşağı; jab01: dürtme 0..1).</summary>
    public void ApplyPose(float pitch, float jab01)
    {
        Init();
        if (spear == null || rHand == null || rArm == null || rFore == null) return;
        if (!spear.gameObject.activeSelf) spear.gameObject.SetActive(true);
        Vector3 f = transform.forward, r = transform.right, u = transform.up;
        float push = jab01 * jabReach;
        Quaternion tilt = Quaternion.AngleAxis(pitch * pitchFollow, r); // bakışa göre aşağı/yukarı

        // 1) sağ kol: el bel önünde (göğüs hizası çevresinde bakışla döner), dirsek geriye-aşağı
        Vector3 chest = transform.position + u * 1.2f;
        Vector3 handOffset = transform.position + u * handHeight + f * (handForward + push) + r * handRight - chest;
        Vector3 handTarget = chest + tilt * handOffset;
        SolveTwoBone(rArm, rFore, rHand, handTarget, -u * 0.6f - f * 0.8f + r * 0.3f);

        // 2) bilek: parmaklar sapa dik, aşağı sarkar (sapın altından sarılır); sonra parmaklar kapanır
        Vector3 armpit = rArm.position - u * armpitDrop - r * 0.04f + tilt * (f * push);
        Vector3 approxDir = (handTarget - armpit).normalized;
        if (rMiddle != null)
        {
            Vector3 fingerDir = Vector3.ProjectOnPlane(-u - r * 0.2f, approxDir);
            if (fingerDir.sqrMagnitude > 1e-4f)
                bone_Aim(rHand, rMiddle.position - rHand.position, fingerDir.normalized);
        }
        foreach (Transform t in fingers) t.localRotation *= Quaternion.Euler(0f, 0f, fingerCurl);
        foreach (Transform t in thumb) t.localRotation *= Quaternion.Euler(0f, 0f, thumbCurl);

        // 3) mızrak: koltuk altından avuç içine geçen çizgi; dürtmede birlikte ileri kayar
        Vector3 palm = rMiddle != null ? Vector3.Lerp(rHand.position, rMiddle.position, 0.5f) : rHand.position;
        Vector3 dir = palm - armpit;
        if (dir.sqrMagnitude < 1e-4f) dir = tilt * f;
        float behind = dir.magnitude + tailBehindArmpit;
        dir.Normalize();
        spear.rotation = Quaternion.LookRotation(dir, u) * Quaternion.Euler(90f, 0f, 0f); // model +Y ekseni boyunca uzanır
        spear.position = palm - dir * behind;
    }

    private static void bone_Aim(Transform bone, Vector3 current, Vector3 desired) { Aim(bone, current, desired); }

    /// <summary>Basit iki kemikli IK (omuz-dirsek-el): eli hedefe, dirseği pole yönüne büker.</summary>
    private static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 poleDir)
    {
        if (upper == null || lower == null || end == null) return;
        Vector3 root = upper.position;
        float a = Vector3.Distance(upper.position, lower.position);
        float b = Vector3.Distance(lower.position, end.position);
        Vector3 toTarget = target - root;
        float dist = Mathf.Clamp(toTarget.magnitude, 0.05f, (a + b) * 0.999f);
        Vector3 dirN = toTarget.normalized;
        float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 bend = Vector3.ProjectOnPlane(poleDir, dirN);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dirN);
        bend.Normalize();
        Vector3 elbow = root + dirN * (a * cosA) + bend * (a * sinA);
        Aim(upper, lower.position - upper.position, elbow - root);
        Aim(lower, end.position - lower.position, (root + dirN * dist) - lower.position);
    }

    private static void Aim(Transform bone, Vector3 current, Vector3 desired)
    {
        if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
    }
}
