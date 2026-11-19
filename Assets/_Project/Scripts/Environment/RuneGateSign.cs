using UnityEngine;

/// <summary>
/// KULLANICI (cila): Kafes kapısının üstündeki rün tabelası. Her yuvada sırasıyla 1, 2, 3 çizgili oyma rün var
/// (her zaman soluk görünür: "bu kapı rünlerle açılır"). Bulmaca ilerledikçe ilgili yuva parlar; çözülünce hepsi
/// nabız gibi parlar. Yanlış dokunuşta kısa kırmızı titreme. Yazı yok. Yerel görsel (ilerleme zaten ağda).
/// </summary>
public class RuneGateSign : MonoBehaviour
{
    [SerializeField] private RuneSequencePuzzle puzzle;
    [Tooltip("Her yuva için o rünün parçaları (yuva 0 = 1 çizgi, yuva 1 = 2 çizgi …).")]
    [SerializeField] private Renderer[] slot0;
    [SerializeField] private Renderer[] slot1;
    [SerializeField] private Renderer[] slot2;
    [SerializeField, ColorUsage(false, true)] private Color dimColor = new Color(0.05f, 0.12f, 0.14f);
    [SerializeField, ColorUsage(false, true)] private Color litColor = new Color(0.45f, 1.5f, 1.7f);
    [SerializeField, ColorUsage(false, true)] private Color wrongColor = new Color(2f, 0.2f, 0.1f);

    private MaterialPropertyBlock block;
    private float[] level = new float[3];
    private float wrongTimer;

    private void OnEnable() { if (puzzle != null) puzzle.Wrong += OnWrong; }
    private void OnDisable() { if (puzzle != null) puzzle.Wrong -= OnWrong; }
    private void OnWrong() { wrongTimer = 0.8f; }

    private void Update()
    {
        if (block == null) block = new MaterialPropertyBlock();
        int progress = puzzle != null ? puzzle.Progress : 0;
        bool solved = puzzle != null && puzzle.Solved;
        wrongTimer = Mathf.Max(0f, wrongTimer - Time.deltaTime);
        float wrong = wrongTimer > 0f ? Mathf.Abs(Mathf.Sin(wrongTimer * 18f)) * Mathf.Clamp01(wrongTimer / 0.3f) : 0f;
        float pulse = solved ? 0.75f + 0.25f * Mathf.Sin(Time.time * 3f) : 1f;
        Apply(slot0, 0, progress, pulse, wrong);
        Apply(slot1, 1, progress, pulse, wrong);
        Apply(slot2, 2, progress, pulse, wrong);
    }

    private void Apply(Renderer[] parts, int i, int progress, float pulse, float wrong)
    {
        if (parts == null) return;
        level[i] = Mathf.MoveTowards(level[i], progress > i ? 1f : 0f, Time.deltaTime * 2.5f);
        Color c = Color.Lerp(dimColor, litColor * pulse, level[i]);
        c = Color.Lerp(c, wrongColor, wrong);
        foreach (Renderer r in parts)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.9f));
            block.SetColor("_EmissionColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
