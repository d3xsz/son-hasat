using UnityEngine;

/// <summary>
/// G-29: Mağara eşiği (GDD 6.7). Öfke 100'de aktifleşir; hayattaki TÜM oyuncular eşik kutusunun içindeyken
/// sunucu çöküş sekansını (CollapseSequence) başlatır. Kutu bu nesnenin konumu/dönüşüyle tanımlanır.
/// </summary>
public class CaveTrigger : MonoBehaviour
{
    [SerializeField] private CollapseSequence sequence;
    [Tooltip("Eşik kutusunun boyutu (yerel eksenlerde, m).")]
    [SerializeField] private Vector3 size = new Vector3(7f, 4f, 4f);
    [SerializeField] private int requiredWrath = 100;
    [Tooltip("Bu mesafeden yakın oyuncuya HUD ipucu gösterilir (m).")]
    [SerializeField] private float hintRadius = 22f;
    [Tooltip("Test: Öfke 100 olmadan da aktif say (yalnızca bu kopyada; host'ta açılırsa sekans başlar).")]
    [SerializeField] private bool debugForceArmed = false;

    public static CaveTrigger Instance { get; private set; }

    public bool Armed => debugForceArmed || (WrathSystem.Instance != null && WrathSystem.Instance.Wrath >= requiredWrath);
    public float HintRadius => hintRadius;

    private void OnEnable() { Instance = this; }
    private void OnDisable() { if (Instance == this) Instance = null; }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector3 h = size * 0.5f;
        return Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.y) <= h.y && Mathf.Abs(local.z) <= h.z;
    }

    /// <summary>Hayattaki oyuncu sayısı ve bunlardan eşikte olanlar (herkeste hesaplanabilir; konumlar senkron).</summary>
    public void Count(out int inside, out int alive)
    {
        inside = 0; alive = 0;
        foreach (PlayerLife p in PlayerLife.All)
        {
            if (p == null || p.State != PlayerLifeState.Alive) continue;
            alive++;
            if (Contains(p.transform.position)) inside++;
        }
    }

    private void Update()
    {
        if (sequence == null || !sequence.IsServer || !sequence.IsSpawned) return;
        // KULLANICI KARARI: son totem alınınca (Öfke 100) mağara çöker ve uçurum açılır; oyuncular içeri girince düşer.
        if (sequence.Phase == CollapsePhase.Idle && Armed) sequence.ServerBegin();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, size);
    }
}
