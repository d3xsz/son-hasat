using UnityEngine;

/// <summary>
/// C-04: Ana menü arka planında kameranın çok yavaş süzülmesi (dalgada sallanan bir tekne gibi).
/// </summary>
public class MenuCameraDrift : MonoBehaviour
{
    [SerializeField] private float swayDegrees = 1.2f;
    [SerializeField] private float rollDegrees = 0.8f;
    [SerializeField] private float bob = 0.12f;
    [SerializeField] private float yawRange = 4f;
    [SerializeField] private float period = 7f;

    private Vector3 startPos;
    private Vector3 startEuler;

    private void Awake()
    {
        startPos = transform.position;
        startEuler = transform.eulerAngles;
    }

    private void Update()
    {
        float t = Time.time * Mathf.PI * 2f / period;
        transform.position = startPos + Vector3.up * Mathf.Sin(t) * bob;
        transform.rotation = Quaternion.Euler(
            startEuler.x + Mathf.Sin(t * 0.7f) * swayDegrees,
            startEuler.y + Mathf.Sin(Time.time * 0.05f) * yawRange,
            startEuler.z + Mathf.Sin(t * 0.9f + 1f) * rollDegrees);
    }
}
