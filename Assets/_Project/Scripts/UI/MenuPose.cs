using UnityEngine;

/// <summary>
/// Menü sahnesindeki karakterlerin duruşu: Animator'da verilen durumu belirli (veya rastgele) bir anda başlatır.
/// normalizedTime &gt;= 0 ise o karede durur (ör. çömelme 1 = tam çömelmiş); &lt; 0 ise rastgele bir yerden döngüye başlar.
/// </summary>
[RequireComponent(typeof(Animator))]
public class MenuPose : MonoBehaviour
{
    [SerializeField] private string stateName = "";
    [SerializeField] private float normalizedTime = -1f;
    [SerializeField] private float playbackSpeed = 1f;

    private void Start()
    {
        var animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        float t = normalizedTime >= 0f ? normalizedTime : Random.value;
        if (!string.IsNullOrEmpty(stateName)) animator.Play(stateName, 0, t);
        else animator.Play(0, 0, t);
        animator.speed = playbackSpeed * Random.Range(0.9f, 1.1f);
    }
}
