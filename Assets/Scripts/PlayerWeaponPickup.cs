using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerWeaponPickup : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private string weaponHolderName = "WeaponHolder";
    [SerializeField] private Vector3 weaponHolderLocalPosition = new Vector3(0.45f, 0.35f, 0.55f);
    [SerializeField] private float pickupRangeRadius = 1.5f;

    private Transform weaponHolder;
    private SphereCollider pickupTrigger;

    private PickupableWeapon nearbyWeapon;
    private PickupableWeapon equippedWeapon;

    public PickupableWeapon EquippedWeapon => equippedWeapon;
    public bool HasWeapon => equippedWeapon != null;

    private void Awake()
    {
        EnsureSetup();
    }

    public void Initialize(Transform holder)
    {
        weaponHolder = holder;
        EnsurePickupTrigger();
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (equippedWeapon == null && nearbyWeapon != null)
            {
                PickUpWeapon();
            }
            else if (equippedWeapon != null)
            {
                DropWeapon();
            }
        }
    }

    private void PickUpWeapon()
    {
        EnsureSetup();

        if (weaponHolder == null)
        {
            Debug.LogWarning("Cannot pick up weapon because WeaponHolder is missing.");
            return;
        }

        equippedWeapon = nearbyWeapon;
        equippedWeapon.PickUp(weaponHolder);
        nearbyWeapon = null;

        Debug.Log("Weapon picked up.");
    }

    private void DropWeapon()
    {
        equippedWeapon.Drop();
        equippedWeapon = null;

        Debug.Log("Weapon dropped.");
    }

    public void ThrowEquippedWeapon(Vector3 direction)
    {
        if (equippedWeapon == null)
        {
            return;
        }

        PickupableWeapon thrownWeapon = equippedWeapon;
        equippedWeapon = null;

        thrownWeapon.Throw(direction);
    }

    public void ClearEquippedWeaponIfMatches(PickupableWeapon weapon)
    {
        if (weapon != null && equippedWeapon == weapon)
        {
            equippedWeapon = null;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PickupableWeapon weapon = other.GetComponent<PickupableWeapon>();

        if (weapon != null && equippedWeapon == null && !weapon.IsBroken)
        {
            nearbyWeapon = weapon;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PickupableWeapon weapon = other.GetComponent<PickupableWeapon>();

        if (weapon != null && weapon == nearbyWeapon)
        {
            nearbyWeapon = null;
        }
    }

    private void EnsureSetup()
    {
        if (weaponHolder == null)
        {
            weaponHolder = FindOrCreateChild(weaponHolderName, weaponHolderLocalPosition);
        }

        EnsurePickupTrigger();
    }

    private void EnsurePickupTrigger()
    {
        if (pickupTrigger == null)
        {
            SphereCollider[] sphereColliders = GetComponents<SphereCollider>();

            foreach (SphereCollider sphereCollider in sphereColliders)
            {
                if (sphereCollider.isTrigger)
                {
                    pickupTrigger = sphereCollider;
                    break;
                }
            }
        }

        if (pickupTrigger == null)
        {
            pickupTrigger = gameObject.AddComponent<SphereCollider>();
        }

        pickupTrigger.isTrigger = true;
        pickupTrigger.radius = pickupRangeRadius;
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
