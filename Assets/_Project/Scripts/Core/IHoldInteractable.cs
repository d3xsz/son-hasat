/// <summary>
/// G-17: E tuşuna BASILI TUTARAK etkileşilen nesneler (bayılan oyuncuyu kaldırma, ileride totem diriltme).
/// PlayerInteractor, HoldDuration dolana kadar basılı tutulmasını bekler, dolunca Interact çağırır.
/// </summary>
public interface IHoldInteractable : IInteractable
{
    /// <summary>Etkileşimin tamamlanması için E'nin basılı tutulması gereken süre (sn).</summary>
    float HoldDuration { get; }
}
