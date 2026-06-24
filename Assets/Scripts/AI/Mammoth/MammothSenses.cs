using UnityEngine;

public class MammothSenses : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float targetRefreshInterval = 0.5f;

    [Header("Ranges")]
    [SerializeField] private float detectionRange = 35f;
    [SerializeField] private float chaseRange = 25f;
    [SerializeField] private float chargeRange = 16f;
    [SerializeField] private float normalAttackRange = 4f;
    [SerializeField] private float stompRange = 3f;
    [SerializeField] private float twistAttackRange = 4.5f;

    [Header("Vision")]
    [SerializeField] private float fieldOfView = 150f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    [Header("Hearing")]
    [SerializeField] private float hearingRange = 22f;
    [SerializeField] private float proximityHearingRange = 6f;
    [SerializeField] private float movementNoiseThreshold = 1.3f;
    [SerializeField] private float suspiciousSoundMemoryDuration = 5f;
    [SerializeField] private float suspiciousSoundRefreshInterval = 0.35f;

    public Transform Target => target;
    public float DistanceToTarget { get; private set; }
    public Vector3 DirectionToTarget { get; private set; }
    public float TargetMovementSpeed { get; private set; }
    public bool HasTarget => target != null;
    public bool CanSeeTarget { get; private set; }
    public bool CanHearTarget { get; private set; }
    public bool IsTargetDetected { get; private set; }
    public bool IsTargetInChaseRange { get; private set; }
    public bool IsTargetInChargeRange { get; private set; }
    public bool IsTargetInNormalAttackRange { get; private set; }
    public bool IsTargetInStompRange { get; private set; }
    public bool IsTargetInTwistAttackRange { get; private set; }
    public bool IsTargetBehind { get; private set; }
    public bool HasSuspiciousStimulus => Time.time - lastHeardSoundTime <= suspiciousSoundMemoryDuration;
    public Vector3 LastSuspiciousPosition => lastHeardSoundPosition;

    private MammothState state;
    private MammothPersonality personality;
    private float nextTargetRefreshTime;
    private Vector3 lastTargetSamplePosition;
    private float lastTargetSampleTime = -1f;
    private Vector3 lastHeardSoundPosition;
    private float lastHeardSoundTime = -999f;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        personality = GetComponent<MammothPersonality>();
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

        if (newTarget == null)
        {
            lastTargetSampleTime = -1f;
            TargetMovementSpeed = 0f;
            return;
        }

        lastTargetSamplePosition = newTarget.position;
        lastTargetSampleTime = Time.time;
    }

    public void ResetAwareness()
    {
        target = null;
        nextTargetRefreshTime = 0f;
        DistanceToTarget = float.MaxValue;
        DirectionToTarget = Vector3.zero;
        TargetMovementSpeed = 0f;
        CanSeeTarget = false;
        CanHearTarget = false;
        IsTargetDetected = false;
        IsTargetInChaseRange = false;
        IsTargetInChargeRange = false;
        IsTargetInNormalAttackRange = false;
        IsTargetInStompRange = false;
        IsTargetInTwistAttackRange = false;
        IsTargetBehind = false;
        lastTargetSampleTime = -1f;
        lastHeardSoundTime = -999f;
        lastHeardSoundPosition = Vector3.zero;
    }

    public void ReportSuspiciousSound(Vector3 worldPosition, float loudness = 1f)
    {
        RememberSuspiciousPosition(worldPosition, Mathf.Lerp(0.04f, 0.22f, Mathf.Clamp01(loudness)));
    }

    private void FindTarget()
    {
        Transform runtimePlayer = MultiplayerPrototype.GetClosestPlayerTransform(transform.position);
        if (IsTargetValid(runtimePlayer))
        {
            SetTarget(runtimePlayer);
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null && playerObject.activeInHierarchy)
        {
            SetTarget(playerObject.transform);
            return;
        }

        PlayerHealth playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (playerHealth != null)
        {
            SetTarget(playerHealth.transform);
        }
    }

    private void UpdateSenses()
    {
        if (!IsTargetValid(target))
        {
            target = null;
            DistanceToTarget = float.MaxValue;
            DirectionToTarget = Vector3.zero;
            CanSeeTarget = false;
            IsTargetDetected = false;
            IsTargetInChaseRange = false;
            IsTargetInChargeRange = false;
            IsTargetInNormalAttackRange = false;
            IsTargetInStompRange = false;
            IsTargetInTwistAttackRange = false;
            IsTargetBehind = false;
            CanHearTarget = false;
            TargetMovementSpeed = 0f;
            lastTargetSampleTime = -1f;
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        DistanceToTarget = toTarget.magnitude;
        DirectionToTarget = DistanceToTarget > 0.001f ? toTarget.normalized : transform.forward;
        TargetMovementSpeed = CalculateTargetMovementSpeed(target.position);

        float angle = Vector3.Angle(transform.forward, DirectionToTarget);

        IsTargetBehind = angle > 110f;
        CanSeeTarget = DistanceToTarget <= detectionRange && angle <= fieldOfView * 0.5f && HasLineOfSight();
        CanHearTarget =
            DistanceToTarget <= proximityHearingRange ||
            (DistanceToTarget <= hearingRange && TargetMovementSpeed >= movementNoiseThreshold);
        IsTargetDetected = CanSeeTarget || CanHearTarget || DistanceToTarget <= normalAttackRange;

        IsTargetInChaseRange = DistanceToTarget <= chaseRange;
        IsTargetInChargeRange = DistanceToTarget <= chargeRange && DistanceToTarget > normalAttackRange;
        IsTargetInNormalAttackRange = DistanceToTarget <= normalAttackRange;
        IsTargetInStompRange = DistanceToTarget <= stompRange;
        IsTargetInTwistAttackRange = DistanceToTarget <= twistAttackRange;

        if (CanSeeTarget)
        {
            personality?.AddAlertness(0.01f);
        }
        else if (CanHearTarget)
        {
            RememberSuspiciousPosition(target.position, 0.015f);
        }
    }

    private bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 destination = target.position + Vector3.up * 0.8f;
        Vector3 direction = destination - origin;

        if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude, lineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        return true;
    }

    private float CalculateTargetMovementSpeed(Vector3 worldPosition)
    {
        if (lastTargetSampleTime < 0f)
        {
            lastTargetSamplePosition = worldPosition;
            lastTargetSampleTime = Time.time;
            return 0f;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.time - lastTargetSampleTime);
        Vector3 flatPrevious = lastTargetSamplePosition;
        flatPrevious.y = 0f;
        Vector3 flatCurrent = worldPosition;
        flatCurrent.y = 0f;

        float speed = Vector3.Distance(flatPrevious, flatCurrent) / deltaTime;
        lastTargetSamplePosition = worldPosition;
        lastTargetSampleTime = Time.time;
        return speed;
    }

    private void RememberSuspiciousPosition(Vector3 worldPosition, float alertnessGain)
    {
        bool shouldRefresh =
            Time.time - lastHeardSoundTime >= suspiciousSoundRefreshInterval ||
            Vector3.Distance(lastHeardSoundPosition, worldPosition) >= 1f;

        if (!shouldRefresh)
        {
            return;
        }

        lastHeardSoundPosition = worldPosition;
        lastHeardSoundTime = Time.time;
        state?.RememberThreatSound(worldPosition);
        personality?.AddAlertness(alertnessGain);
    }

    private static bool IsTargetValid(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }
}
