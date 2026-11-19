using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G-15: Zehir bölgesi (hastalık çiçeği, ileride yayılan hastalık duvarı). Silindir hacmi: yatayda yarıçap, dikeyde yükseklik.
/// Sadece SUNUCUDA çalışır: bölge içindeki her oyuncunun PoisonSystem'ine saniyede sabit zehir ekler.
/// Not: uzaktaki oyuncuların CharacterController'ı kapalı olduğu için çarpışma tetikleyicisi yerine konum hesabı kullanılır.
/// </summary>
public class PoisonZone : MonoBehaviour
{
    [SerializeField] private float radius = 2.2f;
    [Tooltip("Bölgenin zeminden yukarı ve aşağı erişimi (m). Oyuncunun ayak konumuna göre.")]
    [SerializeField] private float height = 2f;
    [Tooltip("Bölge içindeyken saniyede eklenen zehir (0-100 ölçeğinde).")]
    [SerializeField] private float poisonPerSecond = 4f;
    [SerializeField] private bool active = true;

    public float Radius
    {
        get => radius;
        set => radius = Mathf.Max(0f, value);
    }

    public bool Active
    {
        get => active;
        set => active = value;
    }

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 c = transform.position;
        float dx = worldPosition.x - c.x;
        float dz = worldPosition.z - c.z;
        return dx * dx + dz * dz <= radius * radius && Mathf.Abs(worldPosition.y - c.y) <= height;
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (!active || nm == null || !nm.IsServer) return;

        foreach (NetworkClient client in nm.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null || !Contains(playerObject.transform.position)) continue;

            PoisonSystem poison = playerObject.GetComponent<PoisonSystem>();
            if (poison != null && !poison.IsFull) poison.AddPoison(poisonPerSecond * Time.deltaTime);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.9f);
        const int steps = 48;
        Vector3 prev = transform.position + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
        Gizmos.DrawWireCube(transform.position + Vector3.up * (height * 0.5f), new Vector3(radius * 2f, height, radius * 2f));
    }
}
