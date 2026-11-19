using UnityEngine;

/// <summary>
/// G-06/G-07: Eşya tanımı. Envanterdeki ve yerdeki her eşya bir ItemData'ya bağlıdır.
/// isHeavy: ağır eşya (hız düşer, koşu ve mızrak yok, aynı anda en fazla 1). weight: ayarlanabilir ağırlık değeri.
/// isLocked: envanterden bırakılamaz (el feneri).
/// </summary>
[CreateAssetMenu(menuName = "Sisli Ada/Item Data", fileName = "Item_New")]
public class ItemData : ScriptableObject
{
    [SerializeField] private string displayName = "Eşya";
    [SerializeField] private Sprite icon;
    [Tooltip("Yere bırakılınca oluşturulacak prefab (üzerinde PickupItem + Rigidbody olmalı).")]
    [SerializeField] private GameObject worldPrefab;
    [SerializeField] private bool isHeavy = false;
    [SerializeField] private float weight = 1f;
    [Tooltip("Kilitli eşya envanterden bırakılamaz (örn. el feneri).")]
    [SerializeField] private bool isLocked = false;
    [Tooltip("Tüketilebilir (panzehir/iksir): seçiliyken sol tık ile kullanılır, envanterden düşer.")]
    [SerializeField] private bool isConsumable = false;
    [Tooltip("Tüketilince düşen zehir miktarı (0-100).")]
    [SerializeField] private float poisonReduction = 0f;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public GameObject WorldPrefab => worldPrefab;
    public bool IsHeavy => isHeavy;
    public float Weight => weight;
    public bool IsLocked => isLocked;
    public bool IsConsumable => isConsumable;
    public float PoisonReduction => poisonReduction;
}
