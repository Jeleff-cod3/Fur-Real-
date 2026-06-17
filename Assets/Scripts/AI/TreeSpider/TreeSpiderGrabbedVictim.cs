using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerHealth))]
public class TreeSpiderGrabbedVictim : MonoBehaviour
{
    [SerializeField] private float pullStrength = 9f;

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
        transform.SetParent(holdPoint, true);
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

        if (body != null)
        {
            body.useGravity = originalUseGravity;
        }

        Released?.Invoke(this);
    }
}
