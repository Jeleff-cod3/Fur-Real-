using UnityEngine;

public class TreeSpiderSenses : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private float targetRefreshInterval = 0.35f;

    [Header("Ranges")]
    [SerializeField] private float detectionRange = 28f;
    [SerializeField] private float chaseLoseRange = 36f;
    [SerializeField] private float biteRange = 1.75f;
    [SerializeField] private float grabRange = 2.4f;

    [Header("Vision")]
    [SerializeField] private float fieldOfView = 180f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    private TreeSpiderState state;
    private float nextTargetRefreshTime;
    private float previousTreeDistance = -1f;

    public Transform Target => target;
    public bool HasTarget => target != null;
    public float DistanceToTarget { get; private set; }
    public Vector3 DirectionToTarget { get; private set; }
    public bool CanSeeTarget { get; private set; }
    public bool IsTargetInBiteRange { get; private set; }
    public bool IsTargetInGrabRange { get; private set; }
    public bool IsTargetWithinChaseRange { get; private set; }
    public bool IsTargetNearHiddenTree { get; private set; }
    public bool IsTargetDirectlyUnderTree { get; private set; }
    public bool IsTargetClosingOnTree { get; private set; }
    public float TargetDistanceToHiddenTree { get; private set; }

    private void Awake()
    {
        state = GetComponent<TreeSpiderState>();
    }

    private void Update()
    {
        if (!IsTargetValid(target) || Time.time >= nextTargetRefreshTime)
        {
            FindTarget();
        }

        UpdateSenses();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        nextTargetRefreshTime = Time.time + targetRefreshInterval;
    }

    private void FindTarget()
    {
        Transform runtimeTarget = MultiplayerPrototype.GetClosestPlayerTransform(transform.position);
        SetTarget(IsTargetValid(runtimeTarget) ? runtimeTarget : null);
    }

    private void UpdateSenses()
    {
        if (!IsTargetValid(target))
        {
            target = null;
            DistanceToTarget = float.PositiveInfinity;
            DirectionToTarget = transform.forward;
            CanSeeTarget = false;
            IsTargetInBiteRange = false;
            IsTargetInGrabRange = false;
            IsTargetWithinChaseRange = false;
            IsTargetNearHiddenTree = false;
            IsTargetDirectlyUnderTree = false;
            IsTargetClosingOnTree = false;
            TargetDistanceToHiddenTree = float.PositiveInfinity;
            previousTreeDistance = -1f;
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        DistanceToTarget = toTarget.magnitude;
        DirectionToTarget = DistanceToTarget > 0.001f ? toTarget.normalized : transform.forward;
        IsTargetInBiteRange = DistanceToTarget <= biteRange;
        IsTargetInGrabRange = DistanceToTarget <= grabRange;
        IsTargetWithinChaseRange = DistanceToTarget <= chaseLoseRange;

        float angle = Vector3.Angle(transform.forward, DirectionToTarget);
        CanSeeTarget = DistanceToTarget <= detectionRange &&
            angle <= fieldOfView * 0.5f &&
            HasLineOfSight();

        UpdateHiddenTreeAwareness();
    }

    private void UpdateHiddenTreeAwareness()
    {
        IsTargetNearHiddenTree = false;
        IsTargetDirectlyUnderTree = false;
        IsTargetClosingOnTree = false;
        TargetDistanceToHiddenTree = float.PositiveInfinity;

        if (state == null || !state.isHidden || state.currentTreeIndex < 0 || target == null)
        {
            previousTreeDistance = -1f;
            return;
        }

        Vector3 treeToTarget = target.position - state.currentTreeAnchor.trunkBasePosition;
        treeToTarget.y = 0f;
        TargetDistanceToHiddenTree = treeToTarget.magnitude;
        IsTargetNearHiddenTree = TargetDistanceToHiddenTree <= state.currentTreeAnchor.detectionRadius;
        IsTargetDirectlyUnderTree = TargetDistanceToHiddenTree <= state.currentTreeAnchor.directDropRadius;

        if (previousTreeDistance >= 0f)
        {
            IsTargetClosingOnTree = TargetDistanceToHiddenTree < previousTreeDistance - 0.1f;
        }

        previousTreeDistance = TargetDistanceToHiddenTree;
    }

    private bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 destination = target.position + Vector3.up * 0.6f;
        Vector3 direction = destination - origin;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude, lineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        return true;
    }

    private static bool IsTargetValid(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }
}
