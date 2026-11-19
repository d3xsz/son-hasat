using UnityEngine;

/// <summary>
/// M-11: Dinleyen'in görünümü kullanıcının Golem modeli (Kevin Iglesias, Humanoid). Mantık ListenerCreature'da aynen kalır;
/// bu bileşen yalnızca görsel: hızı (konum farkından; istemcide NetworkTransform ile de çalışır) Animator'a verir
/// → yürüme/koşma; dinlerken/ararken (Investigate/Search) dururken çömelip dinler; başı dikkat noktasına döner;
/// sersemleyince sendeler. Her kopyada yerel.
/// </summary>
[DefaultExecutionOrder(50)]
public class GolemAnimatorDriver : MonoBehaviour
{
    [SerializeField] private ListenerCreature creature;
    [SerializeField] private Animator animator;
    [Tooltip("Kliplerin insan ölçeğindeki doğal hızları (m/sn); Awake'te golem boyuna (humanScale × ölçek) çevrilir, oynatma hızı gerçek hıza göre ayarlanır (ayak kayması olmasın).")]
    [SerializeField] private float walkClipSpeed = 1.39f;
    [SerializeField] private float runClipSpeed = 4.38f;
    [SerializeField] private float headTurnLimit = 70f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int ListenId = Animator.StringToHash("Listen");
    private Vector3 lastPos;
    private float speed;
    private Transform head, spine, chest, upperArmL, upperArmR, foreArmL, foreArmR;
    private Quaternion headLook = Quaternion.identity;
    private float attackStart = -1f, attackWindup = 0.5f;

    /// <summary>M-12: vuruş (her kopyada, ListenerCreature.WindupRpc): kollar kalkar → yere çarpar → toparlanır.</summary>
    public void PlayAttack(float windup) { attackStart = Time.time; attackWindup = Mathf.Max(0.15f, windup); }

    private void Awake()
    {
        if (creature == null) creature = GetComponentInParent<ListenerCreature>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            float k = animator.humanScale * animator.transform.lossyScale.y;
            walkClipSpeed *= k; runClipSpeed *= k;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperArmL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            upperArmR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            foreArmL = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            foreArmR = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        }
        lastPos = transform.position;
    }

    private void Update()
    {
        float dt = Time.deltaTime; if (dt <= 0f || animator == null) return;
        Vector3 v = (transform.position - lastPos) / dt; v.y = 0f; lastPos = transform.position;
        speed = Mathf.Lerp(speed, v.magnitude, 1f - Mathf.Exp(-10f * dt));

        ListenerCreature.Mood mood = creature != null ? creature.CurrentMood : ListenerCreature.Mood.Wander;
        bool listening = speed < 0.35f && attackStart < 0f &&(mood == ListenerCreature.Mood.Investigate || mood == ListenerCreature.Mood.Search || mood == ListenerCreature.Mood.Wander);
        animator.SetFloat(SpeedId, speed);
        animator.SetBool(ListenId, listening);

        float play = 1f;
        if (speed > 0.35f)
        {
            // blend ağacındaki karışıma göre beklenen klip hızı (yürüme 1.6'ya kadar, koşu 3.6'dan sonra)
            float w = Mathf.InverseLerp(1.6f, 3.6f, speed);
            float clip = Mathf.Lerp(walkClipSpeed, runClipSpeed, w);
            play = Mathf.Clamp(speed / Mathf.Max(0.1f, clip), 0.5f, 1.6f);
        }
        if (mood == ListenerCreature.Mood.Stunned) play = 0.35f;
        animator.speed = play;
    }

    private void LateUpdate()
    {
        if (creature == null) return;
        float t = Time.time;
        ListenerCreature.Mood mood = creature.CurrentMood;

        // baş: dikkat noktasına (animasyonun üstüne), ararken iki yana tarar
        if (head != null)
        {
            Quaternion target = Quaternion.identity;
            Vector3 to = creature.Attention - head.position; to.y *= 0.3f;
            if (to.sqrMagnitude > 0.5f)
            {
                Vector3 fwd = transform.forward;
                float yaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd, Vector3.up), Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up), -headTurnLimit, headTurnLimit);
                target = Quaternion.AngleAxis(yaw, Vector3.up);
            }
            if (mood == ListenerCreature.Mood.Search) target *= Quaternion.AngleAxis(Mathf.Sin(t * 1.4f) * 45f, Vector3.up);
            float snap = mood == ListenerCreature.Mood.Investigate ? 10f : 3f;
            headLook = Quaternion.Slerp(headLook, target, 1f - Mathf.Exp(-snap * Time.deltaTime));
            head.rotation = headLook * head.rotation;
        }

        // sersemleme: gövde sendeler
        if (spine != null && mood == ListenerCreature.Mood.Stunned)
        {
            attackStart = -1f;
            spine.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 9f) * 12f, transform.forward) * spine.rotation;
        }

        ApplyAttackPose(t);
    }

    /// <summary>Vuruş pozu animasyonun üstüne: hazırlıkta kollar havaya + gövde geriye, vuruşta öne-aşağı çarpma.</summary>
    private void ApplyAttackPose(float t)
    {
        if (attackStart < 0f || spine == null) return;
        float e = t - attackStart, slam = 0.18f, recover = 0.45f;
        float raise, strike;
        if (e < attackWindup) { raise = Mathf.SmoothStep(0f, 1f, e / attackWindup); strike = 0f; }
        else if (e < attackWindup + slam) { float k = (e - attackWindup) / slam; raise = 1f - k; strike = k; }
        else if (e < attackWindup + slam + recover) { raise = 0f; strike = 1f - Mathf.SmoothStep(0f, 1f, (e - attackWindup - slam) / recover); }
        else { attackStart = -1f; return; }

        Vector3 right = transform.right;
        // gövde: hazırlıkta geriye gerilir, vuruşta öne kapanır
        float lean = -14f * raise + 28f * strike;
        Transform torso = chest != null ? chest : spine;
        torso.rotation = Quaternion.AngleAxis(lean, right) * torso.rotation;
        // kollar: hazırlıkta başın üstüne, vuruşta öne-aşağı
        float arm = -150f * raise + 35f * strike;
        if (upperArmL != null) upperArmL.rotation = Quaternion.AngleAxis(arm, right) * upperArmL.rotation;
        if (upperArmR != null) upperArmR.rotation = Quaternion.AngleAxis(arm, right) * upperArmR.rotation;
        float bend = -35f * raise;
        if (foreArmL != null) foreArmL.rotation = Quaternion.AngleAxis(bend, right) * foreArmL.rotation;
        if (foreArmR != null) foreArmR.rotation = Quaternion.AngleAxis(bend, right) * foreArmR.rotation;
    }
}
