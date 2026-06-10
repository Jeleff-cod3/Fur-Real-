using System.Collections.Generic;
using UnityEngine;

public class SpearDamageHitbox : MonoBehaviour
{
    [SerializeField] private PickupableWeapon weapon;

<<<<<<< HEAD
=======
    private readonly HashSet<Component> damagedTargets = new HashSet<Component>();
>>>>>>> 4e613ad (woreking mammoth and shit)
    private Collider hitboxCollider;
    private bool canDamage;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();

        if (hitboxCollider != null)
        {
            hitboxCollider.isTrigger = true;
            hitboxCollider.enabled = false;
        }

        if (weapon == null)
        {
            weapon = GetComponentInParent<PickupableWeapon>();
        }
    }

    private void Update()
    {
        if (canDamage)
        {
            ScanForDamage();
        }
    }

    public void StartDamageWindow()
    {
<<<<<<< HEAD
=======
        damagedTargets.Clear();
>>>>>>> 4e613ad (woreking mammoth and shit)
        canDamage = true;

        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = true;
        }
    }

    public void StopDamageWindow()
    {
        canDamage = false;

        if (hitboxCollider != null)
        {
            hitboxCollider.enabled = false;
        }

<<<<<<< HEAD
=======
        damagedTargets.Clear();
>>>>>>> 4e613ad (woreking mammoth and shit)
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDamage(other);
    }

    private void TryDamage(Collider other)
    {
        if (!canDamage || weapon == null)
        {
            return;
        }

<<<<<<< HEAD
        if (weapon.ShouldIgnoreCollider(other))
=======
        Component damageableComponent = other.GetComponent(typeof(IDamageable)) as Component;

        if (damageableComponent == null)
        {
            damageableComponent = other.GetComponentInParent(typeof(IDamageable)) as Component;
        }

        if (damageableComponent == null || damageableComponent.transform.IsChildOf(weapon.transform))
>>>>>>> 4e613ad (woreking mammoth and shit)
        {
            return;
        }

<<<<<<< HEAD
        weapon.TryRegisterMeleeContact(other);
    }

    private void ScanForDamage()
    {
        if (!(hitboxCollider is SphereCollider sphereCollider))
        {
            return;
        }

        Vector3 worldCenter = sphereCollider.transform.TransformPoint(sphereCollider.center);
        float maxScale = Mathf.Max(
            Mathf.Abs(sphereCollider.transform.lossyScale.x),
            Mathf.Abs(sphereCollider.transform.lossyScale.y),
            Mathf.Abs(sphereCollider.transform.lossyScale.z)
        );
        float worldRadius = Mathf.Max(
            sphereCollider.radius * maxScale,
            weapon != null ? weapon.TipCastRadius : 0f
        );

        Collider[] overlaps = Physics.OverlapSphere(
            worldCenter,
            worldRadius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider overlap in overlaps)
        {
            TryDamage(overlap);
        }
    }
=======
        if (damagedTargets.Contains(damageableComponent))
        {
            return;
        }

        if (!(damageableComponent is IDamageable damageable))
        {
            return;
        }

        damagedTargets.Add(damageableComponent);
        damageable.TakeDamage(weapon.Damage);

        Debug.Log($"Spear tip hit {damageableComponent.name} for {weapon.Damage} damage.");
    }
>>>>>>> 4e613ad (woreking mammoth and shit)
}
