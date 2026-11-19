using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// C-04: Sahne geçişlerinde siyaha kararma. Kendi kendini kurar (kalıcı, en üstte bir Canvas).
/// - Her sahne yüklenişinde siyahtan açılır (ağ sahne yüklemeleri dahil).
/// - ScreenFader.LoadScene(ad): önce kararır, sonra sahneyi yükler (menü/lobi geçişleri).
/// </summary>
public class ScreenFader : MonoBehaviour
{
    private const float FadeOutSeconds = 0.35f;
    private const float FadeInSeconds = 0.7f;

    private static ScreenFader instance;
    private Image image;
    private float alpha;
    private float target;
    private float speed = 1f;
    private bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ScreenFader");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenFader>();
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var imgGo = new GameObject("Black");
        imgGo.transform.SetParent(go.transform, false);
        instance.image = imgGo.AddComponent<Image>();
        instance.image.color = Color.black;
        instance.image.raycastTarget = false;
        var rt = instance.image.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        instance.alpha = 1f;
        instance.SetTarget(0f, FadeInSeconds);
        SceneManager.sceneLoaded += (s, m) => { if (m == LoadSceneMode.Single && instance != null) instance.OnSceneLoaded(); };
    }

    private void OnSceneLoaded()
    {
        busy = false;
        alpha = Mathf.Max(alpha, 0.85f); // ağ yüklemelerinde de yumuşak açılış
        SetTarget(0f, FadeInSeconds);
    }

    private void SetTarget(float a, float seconds)
    {
        target = a;
        speed = 1f / Mathf.Max(0.01f, seconds);
    }

    private void Update()
    {
        alpha = Mathf.MoveTowards(alpha, target, speed * Time.unscaledDeltaTime);
        image.color = new Color(0f, 0f, 0f, alpha);
        image.enabled = alpha > 0.001f;
    }

    /// <summary>Kararıp sahneyi yükler. Fader yoksa doğrudan yükler.</summary>
    public static void LoadScene(string sceneName)
    {
        if (instance == null) { SceneManager.LoadScene(sceneName); return; }
        if (instance.busy) return;
        instance.StartCoroutine(instance.FadeThenLoad(sceneName));
    }

    private IEnumerator FadeThenLoad(string sceneName)
    {
        busy = true;
        SetTarget(1f, FadeOutSeconds);
        while (alpha < 0.999f) yield return null;
        SceneManager.LoadScene(sceneName);
    }
}
