using UnityEngine;

/// <summary>
/// G-32: Fener izli direk. Çizgiler (marks) yalnızca herhangi bir oyuncunun açık fenerinin ışık konisinde ve
/// görüş hattındayken görünür (her kopyada yerel hesap; fener durumu ve bakış zaten senkron). E ile dokunulur.
/// KULLANICI (cila): çizgiler aniden belirmez; ışıkta yumuşakça parlayarak açılır, ışık çekilince yavaşça söner,
/// hafifçe titrer. Doğru dokunulunca başlık parlar; yanlış dokunuşta direkler kısa süre kırmızı titrer.
/// Çizgi materyali Goblin/FogResistUnlit (_BaseColor alfa + _EmissionColor).
/// </summary>
public class RunePillar : MonoBehaviour, IInteractable
{
    [Tooltip("Bu direğin sıradaki yeri (1 = ilk). Çizgi sayısı bununla aynı olmalı.")]
    [SerializeField] private int order = 1;
    [SerializeField] private Renderer[] marks = new Renderer[0];
    [Tooltip("Doğru dokunulunca parlayan parça (isteğe bağlı).")]
    [SerializeField] private Renderer glow;
    [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(0.2f, 1.2f, 1.4f);
    [SerializeField, ColorUsage(false, true)] private Color markColor = new Color(0.55f, 1.6f, 1.8f);
    [SerializeField, ColorUsage(false, true)] private Color wrongColor = new Color(2.2f, 0.25f, 0.15f);
    [SerializeField] private float lightRange = 14f;
    [SerializeField] private float lightHalfAngle = 24f;
    [SerializeField] private float fadeIn = 4f;
    [SerializeField] private float fadeOut = 1.5f;

    private RuneSequencePuzzle puzzle;
    private int index;
    private MaterialPropertyBlock block;
    private float reveal;
    private float wrongTimer;
    private float phase;
    private bool glowOn = true;

    public int Order => order;

    public void Bind(RuneSequencePuzzle owner, int pillarIndex)
    {
        puzzle = owner;
        index = pillarIndex;
    }

    public string InteractionPrompt => "Dokun";
    public bool CanInteract(PlayerInteractor interactor) => puzzle != null && !puzzle.Solved;
    public void Interact(PlayerInteractor interactor) { if (puzzle != null) puzzle.RequestTouch(index); }

    /// <summary>Yanlış dokunuş: kısa kırmızı titreme (tüm direklerde).</summary>
    public void FlashWrong() { wrongTimer = 0.8f; }

    private void Start()
    {
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
        ApplyMarks(0f, 0f);
        SetGlow(false);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool lit = IsLit();
        reveal = Mathf.MoveTowards(reveal, lit ? 1f : 0f, dt * (lit ? fadeIn : fadeOut));
        wrongTimer = Mathf.Max(0f, wrongTimer - dt);
        float wrong = wrongTimer > 0f ? Mathf.Abs(Mathf.Sin(wrongTimer * 18f)) * Mathf.Clamp01(wrongTimer / 0.3f) : 0f;
        ApplyMarks(reveal, wrong);
        SetGlow(puzzle != null && puzzle.Progress >= order, wrong);
    }

    private bool IsLit()
    {
        if (marks.Length == 0 || marks[0] == null) return false;
        return FlashlightReveal.IsLit(marks[0].bounds.center, lightRange, lightHalfAngle);
    }

    private void ApplyMarks(float r, float wrong)
    {
        if (block == null) return;
        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f, phase);
        float a = Mathf.SmoothStep(0f, 1f, r);
        Color e = Color.Lerp(markColor, wrongColor, wrong) * (a * flicker);
        foreach (Renderer m in marks)
        {
            if (m == null) continue;
            bool show = a > 0.01f || wrong > 0.01f;
            if (m.enabled != show) m.enabled = show;
            if (!show) continue;
            m.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Max(a, wrong) * 0.95f));
            block.SetColor("_EmissionColor", wrong > 0.01f ? wrongColor * wrong : e);
            m.SetPropertyBlock(block);
        }
    }

    private void SetGlow(bool on, float wrong = 0f)
    {
        if (glow == null || block == null) return;
        if (on == glowOn && wrong <= 0f && wrongTimer <= 0f) return;
        glowOn = on;
        glow.GetPropertyBlock(block);
        Color c = on ? glowColor * (0.8f + 0.2f * Mathf.Sin(Time.time * 2f + phase)) : Color.black;
        block.SetColor("_EmissionColor", Color.Lerp(c, wrongColor * 0.6f, wrong));
        glow.SetPropertyBlock(block);
    }
}
