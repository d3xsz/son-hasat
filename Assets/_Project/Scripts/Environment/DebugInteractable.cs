using UnityEngine;

/// <summary>
/// G-04 test nesnesi: E'ye basınca renk değiştirir ve Console'a yazar.
/// singleUse açıksa bir kez kullanılınca CanInteract false olur (ipucu kaybolur).
/// </summary>
public class DebugInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string prompt = "Etkileşim";
    [SerializeField] private bool singleUse = false;
    [SerializeField] private Color colorA = new Color(0.55f, 0.55f, 0.58f);
    [SerializeField] private Color colorB = new Color(1f, 0.55f, 0.1f);

    private Renderer rend;
    private MaterialPropertyBlock block;
    private bool toggled;
    private bool used;

    public string InteractionPrompt => prompt;

    public bool CanInteract(PlayerInteractor interactor) { return !(singleUse && used); }

    public void Interact(PlayerInteractor interactor)
    {
        used = true;
        toggled = !toggled;
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (block == null) block = new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetColor("_BaseColor", toggled ? colorB : colorA);
        rend.SetPropertyBlock(block);
        Debug.Log("[Etkileşim] " + name + " <- " + interactor.name);
    }
}
