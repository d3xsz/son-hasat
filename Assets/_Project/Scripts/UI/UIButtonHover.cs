using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// C-04: Buton üzerine gelince yumuşak büyüme, basınca hafif küçülme. Etkileşimsiz butonda çalışmaz.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    /// <summary>KULLANICI (cila): tıklayınca parşömen/sayfa sesi.</summary>
    public void OnPointerClick(PointerEventData e)
    {
        if (selectable != null && !selectable.IsInteractable()) return;
        if (Sfx.Lib != null) Sfx.UI(Sfx.Lib.uiClick, 0.55f, 1.1f, 0.08f);
    }

    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float pressScale = 0.97f;
    [SerializeField] private float speed = 14f;
    [Tooltip("Üzerine gelince beliren arka hale (isteğe bağlı).")]
    [SerializeField] private Graphic glow;
    [SerializeField] private float glowAlpha = 0.55f;

    private Selectable selectable;
    private bool hover, pressed;

    private void Awake() { selectable = GetComponent<Selectable>(); }

    private void OnDisable()
    {
        hover = pressed = false;
        transform.localScale = Vector3.one;
        if (glow != null) { Color c = glow.color; c.a = 0f; glow.color = c; }
    }

    public void OnPointerEnter(PointerEventData e) { hover = true; }
    public void OnPointerExit(PointerEventData e) { hover = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) { pressed = true; }
    public void OnPointerUp(PointerEventData e) { pressed = false; }

    private void Update()
    {
        bool active = selectable == null || selectable.IsInteractable();
        float target = !active ? 1f : pressed ? pressScale : hover ? hoverScale : 1f;
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        float s = Mathf.Lerp(transform.localScale.x, target, k);
        transform.localScale = new Vector3(s, s, 1f);
        if (glow != null)
        {
            Color c = glow.color;
            float ga = active && (hover || pressed) ? glowAlpha * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f)) : 0f;
            c.a = Mathf.Lerp(c.a, ga, k);
            glow.color = c;
        }
    }
}
