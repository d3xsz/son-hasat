using UnityEngine;

/// <summary>
/// B2-05: Yönlendirici kristal. E ile 45° döner (durum BeamPuzzle'da, ağda). Kendisine çarpan ışını, sivri ucunun baktığı
/// yöne (yerel +Z, dönüş adımına göre) gönderir. Dönen kısım yumuşakça yeni açıya döner.
/// </summary>
public class BeamMirror : MonoBehaviour, IInteractable
{
    [Tooltip("Dönen görsel parça (sivri uç yerel +Z'yi gösterir).")]
    [SerializeField] private Transform rotor;
    [Tooltip("Başlangıç yönü (0-7, her adım 45°, 0 = dünya +Z/kuzey).")]
    [SerializeField] private int startStep = 0;
    [SerializeField] private float turnSpeed = 200f;

    private BeamPuzzle owner;
    private int index;
    private float shownYaw;

    public BeamPuzzle Owner => owner;
    public int Index => index;
    public int StartStep => startStep;

    public void Bind(BeamPuzzle puzzle, int i) { owner = puzzle; index = i; shownYaw = startStep * 45f; }

    public Vector3 DirectionForStep(int step) => Quaternion.Euler(0f, step * 45f, 0f) * Vector3.forward;

    public string InteractionPrompt => "Çevir";
    public bool CanInteract(PlayerInteractor interactor) => owner != null && !owner.IsOpen;
    public void Interact(PlayerInteractor interactor) { if (owner != null) owner.RequestRotate(index); }

    private Light litGlow;
    private bool lit;

    /// <summary>Işın bu kristale değiyor mu (BeamPuzzle her kare yerel olarak bildirir) — değince kristal parlar.</summary>
    public void SetLit(bool value) { lit = value; }

    private void Update()
    {
        if (litGlow == null)
        {
            var go = new GameObject("BeamGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.3f;
            litGlow = go.AddComponent<Light>();
            litGlow.type = LightType.Point; litGlow.range = 3.5f; litGlow.color = new Color(0.45f, 0.9f, 1f);
            litGlow.intensity = 0f; litGlow.shadows = LightShadows.None;
        }
        float targetI = lit ? 2.2f + 0.3f * Mathf.Sin(Time.time * 7f) : 0f;
        litGlow.intensity = Mathf.MoveTowards(litGlow.intensity, targetI, Time.deltaTime * 8f);
        litGlow.enabled = litGlow.intensity > 0.01f;

        if (rotor == null || owner == null) return;
        float target = owner.StepOf(index) * 45f;
        shownYaw = Mathf.MoveTowardsAngle(shownYaw, target, turnSpeed * Time.deltaTime);
        rotor.rotation = Quaternion.Euler(0f, shownYaw, 0f);
    }
}
