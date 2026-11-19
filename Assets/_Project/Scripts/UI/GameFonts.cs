using UnityEngine;

/// <summary>
/// Oyunun yazı tipleri (OFL lisanslı, Google Fonts): başlıklar Pirata One, diğer yazılar Alegreya.
/// Dosyalar Assets/_Project/Resources/Fonts altında; bulunamazsa Unity'nin varsayılan fontuna düşer.
/// Çalışma anında yazı oluşturan scriptler (sinematik, çöküş başlığı, isim etiketi) buradan alır.
/// </summary>
public static class GameFonts
{
    private static Font title, body;

    public static Font Title => title != null ? title : (title = Load("Fonts/PirataOne-Regular"));
    public static Font Body => body != null ? body : (body = Load("Fonts/Alegreya"));

    private static Font Load(string path)
    {
        Font f = Resources.Load<Font>(path);
        return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
