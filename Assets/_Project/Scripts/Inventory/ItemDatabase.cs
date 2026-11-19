using UnityEngine;

/// <summary>
/// G-12: Tüm eşyaların listesi. Ağda eşyalar bu listedeki sıra numarasıyla (kimlik) anlaşılır.
/// Yeni eşya eklenince listenin SONUNA eklenmeli (sıra değişirse kimlikler kayar).
/// </summary>
[CreateAssetMenu(menuName = "Sisli Ada/Item Database", fileName = "ItemDatabase")]
public class ItemDatabase : ScriptableObject
{
    [SerializeField] private ItemData[] items;

    public ItemData Get(int id)
    {
        return (items != null && id >= 0 && id < items.Length) ? items[id] : null;
    }

    public int IdOf(ItemData item)
    {
        if (item == null || items == null) return -1;
        for (int i = 0; i < items.Length; i++) if (items[i] == item) return i;
        return -1;
    }
}
