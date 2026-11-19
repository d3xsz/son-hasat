using UnityEngine;

/// <summary>
/// G-10: Oyuncuların doğacağı nokta. Sahnede 4 tane olmalı (Index 0-3); host oyuncuları sırayla buralara yerleştirir.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    [SerializeField] private int index;
    public int Index => index;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.4f);
        Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 1.2f);
    }
}
