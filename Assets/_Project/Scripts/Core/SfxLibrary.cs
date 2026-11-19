using UnityEngine;

/// <summary>KULLANICI (cila): Aksiyon ses klipleri (Resources/Audio/SfxLibrary.asset). Kullanıcı kendi seslerini buraya atayabilir.</summary>
[CreateAssetMenu(menuName = "Goblin/Sfx Library", fileName = "SfxLibrary")]
public class SfxLibrary : ScriptableObject
{
    [Header("Oyuncu")]
    public AudioClip[] stepWalk;
    public AudioClip[] stepRun;
    [Tooltip("Taş zemin (mağara) adımları — BigSoundBank #0606 / #0514, CC0.")]
    public AudioClip[] stepStoneWalk;
    public AudioClip[] stepStoneRun;
    public AudioClip[] jumpLand;
    public AudioClip[] spearWhoosh;
    public AudioClip[] spearHit;
    public AudioClip[] pickup;
    public AudioClip[] flashlightClick;

    [Header("Dünya")]
    public AudioClip[] totemTake;
    public AudioClip[] rockHit;
    public AudioClip[] rumble;
    public AudioClip[] doorUnlock;
    public AudioClip[] doorCreak;
    public AudioClip[] treeCrash;
    public AudioClip[] splash;

    [Header("Sinematik")]
    public AudioClip seaWaves;

    [Header("Mağara")]
    public AudioClip caveAmbience;
    public AudioClip[] drips;

    [Header("Mağara düşüşü")]
    public AudioClip fallWind;
    public AudioClip[] heartbeat;
    public AudioClip[] impactBoom;

    [Header("Örümcek")]
    public AudioClip[] spiderAttack;
    public AudioClip[] spiderDeath;

    [Header("Arayüz")]
    public AudioClip[] uiClick;
}
