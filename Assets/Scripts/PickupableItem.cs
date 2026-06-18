using System;
using UnityEngine;

public class PickupableItem : MonoBehaviour
{
    private enum ItemState
    {
        World,
        Held,
        Consumed
    }

    [SerializeField] private PickupItemType itemType = PickupItemType.Stick;
    [SerializeField] private Vector3 heldLocalPositionOffset = new Vector3(0.35f, 0.18f, 0.38f);
    [SerializeField] private Vector3 heldLocalEulerAngles = new Vector3(10f, 35f, 85f);
    [SerializeField] private float worldHoverOffset = 0.02f;
    [SerializeField] private float droppedAngularVelocityStrength = 3f;
    [SerializeField] private bool settleDroppedItemsImmediately = true;
    [SerializeField] private float dropGroundProbeHeight = 8f;
    [SerializeField] private LayerMask dropGroundLayers = ~0;

    private Rigidbody rb;
    private Collider mainCollider;
    private ItemState state = ItemState.World;
    private bool wasDroppedByPlayer;
    private bool hasBeenHeld;
    private bool hasNotifiedRemovedFromWorldSupply;

    public PickupItemType ItemType => itemType;
    public bool IsHeld => state == ItemState.Held;
    public bool CanBePickedUpFromWorld => state == ItemState.World;
    public bool CanBeUsedForCrafting => state == ItemState.World && wasDroppedByPlayer;

    public event Action<PickupableItem> RemovedFromWorldSupply;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mainCollider = GetComponent<Collider>();
        SetupWorldPhysics();
    }

    private void Start()
    {
        PickupHighlightVisual.EnsureAttached(gameObject);
    }

    public void Configure(PickupItemType newItemType, Vector3 heldPositionOffset, Vector3 heldEulerAngles)
    {
        itemType = newItemType;
        heldLocalPositionOffset = heldPositionOffset;
        heldLocalEulerAngles = heldEulerAngles;
    }

    public void SetWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.position = worldPosition + Vector3.up * worldHoverOffset;
        transform.rotation = worldRotation;
        wasDroppedByPlayer = false;
        SetupWorldPhysics();
    }

    public void PickUp(Transform itemHolder)
    {
        if (state == ItemState.Consumed || itemHolder == null)
        {
            return;
        }

        state = ItemState.Held;
        hasBeenHeld = true;
        wasDroppedByPlayer = false;
        NotifyRemovedFromWorldSupplyOnce();

        transform.SetParent(itemHolder, false);
        transform.localPosition = heldLocalPositionOffset;
        transform.localRotation = Quaternion.Euler(heldLocalEulerAngles);

        FreezeRigidbody();

        if (mainCollider != null)
        {
            mainCollider.enabled = false;
            mainCollider.isTrigger = false;
        }
    }

    public void Drop(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (state == ItemState.Consumed)
        {
            return;
        }

        transform.SetParent(null, true);
        transform.position = settleDroppedItemsImmediately
            ? GetGroundedWorldPosition(worldPosition)
            : worldPosition + Vector3.up * worldHoverOffset;
        transform.rotation = worldRotation;
        state = ItemState.World;
        wasDroppedByPlayer = hasBeenHeld;

        if (mainCollider != null)
        {
            mainCollider.enabled = true;
            mainCollider.isTrigger = false;
        }

        if (settleDroppedItemsImmediately)
        {
            FreezeRigidbody();
        }
        else
        {
            EnableDropPhysics();
        }
    }

    public void Consume()
    {
        if (state == ItemState.Consumed)
        {
            return;
        }

        state = ItemState.Consumed;
        NotifyRemovedFromWorldSupplyOnce();
        Destroy(gameObject);
    }

    private void SetupWorldPhysics()
    {
        if (state == ItemState.Consumed)
        {
            return;
        }

        state = ItemState.World;

        if (mainCollider != null)
        {
            mainCollider.enabled = true;
            mainCollider.isTrigger = false;
        }

        FreezeRigidbody();
    }

    private void FreezeRigidbody()
    {
        if (rb == null)
        {
            return;
        }

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void EnableDropPhysics()
    {
        if (rb == null)
        {
            return;
        }

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * droppedAngularVelocityStrength;
    }

    private Vector3 GetGroundedWorldPosition(Vector3 worldPosition)
    {
        int mask = GetDropGroundMask();

        Vector3 origin = worldPosition + Vector3.up * Mathf.Max(0.1f, dropGroundProbeHeight);
        float distance = Mathf.Max(0.1f, dropGroundProbeHeight * 2f);

        if (Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                distance,
                mask,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + hit.normal * worldHoverOffset;
        }

        return worldPosition + Vector3.up * worldHoverOffset;
    }

    private int GetDropGroundMask()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            return 1 << groundLayer;
        }

        return dropGroundLayers.value != 0
            ? dropGroundLayers.value
            : Physics.DefaultRaycastLayers;
    }

    private void NotifyRemovedFromWorldSupplyOnce()
    {
        if (hasNotifiedRemovedFromWorldSupply)
        {
            return;
        }

        hasNotifiedRemovedFromWorldSupply = true;
        RemovedFromWorldSupply?.Invoke(this);
    }
}
