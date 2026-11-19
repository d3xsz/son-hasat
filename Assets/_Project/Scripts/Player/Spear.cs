using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G-19: Mızrak (GDD 5.5: silah değil, çok amaçlı alet). Yalnızca sahipte çalışır (NetworkPlayer sahibe özel listesinde).
/// Mızrak seçili slottayken sol tık ile dürtülür: kameradan kısa menzilli raycast; ISpearTarget bulursa sunucuya bildirir.
/// Ağır eşya taşırken kullanılamaz (iki el dolu). Görsel: kısa ileri-geri dürtme animasyonlu viewmodel (placeholder).
/// </summary>
public class Spear : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private GameObject viewModel;
    [SerializeField] private ItemData spearItem;

    [Header("Ayarlar")]
    [SerializeField] private float reach = 3f;
    [SerializeField] private float cooldown = 0.6f;
    [SerializeField] private float jabDistance = 0.45f;
    [SerializeField] private float jabDuration = 0.22f;

    private InputAction attackAction;
    private InventorySystem inventory;
    private PlayerLife life;
    private SpearNetworkSync net;
    private PlayerFeel feel; // C-03: vuruş efekti + kamera tepmesi
    private Vector3 viewRestPosition;
    private float nextAttack;
    private float jabTimer = -1f;
    private bool wasLocked;

    private void Awake()
    {
        attackAction = inputActions.FindActionMap("Player", true).FindAction("Attack", true);
        inventory = GetComponent<InventorySystem>();
        life = GetComponent<PlayerLife>();
        net = GetComponent<SpearNetworkSync>();
        feel = GetComponent<PlayerFeel>();
        if (viewModel != null) viewRestPosition = viewModel.transform.localPosition;
    }

    private void OnEnable() { attackAction.Enable(); }

    private void OnDisable()
    {
        attackAction.Disable();
        if (viewModel != null) viewModel.SetActive(false);
    }

    private bool Holding => inventory != null && inventory.SelectedItem == spearItem && spearItem != null;
    private bool CanAct => life == null || life.CanAct;

    private void Update()
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool heavy = inventory != null && inventory.IsCarryingHeavy;

        if (viewModel != null) viewModel.SetActive(Holding && CanAct && !heavy);

        // İmleci tekrar kilitleyen tıklama dürtme sayılmasın
        if (attackAction.WasPressedThisFrame() && locked && wasLocked && Holding && CanAct)
        {
            if (heavy) inventory.ShowMessage("İki elin dolu: mızrak kullanılamaz");
            else if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + cooldown;
                jabTimer = 0f;
                Jab();
            }
        }

        AnimateJab();
        wasLocked = locked;
    }

    private void Jab()
    {
        if (net == null || rayOrigin == null) return;
        net.BroadcastJab(); // G-35: diğer oyuncular dürtme hareketini görsün
        if (feel != null) feel.Kick(new Vector3(-0.6f, 0f, 0f));
        Vector3 origin = rayOrigin.position;
        Vector3 dir = rayOrigin.forward;
        if (Sfx.Lib != null) Sfx.At(Sfx.Lib.spearWhoosh, origin + dir * 0.5f, 0.4f, 1.15f, 0.1f, 1f, 15f);
        Noise.Emit(origin, Noise.SpearSwing);

        RaycastHit[] hits = Physics.RaycastAll(origin, dir, reach, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue; // kendi gövdem

            ISpearTarget target = hit.collider.GetComponentInParent<ISpearTarget>();
            if (target != null)
            {
                NetworkObject no = hit.collider.GetComponentInParent<NetworkObject>();
                if (no != null && no.IsSpawned) net.RequestHit(no, hit.point, dir);
                if (feel != null) feel.OnSpearImpact(hit.point, -dir, true);
                if (Sfx.Lib != null) Sfx.At(Sfx.Lib.spearHit, hit.point, 0.75f, 1.05f, 0.08f, 1.5f, 20f);
                return;
            }
            if (!hit.collider.isTrigger) // duvar/engel mızrağı durdurur
            {
                if (feel != null) feel.OnSpearImpact(hit.point, hit.normal, false);
                if (Sfx.Lib != null) Sfx.At(Sfx.Lib.spearHit, hit.point, 0.45f, 0.75f, 0.08f, 1.5f, 15f);
                Noise.Emit(hit.point, Noise.SpearHit);
                return;
            }
        }
    }

    private void AnimateJab()
    {
        if (viewModel == null) return;
        if (jabTimer < 0f)
        {
            viewModel.transform.localPosition = viewRestPosition;
            return;
        }
        float t = jabTimer / jabDuration;
        float offset = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * jabDistance;
        viewModel.transform.localPosition = viewRestPosition + new Vector3(0f, 0f, offset);
        jabTimer += Time.deltaTime;
        if (t >= 1f) jabTimer = -1f;
    }
}
