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
    public float lastActionChangeTime;
    public float lastDamageTime;
    public float lastTargetSeenTime;
    public float lastTargetLostTime;

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

    public void MarkDamaged()
    {
        lastDamageTime = Time.time;
    }

    public bool WasDamagedRecently(float recentTime)
    {
        return Time.time - lastDamageTime <= recentTime;
    }

    public bool HasRecentTargetMemory(float memoryDuration)
    {
        return lastTargetSeenTime > 0f && Time.time - lastTargetSeenTime <= memoryDuration;
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
