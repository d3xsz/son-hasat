using UnityEngine;

/// <summary>
/// B2-04: Dinleyen'in prosedürel animasyonu (her kopyada yerel; durum ve dikkat noktası ağdan gelir).
/// Kambur gövde nefesle hafifçe şişer, yürürken sallanır ve uzun kolları sarkıp savrulur; baş dikkat noktasına döner,
/// duyunca başını sertçe çevirip kulak yüzgeçlerini açar; ararken başını iki yana yavaşça çevirir; sersemleyince sendeler.
/// </summary>
public class ListenerAnimator : MonoBehaviour
{
    [SerializeField] private ListenerCreature creature;
    [SerializeField] private Transform body;
    [SerializeField] private Transform head;
    [SerializeField] private Transform earL, earR;
    [SerializeField] private Transform armL, armR;
    [SerializeField] private Transform legL, legR;

    private Vector3 lastPos;
    private float stride;
    private float lastStepPhase;
    private Quaternion bodyRest, headRest, armLRest, armRRest, legLRest, legRRest, earLRest, earRRest;
    private float alert;
    private ListenerCreature.Mood lastMood;

    private void Awake()
    {
        if (creature == null) creature = GetComponentInParent<ListenerCreature>();
        bodyRest = body.localRotation; headRest = head.localRotation;
        armLRest = armL.localRotation; armRRest = armR.localRotation; legLRest = legL.localRotation; legRRest = legR.localRotation;
        earLRest = earL.localRotation; earRRest = earR.localRotation;
        lastPos = transform.position;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime; if (dt <= 0f) return;
        Vector3 v = (transform.position - lastPos) / dt; v.y = 0f; lastPos = transform.position;
        float speed = v.magnitude;
        stride += speed * dt * 2.4f;
        // B2-07: her yarım adımda ağır, boğuk ayak sesi (karanlıkta yaklaştığı duyulsun)
        float stepPhase = Mathf.Floor(stride / Mathf.PI);
        if (stepPhase != lastStepPhase && speed > 0.3f && Sfx.Lib != null)
            Sfx.At(Sfx.Lib.stepStoneWalk != null && Sfx.Lib.stepStoneWalk.Length > 0 ? Sfx.Lib.stepStoneWalk : Sfx.Lib.stepWalk, transform.position, Mathf.Lerp(0.45f, 0.9f, Mathf.Clamp01(speed / 4.4f)), 0.55f, 0.06f, 2f, 26f);
        lastStepPhase = stepPhase;

        ListenerCreature.Mood mood = creature != null ? creature.CurrentMood : ListenerCreature.Mood.Wander;
        if (mood == ListenerCreature.Mood.Investigate && lastMood == ListenerCreature.Mood.Wander) alert = 1f;
        lastMood = mood;
        alert = Mathf.MoveTowards(alert, 0f, dt * 0.8f);

        float t = Time.time;
        float breathe = Mathf.Sin(t * 1.6f) * 2f;
        float swing = Mathf.Sin(stride) * Mathf.Clamp01(speed / 2f);
        float stun = mood == ListenerCreature.Mood.Stunned ? Mathf.Sin(t * 9f) * 12f : 0f;

        body.localRotation = bodyRest * Quaternion.Euler(breathe + Mathf.Abs(swing) * 6f + (mood == ListenerCreature.Mood.Investigate ? 10f : 0f), 0f, swing * 5f + stun);
        armL.localRotation = armLRest * Quaternion.Euler(swing * 35f + Mathf.Sin(t * 1.3f) * 4f, 0f, 0f);
        armR.localRotation = armRRest * Quaternion.Euler(-swing * 35f + Mathf.Sin(t * 1.1f + 1f) * 4f, 0f, 0f);
        legL.localRotation = legLRest * Quaternion.Euler(-swing * 30f, 0f, 0f);
        legR.localRotation = legRRest * Quaternion.Euler(swing * 30f, 0f, 0f);

        // baş: dikkat noktasına (ararken iki yana tarar)
        Quaternion look = headRest;
        if (creature != null)
        {
            Vector3 to = creature.Attention - head.position; to.y *= 0.3f;
            if (to.sqrMagnitude > 0.5f)
            {
                Quaternion world = Quaternion.LookRotation(to.normalized, Vector3.up);
                look = Quaternion.Inverse(head.parent.rotation) * world;
                look = Quaternion.RotateTowards(headRest, look, 70f);
            }
        }
        if (mood == ListenerCreature.Mood.Search) look *= Quaternion.Euler(0f, Mathf.Sin(t * 1.4f) * 50f, Mathf.Sin(t * 0.9f) * 10f);
        float snap = mood == ListenerCreature.Mood.Investigate || alert > 0.5f ? 18f : 3f;
        head.localRotation = Quaternion.Slerp(head.localRotation, look * Quaternion.Euler(0, 0, stun), dt * snap);

        // kulak yüzgeçleri: dinlerken açılır, duyunca titrer
        float open = (mood == ListenerCreature.Mood.Wander ? 10f : 35f) + alert * Mathf.Sin(t * 40f) * 8f;
        earL.localRotation = earLRest * Quaternion.Euler(0f, -open, 0f);
        earR.localRotation = earRRest * Quaternion.Euler(0f, open, 0f);
    }
}
