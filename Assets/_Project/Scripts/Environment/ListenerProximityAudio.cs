using UnityEngine;

/// <summary>
/// B2-07: Dinleyen yaklaştıkça yerel oyuncunun kulağında gerilim: alçak uğultu yükselir ve kalp atışı hızlanır
/// (yaratık görünmese bile tehlike hissedilsin). Avlanırken daha güçlü. Yalnızca yerel ses; ağ gerekmez.
/// Dinleyen'in üstüne eklenir.
/// </summary>
public class ListenerProximityAudio : MonoBehaviour
{
    [SerializeField] private ListenerCreature creature;
    [SerializeField] private float range = 16f;
    [SerializeField, Range(0f, 1f)] private float droneVolume = 0.45f;

    private AudioSource drone;
    private float nextBeat;
    private float level;

    private void Awake()
    {
        if (creature == null) creature = GetComponent<ListenerCreature>();
        drone = gameObject.AddComponent<AudioSource>();
        drone.clip = PlaceholderSounds.CreateDroneLoop();
        drone.loop = true; drone.spatialBlend = 0f; drone.volume = 0f; drone.pitch = 0.45f; drone.playOnAwake = false;
        drone.Play();
    }

    private void Update()
    {
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        float target = 0f;
        if (cam != null)
        {
            float d = Vector3.Distance(cam.position, transform.position + Vector3.up * 1.5f);
            target = Mathf.Clamp01(1f - d / range);
            target *= target;
            if (creature != null && creature.CurrentMood == ListenerCreature.Mood.Investigate) target = Mathf.Min(1f, target * 1.6f + 0.1f * (d < range ? 1f : 0f));
        }
        level = Mathf.MoveTowards(level, target, Time.deltaTime * 0.8f);
        drone.volume = droneVolume * level;
        drone.pitch = 0.42f + 0.12f * level;

        if (level > 0.15f && Time.time >= nextBeat && Sfx.Lib != null)
        {
            Sfx.UI(Sfx.Lib.heartbeat, 0.25f + 0.45f * level, 0.92f, 0.02f);
            nextBeat = Time.time + Mathf.Lerp(1.15f, 0.42f, level);
        }
    }
}
