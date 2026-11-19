/// <summary>
/// G-04: Oyuncunun E tuşu ile etkileşime girebildiği her şey bunu uygular
/// (totem, mızrak yerde, düğme, kapı kolu vb.). Collider'ı olan objede ya da üst objesinde durmalı.
/// </summary>
public interface IInteractable
{
    /// <summary>Ekranda "[E] ..." yanında gösterilecek metin.</summary>
    string InteractionPrompt { get; }

    /// <summary>False ise oyuncu bu nesneye bakarken ipucu çıkmaz ve E işlemez.</summary>
    bool CanInteract(PlayerInteractor interactor);

    void Interact(PlayerInteractor interactor);
}
