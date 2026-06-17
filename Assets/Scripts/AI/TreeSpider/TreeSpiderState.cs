using UnityEngine;

public class TreeSpiderState : MonoBehaviour
{
    [Header("Action State")]
    public TreeSpiderActionType currentAction = TreeSpiderActionType.Idle;
    public TreeSpiderActionType previousAction = TreeSpiderActionType.Idle;

    [Header("Flags")]
    public bool isHidden;
    public bool isBusy;
    public bool isReturningToTree;

    [Header("Target Memory")]
    public Transform currentTarget;
    public Vector3 lastKnownTargetPosition;
    public float lastTargetSeenTime;
    public float lastTargetLostTime;

    [Header("Tree")]
    public int currentTreeIndex = -1;
    public ResourceForestTreeAnchor currentTreeAnchor;

    public void SetAction(TreeSpiderActionType newAction)
    {
        if (currentAction == newAction)
        {
            return;
        }

        previousAction = currentAction;
        currentAction = newAction;
    }

    public void SetTarget(Transform target)
    {
        currentTarget = target;

        if (target != null)
        {
            lastKnownTargetPosition = target.position;
        }
    }

    public void RememberTarget(Transform target)
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
        lastTargetLostTime = Time.time;
    }

    public bool HasRecentTargetMemory(float duration)
    {
        return lastTargetSeenTime > 0f && Time.time - lastTargetSeenTime <= duration;
    }

    public void AssignTree(int treeIndex, ResourceForestTreeAnchor anchor)
    {
        currentTreeIndex = treeIndex;
        currentTreeAnchor = anchor;
        isReturningToTree = false;
    }

    public void ClearTree()
    {
        currentTreeIndex = -1;
        currentTreeAnchor = default;
        isReturningToTree = false;
    }

    public bool CanStartNewAction()
    {
        return !isBusy;
    }
}
