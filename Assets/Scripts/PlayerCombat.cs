using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private string attackPointName = "AttackPoint";
    [SerializeField] private Vector3 attackPointLocalPosition = new Vector3(0f, 0.45f, 1.25f);
    [SerializeField] private string enemyLayerName = "Enemy";

    private PlayerWeaponPickup weaponPickup;
    private Transform attackPoint;
    private LayerMask enemyLayer;
    private PlayerMouseAim mouseAim;

    private float nextAttackTime;

    private void Awake()
    {
        EnsureSetup();
    }

    public void Initialize(PlayerWeaponPickup pickup, Transform point, LayerMask layer)
    {
        weaponPickup = pickup;
        attackPoint = point;
        enemyLayer = layer;
        EnsureSetup();
    }

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryMeleeAttack();
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            TryThrowWeapon();
        }
    }

    private void TryMeleeAttack()
    {
        if (weaponPickup == null || !weaponPickup.HasWeapon)
        {
            Debug.Log("No weapon equipped.");
            return;
        }

        PickupableWeapon weapon = weaponPickup.EquippedWeapon;

        if (Time.time < nextAttackTime)
        {
            return;
        }

        if (TryGetAimDirection(out Vector3 aimDirection))
        {
            FaceDirectionImmediately(aimDirection);
        }

        nextAttackTime = Time.time + weapon.AttackCooldown;

        weapon.StartMeleeAttack();

        Debug.Log("Spear attack started. Damage now depends on spear tip collision.");
    }

    private void TryThrowWeapon()
    {
        if (weaponPickup == null || !weaponPickup.HasWeapon)
        {
            return;
        }

        Vector3 throwDirection = transform.forward;

        if (TryGetAimDirection(out Vector3 aimedDirection))
        {
            throwDirection = aimedDirection;
            FaceDirectionImmediately(aimedDirection);
        }

        weaponPickup.ThrowEquippedWeapon(throwDirection);

        Debug.Log("Spear thrown.");
    }

    private void EnsureSetup()
    {
        if (weaponPickup == null)
        {
            weaponPickup = GetComponent<PlayerWeaponPickup>();

            if (weaponPickup == null)
            {
                weaponPickup = gameObject.AddComponent<PlayerWeaponPickup>();
            }
        }

        mouseAim = GetComponent<PlayerMouseAim>();

        if (mouseAim == null)
        {
            mouseAim = gameObject.AddComponent<PlayerMouseAim>();
        }

        if (attackPoint == null)
        {
            attackPoint = FindOrCreateChild(attackPointName, attackPointLocalPosition);
        }

        if (enemyLayer.value == 0)
        {
            enemyLayer = LayerMask.GetMask(enemyLayerName);
        }
    }

    private bool TryGetAimDirection(out Vector3 aimDirection)
    {
        if (mouseAim != null && mouseAim.TryGetAimDirection(out aimDirection, false))
        {
            return true;
        }

        aimDirection = Vector3.zero;
        return false;
    }

    private void FaceDirectionImmediately(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb != null && !rb.isKinematic)
        {
            rb.MoveRotation(targetRotation);
            return;
        }

        transform.rotation = targetRotation;
    }

    private Transform FindOrCreateChild(string childName, Vector3 localPosition)
    {
        Transform child = transform.Find(childName);

        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        child = childObject.transform;
        child.SetParent(transform, false);
        child.localPosition = localPosition;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child;
    }
}
