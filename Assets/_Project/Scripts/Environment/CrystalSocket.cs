using UnityEngine;

/// <summary>
/// B2-10 (KULLANICI, 2026-11-09): Labirentteki taş kristal yuvası. Önünde yalnızca FENER IŞIĞINDA görünen 1/2/3 çizgi
/// (sıra) var. Elinde kristal parçası olan oyuncu E ile parçayı yuvaya takar (CrystalSocketPuzzle sunucuda doğrular).
/// Dolu yuvada kristal görünür ve parlar; yanlış sırada hepsi kırmızı titreyip parçaları dışarı atar.
/// Görsel her kopyada yerel; dolu/boş durumu bulmacanın ağ değişkeninden okunur. Ekranda ipucu yazısı yok.
/// </summary>
public class CrystalSocket : MonoBehaviour, IInteractable
{
    [Tooltip("Bu yuvanın sıradaki yeri (1 = ilk). Çizgi sayısı bununla aynı olmalı.")]
    [SerializeField] private int order = 1;
    [SerializeField] private Renderer[] marks = new Renderer[0];
    [Tooltip("Yuva dolunca görünen kristal.")]
    [SerializeField] private GameObject crystalVisual;
    [SerializeField] private Light glow;
    [SerializeField, ColorUsage(false, true)] private Color markColor = new Color(0.55f, 1.6f, 1.8f);
    [SerializeField, ColorUsage(false, true)] private Color wrongColor = new Color(2.2f, 0.25f, 0.15f);
    [SerializeField] private float lightRange = 14f;
    [SerializeField] private float lightHalfAngle = 24f;

    private CrystalSocketPuzzle puzzle;
    private int index;
    private MaterialPropertyBlock block;
    private float reveal;
    private float wrongTimer;
    private float filledShown;
    private float phase;

    public int Order => order;

    public void Bind(CrystalSocketPuzzle owner, int socketIndex) { puzzle = owner; index = socketIndex; }

    private bool Filled => puzzle != null && puzzle.IsFilled(index);

    public string InteractionPrompt => "Kristali yerleştir";

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (puzzle == null || puzzle.Solved || Filled || interactor == null) return false;
        var inv = interactor.GetComponent<InventorySystem>();
        return inv != null && puzzle.CrystalItem != null && inv.SelectedItem == puzzle.CrystalItem;
    }

    public void Interact(PlayerInteractor interactor) { if (puzzle != null) puzzle.RequestPlace(index); }

    public void FlashWrong() { wrongTimer = 0.9f; }

    private void Start()
    {
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
        if (crystalVisual != null) crystalVisual.SetActive(false);
        if (glow != null) glow.enabled = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool lit = marks.Length > 0 && marks[0] != null && FlashlightReveal.IsLit(marks[0].bounds.center, lightRange, lightHalfAngle);
        reveal = Mathf.MoveTowards(reveal, lit ? 1f : 0f, dt * (lit ? 4f : 1.5f));
        wrongTimer = Mathf.Max(0f, wrongTimer - dt);
        float wrong = wrongTimer > 0f ? Mathf.Abs(Mathf.Sin(wrongTimer * 18f)) * Mathf.Clamp01(wrongTimer / 0.3f) : 0f;

        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 3f, phase);
        float a = Mathf.SmoothStep(0f, 1f, reveal);
        foreach (Renderer m in marks)
        {
            if (m == null) continue;
            bool show = a > 0.01f || wrong > 0.01f;
            if (m.enabled != show) m.enabled = show;
            if (!show) continue;
            m.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Max(a, wrong) * 0.95f));
            block.SetColor("_EmissionColor", wrong > 0.01f ? wrongColor * wrong : markColor * (a * flicker));
            m.SetPropertyBlock(block);
        }

        filledShown = Mathf.MoveTowards(filledShown, Filled ? 1f : 0f, dt * 2f);
        bool vis = filledShown > 0.01f;
        if (crystalVisual != null && crystalVisual.activeSelf != vis) crystalVisual.SetActive(vis);
        if (crystalVisual != null && vis) crystalVisual.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, filledShown);
        if (glow != null)
        {
            glow.enabled = vis || wrong > 0.01f;
            glow.color = wrong > 0.01f ? new Color(1f, 0.25f, 0.2f) : new Color(0.3f, 0.8f, 1f);
            glow.intensity = (vis ? 2.2f * filledShown * (0.9f + 0.1f * Mathf.Sin(Time.time * 2f + phase)) : 0f) + wrong * 2f;
        }
    }
}
