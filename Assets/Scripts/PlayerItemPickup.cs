using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerItemPickup : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private string itemHolderName = "ItemHolder";
    [SerializeField] private Vector3 itemHolderLocalPosition = new Vector3(0.35f, 0.22f, 0.4f);
    [SerializeField] private float pickupRangeRadius = 1.5f;

    [Header("Drop")]
    [SerializeField] private float dropDistance = 1.05f;
    [SerializeField] private float dropHeight = 0.6f;
    [SerializeField] private float groundProbeHeight = 8f;
    [SerializeField] private LayerMask dropGroundLayers = ~0;

    private readonly HashSet<PickupableItem> nearbyItems = new HashSet<PickupableItem>();

    private Transform itemHolder;
    private SphereCollider pickupTrigger;
    private PlayerCarryController carryController;
    private PickupableItem heldItem;

    public PickupableItem HeldItem => heldItem;
    public bool HasItem => heldItem != null;

    private void Awake()
    {
        EnsureSetup();
    }

    public void Initialize(Transform holder)
    {
        itemHolder = holder;
        EnsureSetup();
    }

    private void Update()
    {
        nearbyItems.RemoveWhere(item => item == null || !item.CanBePickedUpFromWorld);

        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
        {
            return;
        }

        if (carryController != null && carryController.WasInteractHandledThisFrame)
        {
            return;
        }

        if (heldItem != null)
        {
            if (carryController != null)
            {
                carryController.MarkInteractHandled();
            }

            DropHeldItem();
            return;
        }

        PickupableItem closestItem = GetClosestNearbyItem();
        if (closestItem == null)
        {
            return;
        }

        if (carryController != null && !carryController.TryClaim(closestItem))
        {
            return;
        }

        if (carryController != null)
        {
            carryController.MarkInteractHandled();
        }

        PickUpItem(closestItem);
    }

    private void PickUpItem(PickupableItem item)
    {
        EnsureSetup();

        if (itemHolder == null || item == null)
        {
            return;
        }

        heldItem = item;
        heldItem.PickUp(itemHolder);
        nearbyItems.Remove(item);
    }

    private void DropHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        ResolveDropPose(out Vector3 dropPosition, out Quaternion dropRotation);
        PickupableItem itemToDrop = heldItem;
        heldItem = null;
        carryController?.ReleaseIfMatches(itemToDrop);
        itemToDrop.Drop(dropPosition, dropRotation);
    }

    private void ResolveDropPose(out Vector3 dropPosition, out Quaternion dropRotation)
    {
        Vector3 forward = transform.forward;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        Vector3 desiredPosition = transform.position + forward.normalized * dropDistance + Vector3.up * dropHeight;
        Vector3 rayOrigin = desiredPosition + Vector3.up * groundProbeHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                groundProbeHeight * 2f,
                dropGroundLayers,
                QueryTriggerInteraction.Ignore))
        {
            dropPosition = hit.point + Vector3.up * dropHeight;
        }
        else
        {
            dropPosition = desiredPosition;
        }

        Vector3 flattenedForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (flattenedForward.sqrMagnitude < 0.001f)
        {
            flattenedForward = Vector3.forward;
        }

        dropRotation = Quaternion.LookRotation(flattenedForward.normalized, Vector3.up);
    }

    private PickupableItem GetClosestNearbyItem()
    {
        nearbyItems.RemoveWhere(item => item == null || !item.CanBePickedUpFromWorld);

        PickupableItem closest = null;
        float closestDistance = float.MaxValue;

        foreach (PickupableItem item in nearbyItems)
        {
            float distance = (item.transform.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = item;
            }
        }

        return closest;
    }

    private void OnTriggerEnter(Collider other)
    {
        PickupableItem item = other.GetComponent<PickupableItem>();
        if (item != null && item.CanBePickedUpFromWorld)
        {
            nearbyItems.Add(item);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PickupableItem item = other.GetComponent<PickupableItem>();
        if (item != null)
        {
            nearbyItems.Remove(item);
        }
    }

    private void EnsureSetup()
    {
        if (carryController == null)
        {
            carryController = GetComponent<PlayerCarryController>();
            if (carryController == null)
            {
                carryController = gameObject.AddComponent<PlayerCarryController>();
            }
        }

        if (itemHolder == null)
        {
            itemHolder = FindOrCreateChild(itemHolderName, itemHolderLocalPosition);
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
