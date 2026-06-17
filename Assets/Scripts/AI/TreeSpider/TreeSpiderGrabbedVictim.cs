using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerHealth))]
public class TreeSpiderGrabbedVictim : MonoBehaviour
{
    [SerializeField] private float pullStrength = 9f;
    [SerializeField] [Range(0f, 1f)] private float scaleCompensationStrength = 0.82f;
    [SerializeField] private Vector3 heldLocalOffset = new Vector3(0f, -0.1f, 0.22f);

    private PlayerHealth playerHealth;
    private Rigidbody body;
    private Transform originalParent;
    private Transform holdPoint;
    private float escapeThreshold;
    private float releaseAtTime;
    private int failureDamage;
    private float struggleProgress;
    private bool originalUseGravity;
    private bool isHolding;
    private Vector3 lastLocalPosition;
    private Vector3 originalLocalScale;
    private Vector3 originalLossyScale;

    public event Action<TreeSpiderGrabbedVictim> Released;

    public bool IsHolding => isHolding;
    public bool DidEscape { get; private set; }

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        body = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!isHolding || holdPoint == null)
        {
            return;
        }

        transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, Time.deltaTime * pullStrength);

        float drift = (transform.localPosition - lastLocalPosition).magnitude;
        struggleProgress += drift;
        lastLocalPosition = transform.localPosition;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.wasPressedThisFrame) struggleProgress += 0.35f;
            if (Keyboard.current.aKey.wasPressedThisFrame) struggleProgress += 0.35f;
            if (Keyboard.current.sKey.wasPressedThisFrame) struggleProgress += 0.35f;
            if (Keyboard.current.dKey.wasPressedThisFrame) struggleProgress += 0.35f;
        }

        if (struggleProgress >= escapeThreshold)
        {
            Release(true, false);
            return;
        }

        if (Time.time >= releaseAtTime)
        {
            Release(false, true);
        }
    }

    private void FixedUpdate()
    {
        if (!isHolding || body == null)
        {
            return;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    public void BeginGrab(Transform targetHoldPoint, float holdDuration, float requiredStruggle, int onFailureDamage)
    {
        if (targetHoldPoint == null || playerHealth == null || playerHealth.IsDead)
        {
            return;
        }

        if (isHolding)
        {
            Release(false, false);
        }

        holdPoint = targetHoldPoint;
        originalParent = transform.parent;
        escapeThreshold = Mathf.Max(0.5f, requiredStruggle);
        releaseAtTime = Time.time + Mathf.Max(0.2f, holdDuration);
        failureDamage = Mathf.Max(0, onFailureDamage);
        struggleProgress = 0f;
        DidEscape = false;
        isHolding = true;
        originalLocalScale = transform.localScale;
        originalLossyScale = transform.lossyScale;
        transform.SetParent(holdPoint, true);
        ApplyGrabVisualPose();
        lastLocalPosition = transform.localPosition;

        if (body != null)
        {
            originalUseGravity = body.useGravity;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    public void ForceRelease()
    {
        Release(false, false);
    }

    private void Release(bool escaped, bool applyFailureDamage)
    {
        if (!isHolding)
        {
            return;
        }

        if (applyFailureDamage && playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.TakeDamage(failureDamage);
        }

        DidEscape = escaped;
        isHolding = false;
        holdPoint = null;
        transform.SetParent(originalParent, true);
        transform.localScale = originalLocalScale;

        if (body != null)
        {
            body.useGravity = originalUseGravity;
        }

        Released?.Invoke(this);
    }

    private void ApplyGrabVisualPose()
    {
        if (holdPoint == null)
        {
            return;
        }

        transform.localPosition = heldLocalOffset;

        Vector3 parentLossyScale = holdPoint.lossyScale;
        Vector3 fullyCompensatedScale = new Vector3(
            SafeDivide(originalLossyScale.x, parentLossyScale.x),
            SafeDivide(originalLossyScale.y, parentLossyScale.y),
            SafeDivide(originalLossyScale.z, parentLossyScale.z)
        );

        transform.localScale = Vector3.Lerp(
            transform.localScale,
            fullyCompensatedScale,
            scaleCompensationStrength
        );
    }

    private static float SafeDivide(float numerator, float denominator)
    {
        return Mathf.Abs(denominator) > 0.0001f ? numerator / denominator : numerator;
    }
}
