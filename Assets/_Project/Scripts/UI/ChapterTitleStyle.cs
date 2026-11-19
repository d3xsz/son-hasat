using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KULLANICI (2026-11-15): bölüm başlıkları üç bölümde de AYNI görsel dili kullanır. Ölçü, Bölüm 2 başlığıdır
/// (CollapseSequence): siyah ekran üstünde, ortada, tek satır "BÖLÜM N: Ad", başlık yazı tipi, krem rengi.
/// Bölüm 1 (IntroCinematic) ve Bölüm 3 (ChapterEnd geçişi) başlıklarını bu sınıf kurar; Bölüm 2'nin kendi kodu aynı değerleri kullanır.
/// </summary>
public static class ChapterTitleStyle
{
    public static readonly Color TextColor = new Color(0.9f, 0.85f, 0.75f);
    public const int FontSize = 56;

    /// <summary>Kendi tuvalinde (Bölüm 2 ile aynı ölçekleme) ortalanmış başlık yazısı kurar; başta görünmezdir.</summary>
    public static Text Create(Transform parent, string text, int sortingOrder)
    {
        GameObject canvasGo = new GameObject("ChapterTitle");
        if (parent != null) canvasGo.transform.SetParent(parent, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        // Bölüm 2 ile aynı: varsayılan başvuru çözünürlüğüyle ekran boyutuna göre ölçekleme
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        GameObject txt = new GameObject("Title", typeof(RectTransform));
        txt.transform.SetParent(canvasGo.transform, false);
        Text title = txt.AddComponent<Text>();
        title.font = GameFonts.Title;
        title.fontSize = FontSize;
        title.alignment = TextAnchor.MiddleCenter;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        title.color = WithAlpha(0f);
        title.text = text;
        title.raycastTarget = false;
        RectTransform tr = title.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        return title;
    }

    public static Color WithAlpha(float a) => new Color(TextColor.r, TextColor.g, TextColor.b, Mathf.Clamp01(a));
}
