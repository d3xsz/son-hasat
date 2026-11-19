using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public enum ObjectiveType
{
    /// <summary>Hayattaki herhangi bir oyuncu alana girer.</summary>
    ReachArea = 0,
    /// <summary>Alınan sabit totem sayısı hedefe ulaşır.</summary>
    TakeTotems = 1,
    /// <summary>Herhangi bir oyuncunun envanterinde eşya bulunur (bir kez yeter).</summary>
    CollectItem = 2,
    /// <summary>Başka bir sistem ServerComplete(id) çağırır (bulmacalar, G-32).</summary>
    Manual = 3
}

/// <summary>
/// G-31: Bölüm hedefleri (GDD 6.8). Hedefler inspector'da tanımlanır; sunucu değerlendirir ve
/// tamamlanma maskesi + totem sayısını NetworkVariable ile herkese gönderir. Her kopya HUD metnini üretir,
/// bir hedef tamamlanınca yerel oyuncuya mesaj gösterir. Tamamlanan hedef geri alınmaz (yeniden başlatmada da).
/// </summary>
public class ObjectiveSystem : NetworkBehaviour
{
    [Serializable]
    public class Objective
    {
        public string id = "hedef";
        public string text = "Hedef";
        public ObjectiveType type = ObjectiveType.ReachArea;
        [Tooltip("ReachArea: alan merkezi.")]
        public Transform area;
        [Tooltip("ReachArea: yatay yarıçap (m).")]
        public float radius = 8f;
        [Tooltip("TakeTotems: gereken totem sayısı.")]
        public int requiredCount = 5;
        [Tooltip("CollectItem: aranan eşya.")]
        public ItemData item;
        [Tooltip("Bu hedeflerin hepsi bitmeden görünmez/değerlendirilmez (id listesi).")]
        public string[] requires = new string[0];
        [Tooltip("Gizli hedef: bu kadar totem alınmadan görünmez/değerlendirilmez (0 = baştan görünür).")]
        public int revealAfterTotems = 0;
    }

    [SerializeField] private string header = "HEDEFLER";
    [Tooltip("Hedef tamamlanınca ekranda mesaj göster (kullanıcı kararı: kapalı; hedefler arka planda ilerlemeyi izler).")]
    [SerializeField] private bool announce = false;
    [SerializeField] private List<Objective> objectives = new List<Objective>();

    public static ObjectiveSystem Instance { get; private set; }

    /// <summary>Tüm kopyalarda: tamamlanan hedefin sırası.</summary>
    public event Action<int> ObjectiveCompleted;

    private readonly NetworkVariable<int> completedMask = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> totemsTaken = new NetworkVariable<int>(0);

    public string DisplayText { get; private set; } = "";
    public IReadOnlyList<Objective> Objectives => objectives;
    public bool IsCompleted(int index) => (completedMask.Value & (1 << index)) != 0;
    public bool AllCompleted => objectives.Count > 0 && completedMask.Value == (1 << objectives.Count) - 1;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        completedMask.OnValueChanged += OnMaskChanged;
        totemsTaken.OnValueChanged += OnTotemsChanged;
        if (IsServer)
        {
            Totem.TotemTaken += OnTotemTaken;
            int already = 0;
            foreach (Totem t in Totem.All) if (t != null && t.IsTaken) already++;
            totemsTaken.Value = already;
        }
        RebuildText();
    }

    private void OnTotemsChanged(int oldValue, int newValue) { RebuildText(); }

    public override void OnNetworkDespawn()
    {
        completedMask.OnValueChanged -= OnMaskChanged;
        totemsTaken.OnValueChanged -= OnTotemsChanged;
        Totem.TotemTaken -= OnTotemTaken;
        if (Instance == this) Instance = null;
    }

    // ---------- Sunucu ----------

    private void OnTotemTaken(Totem t) { totemsTaken.Value++; }

    /// <summary>Manual hedefleri (ve test için herhangi birini) tamamlar. Yalnızca sunucu.</summary>
    public void ServerComplete(string id)
    {
        if (!IsServer) return;
        int i = objectives.FindIndex(o => o.id == id);
        if (i >= 0) completedMask.Value |= 1 << i;
    }

    private bool RequirementsMet(Objective o, int mask)
    {
        if (totemsTaken.Value < o.revealAfterTotems) return false;
        if (o.requires == null) return true;
        foreach (string r in o.requires)
        {
            int j = objectives.FindIndex(x => x.id == r);
            if (j >= 0 && (mask & (1 << j)) == 0) return false;
        }
        return true;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerEvaluate();
    }

    private void ServerEvaluate()
    {
        int mask = completedMask.Value;
        int newMask = mask;
        for (int i = 0; i < objectives.Count && i < 31; i++)
        {
            if ((mask & (1 << i)) != 0) continue;
            Objective o = objectives[i];
            if (!RequirementsMet(o, mask)) continue;
            if (Check(o)) newMask |= 1 << i;
        }
        if (newMask != mask) completedMask.Value = newMask;
    }

    private bool Check(Objective o)
    {
        switch (o.type)
        {
            case ObjectiveType.ReachArea:
                if (o.area == null) return false;
                foreach (PlayerLife p in PlayerLife.All)
                {
                    if (p == null || p.State != PlayerLifeState.Alive) continue;
                    Vector3 d = p.transform.position - o.area.position;
                    d.y = 0f;
                    if (d.sqrMagnitude <= o.radius * o.radius && Mathf.Abs(p.transform.position.y - o.area.position.y) < 6f) return true;
                }
                return false;
            case ObjectiveType.TakeTotems:
                return totemsTaken.Value >= o.requiredCount;
            case ObjectiveType.CollectItem:
                if (o.item == null) return false;
                foreach (PlayerLife p in PlayerLife.All)
                {
                    if (p == null) continue;
                    InventoryNetworkSync inv = p.GetComponent<InventoryNetworkSync>();
                    if (inv != null && inv.HasItem(o.item)) return true;
                }
                return false;
            default:
                return false;
        }
    }

    // ---------- Tüm kopyalar ----------

    private void OnMaskChanged(int oldMask, int newMask)
    {
        for (int i = 0; i < objectives.Count; i++)
        {
            bool was = (oldMask & (1 << i)) != 0, now = (newMask & (1 << i)) != 0;
            if (was || !now) continue;
            ObjectiveCompleted?.Invoke(i);
            if (announce) ShowLocalMessage("Hedef tamamlandı: " + objectives[i].text);
        }
        RebuildText();
    }

    private static void ShowLocalMessage(string msg)
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null && nm.LocalClient != null ? nm.LocalClient.PlayerObject : null;
        InventorySystem inv = po != null ? po.GetComponent<InventorySystem>() : null;
        if (inv != null) inv.ShowMessage(msg);
    }

    private readonly StringBuilder sb = new StringBuilder();

    private void RebuildText()
    {
        int mask = completedMask.Value;
        sb.Length = 0;
        sb.Append(header);
        for (int i = 0; i < objectives.Count; i++)
        {
            Objective o = objectives[i];
            bool done = (mask & (1 << i)) != 0;
            if (!done && !RequirementsMet(o, mask)) continue;
            sb.Append('\n').Append(done ? "[x] " : "[  ] ").Append(o.text);
            if (!done && o.type == ObjectiveType.TakeTotems)
                sb.Append(" (").Append(Mathf.Min(totemsTaken.Value, o.requiredCount)).Append('/').Append(o.requiredCount).Append(')');
        }
        DisplayText = sb.ToString();
    }
}
