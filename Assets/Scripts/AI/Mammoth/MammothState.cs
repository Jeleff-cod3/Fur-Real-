using UnityEngine;

public class MammothState : MonoBehaviour
{
    [Header("Action State")]
    public MammothActionType currentAction = MammothActionType.Idle;
    public MammothActionType previousAction = MammothActionType.Idle;

    [Header("Busy Flags")]
    public bool isBusy;
    public bool isAttacking;
    public bool isCharging;
    public bool isRecovering;

    [Header("Memory")]
    public Transform currentTarget;
    public Vector3 lastKnownTargetPosition;
    public Vector3 lastHeardThreatPosition;
    public Vector3 lastDamageSourcePosition;
    public Vector3 lastDamageDirection;
    public float lastActionChangeTime;
    public float lastDamageTime;
    public float lastTargetSeenTime;
    public float lastTargetLostTime;
    public float lastHeardThreatTime;
    public float lastThreatenTime;
    public int repeatedThreatHitCount;
    public bool hasDamageSource;

    public void SetAction(MammothActionType newAction)
    {
        if (currentAction == newAction)
        {
            return;
        }

        previousAction = currentAction;
        currentAction = newAction;
        lastActionChangeTime = Time.time;

        Debug.Log($"Mammoth action changed: {previousAction} -> {currentAction}");
    }

    public void SetTarget(Transform target)
    {
        currentTarget = target;

        if (target != null)
        {
            lastKnownTargetPosition = target.position;
        }
    }

    public void RememberTargetSighting(Transform target)
    {
        if (target == null)
        {
            return;
        }

        currentTarget = target;
        lastKnownTargetPosition = target.position;
        lastTargetSeenTime = Time.time;
    }

    public void MarkTargetLost()
    {
        if (currentTarget == null)
        {
            return;
        }

        lastTargetLostTime = Time.time;
    }

    public void MarkDamaged(Vector3? sourcePosition = null)
    {
        float previousDamageTime = lastDamageTime;
        Vector3 previousDamageDirection = lastDamageDirection;

        lastDamageTime = Time.time;

        if (!sourcePosition.HasValue)
        {
            repeatedThreatHitCount = Mathf.Max(1, repeatedThreatHitCount);
            return;
        }

        Vector3 damageDirection = sourcePosition.Value - transform.position;
        damageDirection.y = 0f;

        if (damageDirection.sqrMagnitude <= 0.001f)
        {
            damageDirection = -transform.forward;
        }

        damageDirection.Normalize();

        bool repeatedDirection =
            previousDamageTime > 0f &&
            Time.time - previousDamageTime <= 8f &&
            previousDamageDirection.sqrMagnitude > 0.001f &&
            Vector3.Dot(previousDamageDirection.normalized, damageDirection) >= 0.7f;

        repeatedThreatHitCount = repeatedDirection ? repeatedThreatHitCount + 1 : 1;
        lastDamageSourcePosition = sourcePosition.Value;
        lastDamageDirection = damageDirection;
        lastKnownTargetPosition = sourcePosition.Value;
        hasDamageSource = true;
    }

    public bool WasDamagedRecently(float recentTime)
    {
        return Time.time - lastDamageTime <= recentTime;
    }

    public bool HasRecentTargetMemory(float memoryDuration)
    {
        return lastTargetSeenTime > 0f && Time.time - lastTargetSeenTime <= memoryDuration;
    }

    public void RememberThreatSound(Vector3 worldPosition)
    {
        lastHeardThreatPosition = worldPosition;
        lastHeardThreatTime = Time.time;
        lastKnownTargetPosition = worldPosition;
    }

    public bool HasRecentHeardThreat(float memoryDuration)
    {
        return lastHeardThreatTime > 0f && Time.time - lastHeardThreatTime <= memoryDuration;
    }

    public bool HasRecentDamageSource(float memoryDuration)
    {
        return hasDamageSource && WasDamagedRecently(memoryDuration);
    }

    public bool HasRecentThreatMemory(float memoryDuration)
    {
        return HasRecentTargetMemory(memoryDuration)
            || HasRecentHeardThreat(memoryDuration)
            || HasRecentDamageSource(memoryDuration);
    }

    public bool WasThreatenedFromSameDirectionRecently(float memoryDuration, int minimumHits)
    {
        return repeatedThreatHitCount >= minimumHits && HasRecentDamageSource(memoryDuration);
    }

    public Vector3 GetBestInvestigationPosition()
    {
        float seenAge = lastTargetSeenTime > 0f ? Time.time - lastTargetSeenTime : Mathf.Infinity;
        float heardAge = lastHeardThreatTime > 0f ? Time.time - lastHeardThreatTime : Mathf.Infinity;
        float damageAge = hasDamageSource ? Time.time - lastDamageTime : Mathf.Infinity;

        if (damageAge <= heardAge && damageAge <= seenAge && hasDamageSource)
        {
            return lastDamageSourcePosition;
        }

        if (heardAge <= seenAge && lastHeardThreatTime > 0f)
        {
            return lastHeardThreatPosition;
        }

        if (lastTargetSeenTime > 0f)
        {
            return lastKnownTargetPosition;
        }

        return lastKnownTargetPosition != Vector3.zero ? lastKnownTargetPosition : transform.position;
    }

    public void RecordThreatDisplay()
    {
        lastThreatenTime = Time.time;
    }

    public bool HasThreatenedRecently(float cooldown)
    {
        return lastThreatenTime > 0f && Time.time - lastThreatenTime <= cooldown;
    }

    public float TimeSinceLastTargetSeen()
    {
        if (lastTargetSeenTime <= 0f)
        {
            return Mathf.Infinity;
        }

        return Time.time - lastTargetSeenTime;
    }

    public bool CanStartNewAction()
    {
        return !isBusy && !isAttacking && !isCharging && !isRecovering;
    }
}
