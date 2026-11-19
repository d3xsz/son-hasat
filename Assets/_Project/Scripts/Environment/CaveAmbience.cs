using UnityEngine;

/// <summary>
/// KULLANICI (cila): Mağara ortam sesi. Döngülü mağara atmosferi (2B, yavaşça açılır) + sarkıt uçlarından rastgele
/// yankılı su damlaları (3B). Damla parçacıkları (varsa) aynı noktalardan düşer. Yankıyı sahnedeki AudioReverbZone verir.
/// Yalnızca yerel ses/görsel; ağ gerekmez.
/// </summary>
public class CaveAmbience : MonoBehaviour
{
    [SerializeField] private Transform[] dripPoints = new Transform[0];
    [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.45f;
    [SerializeField] private float dripIntervalMin = 0.8f;
    [SerializeField] private float dripIntervalMax = 3.2f;
    [SerializeField, Range(0f, 1f)] private float dripVolume = 0.55f;

    private AudioSource loop;
    private float nextDrip;

    private void Start()
    {
        SfxLibrary lib = Sfx.Lib;
        if (lib == null || lib.caveAmbience == null) return;
        loop = gameObject.AddComponent<AudioSource>();
        loop.clip = lib.caveAmbience; loop.loop = true; loop.spatialBlend = 0f; loop.volume = 0f; loop.playOnAwake = false;
        loop.Play();
        nextDrip = Time.time + 1f;
    }

    private void Update()
    {
        // KULLANICI (2026-11-14): Bölüm 3'te (rüyalar alemi) mağara sesi duyulmasın; Bölüm 2 sahnesi yüklü kaldığı için burada susturulur
        bool dream = FreefallSequence.Instance != null && Camera.main != null && FreefallSequence.InColumn(Camera.main.transform.position);
        if (loop != null) loop.volume = Mathf.MoveTowards(loop.volume, dream ? 0f : ambienceVolume, Time.deltaTime * (dream ? 0.6f : 0.15f));
        if (dream || Time.time < nextDrip || dripPoints.Length == 0) return;
        nextDrip = Time.time + Random.Range(dripIntervalMin, dripIntervalMax);
        SfxLibrary lib = Sfx.Lib;
        Transform p = dripPoints[Random.Range(0, dripPoints.Length)];
        if (lib != null && p != null) Sfx.At(lib.drips, p.position, dripVolume * Random.Range(0.6f, 1f), Random.Range(0.85f, 1.2f), 0.02f, 2f, 30f);
    }
}
