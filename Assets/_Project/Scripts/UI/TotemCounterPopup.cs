using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (cila): Bir totem alınınca (kim alırsa alsın) ekranın üstünde kısa süre "Totem 1 / 5" ve 5 nokta görünür;
/// oyuncu ilk totemde kaç tane toplaması gerektiğini öğrenir. Yavaşça belirir, birkaç saniye kalır, söner.
/// (Genel kural "ekranda görev yazısı yok"; bu sayaç kullanıcı isteğiyle istisna.)
/// Arayüz kodla kurulur; HUD Canvas'ının altında durur.
/// </summary>
public class TotemCounterPopup : MonoBehaviour
{
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Color textColor = new Color(0.93f, 0.88f, 0.75f, 1f);
    [SerializeField] private Color filledColor = new Color(1f, 0.78f, 0.35f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.93f, 0.88f, 0.75f, 0.25f);
    [SerializeField] private float holdSeconds = 3.5f;
    [SerializeField] private float topOffset = -150f;

    private CanvasGroup group;
    private Text label;
    private Image[] dots = new Image[0];
    private RectTransform dotRow;
    private float timer = -1f;
    private int shownTaken, shownTotal;
    private float pop;

    private void Awake() { Build(); }
    private void OnEnable() { Totem.TakenCountChanged += Show; }
    private void OnDisable() { Totem.TakenCountChanged -= Show; }

    private void Build()
    {
        var root = new GameObject("TotemCounter", typeof(RectTransform), typeof(CanvasGroup));
        var rt = (RectTransform)root.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, topOffset);
        rt.sizeDelta = new Vector2(600f, 120f);
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;

        var lgo = new GameObject("Label", typeof(RectTransform));
        var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
        lrt.anchorMin = new Vector2(0f, 0.45f); lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        label = lgo.AddComponent<Text>();
        label.font = GameFonts.Title; label.fontSize = 52; label.alignment = TextAnchor.MiddleCenter; label.color = textColor;
        // dikey taşma açık: yazı tipi satır yüksekliği kutudan büyükse Text yazıyı tamamen gizliyordu
        label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Overflow; label.raycastTarget = false;
        var sh = lgo.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.7f); sh.effectDistance = new Vector2(2f, -2f);

        var dgo = new GameObject("Dots", typeof(RectTransform));
        dotRow = (RectTransform)dgo.transform; dotRow.SetParent(rt, false);
        dotRow.anchorMin = new Vector2(0f, 0f); dotRow.anchorMax = new Vector2(1f, 0.42f); dotRow.offsetMin = dotRow.offsetMax = Vector2.zero;
    }

    private void EnsureDots(int total)
    {
        if (dots.Length == total) return;
        foreach (Image d in dots) if (d != null) Destroy(d.gameObject);
        dots = new Image[total];
        float spacing = 34f;
        for (int i = 0; i < total; i++)
        {
            var g = new GameObject("Dot" + i, typeof(RectTransform));
            var r = (RectTransform)g.transform; r.SetParent(dotRow, false);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(20f, 20f);
            r.anchoredPosition = new Vector2((i - (total - 1) * 0.5f) * spacing, 0f);
            var img = g.AddComponent<Image>(); img.sprite = dotSprite; img.raycastTarget = false;
            dots[i] = img;
        }
    }

    private void Show(int taken, int total)
    {
        if (total <= 0) return;
        shownTaken = taken; shownTotal = total;
        EnsureDots(total);
        label.text = "Totem " + taken + " / " + total;
        timer = 0f;
        pop = 0f;
    }

    private void Update()
    {
        if (timer < 0f || group == null) return;
        timer += Time.deltaTime;
        float fadeIn = 0.35f, fadeOut = 1.0f;
        float a = timer < fadeIn ? timer / fadeIn : timer < fadeIn + holdSeconds ? 1f : 1f - (timer - fadeIn - holdSeconds) / fadeOut;
        group.alpha = Mathf.Clamp01(a);
        pop = Mathf.Min(1f, pop + Time.deltaTime * 3f);

        for (int i = 0; i < dots.Length; i++)
        {
            if (dots[i] == null) continue;
            bool filled = i < shownTaken;
            bool newest = i == shownTaken - 1;
            dots[i].color = filled ? filledColor : emptyColor;
            float s = newest ? 1f + 0.6f * Mathf.Sin(pop * Mathf.PI) : 1f; // yeni alınan nokta "pıt" diye büyür
            dots[i].rectTransform.localScale = new Vector3(s, s, 1f);
        }
        if (a <= 0f && timer > fadeIn) { timer = -1f; group.alpha = 0f; }
    }
}
