using System.Collections.Generic;
using UnityEngine;

public class SpearDamageHitbox : MonoBehaviour
{
    [SerializeField] private PickupableWeapon weapon;

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

        if (weapon.ShouldIgnoreCollider(other))
        {
            return;
        }

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
}
