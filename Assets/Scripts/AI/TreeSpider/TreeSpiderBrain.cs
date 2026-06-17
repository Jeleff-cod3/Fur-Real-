using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(TreeSpiderState))]
[RequireComponent(typeof(TreeSpiderSenses))]
[RequireComponent(typeof(TreeSpiderMovement))]
[RequireComponent(typeof(TreeSpiderCombat))]
[RequireComponent(typeof(TreeSpiderHealth))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class TreeSpiderBrain : MonoBehaviour
{
    [Header("Decision")]
    [SerializeField] private float decisionInterval = 0.18f;
    [SerializeField] private float targetMemoryDuration = 5f;

    [Header("Hidden Tree Behaviour")]
    [SerializeField] private float hiddenDecisionDelayMin = 0.7f;
    [SerializeField] private float hiddenDecisionDelayMax = 1.5f;
    [SerializeField] private float hiddenDropBaseChance = 0.2f;
    [SerializeField] private float hiddenCloserBonus = 0.38f;
    [SerializeField] private float hiddenFartherPenalty = 0.18f;

    [Header("Visible Behaviour")]
    [SerializeField] private float chooseGrabChance = 0.32f;
    [SerializeField] private float wanderDurationAfterLosingTarget = 4.5f;
    [SerializeField] private float returnTreeDistance = 2.2f;

    private TreeSpiderState state;
    private TreeSpiderSenses senses;
    private TreeSpiderMovement movement;
    private TreeSpiderCombat combat;
    private TreeSpiderHealth health;
    private ResourceForestTreeAnchorRegistry treeRegistry;
    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;
    private Rigidbody body;
    private float nextDecisionTime;
    private float nextHiddenDecisionTime;
    private float wanderUntilTime;

    private void Awake()
    {
        state = GetComponent<TreeSpiderState>();
        senses = GetComponent<TreeSpiderSenses>();
        movement = GetComponent<TreeSpiderMovement>();
        combat = GetComponent<TreeSpiderCombat>();
        health = GetComponent<TreeSpiderHealth>();
        body = GetComponent<Rigidbody>();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);

        EnsureEnemyLayer();
        EnsureCollider();
        ConfigureRigidbody();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += HandleDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
        }
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            return;
        }

        if (treeRegistry == null)
        {
            treeRegistry = FindAnyObjectByType<ResourceForestTreeAnchorRegistry>();
        }

        if (Time.time < nextDecisionTime)
        {
            return;
        }

        nextDecisionTime = Time.time + decisionInterval;

        if (state == null || !state.CanStartNewAction())
        {
            return;
        }

        RunDecisionLoop();
    }

    public void InitializeInTree(ResourceForestTreeAnchorRegistry registry, int treeIndex, ResourceForestTreeAnchor anchor)
    {
        treeRegistry = registry;
        state.AssignTree(treeIndex, anchor);
        HideInAssignedTree();
    }

    public bool CanBeDespawnedSilently()
    {
        return state != null && state.isHidden && !state.isBusy;
    }

    public void ForceDespawn()
    {
        if (treeRegistry != null)
        {
            treeRegistry.ReleaseAnchor(state.currentTreeIndex, this);
        }

        Destroy(gameObject);
    }

    private void RunDecisionLoop()
    {
        Transform target = senses != null ? senses.Target : null;

        if (target != null)
        {
            state.SetTarget(target);
        }

        if (senses != null && senses.CanSeeTarget && target != null)
        {
            state.RememberTarget(target);
        }
        else if (state.currentTarget != null && state.lastTargetLostTime < state.lastTargetSeenTime)
        {
            state.MarkTargetLost();
            wanderUntilTime = Time.time + wanderDurationAfterLosingTarget;
        }

        if (state.isHidden)
        {
            DecideWhileHidden(target);
            return;
        }

        if (state.isReturningToTree)
        {
            ContinueReturningToTree();
            return;
        }

        if (target != null && (senses.CanSeeTarget || senses.IsTargetWithinChaseRange))
        {
            DecideWhileVisible(target);
            return;
        }

        if (state.HasRecentTargetMemory(targetMemoryDuration))
        {
            WanderAfterLosingTarget();
            return;
        }

        TryReturnToNearestTree();
    }

    private void DecideWhileHidden(Transform target)
    {
        state.SetAction(TreeSpiderActionType.Hidden);

        if (target == null || senses == null || !senses.IsTargetNearHiddenTree)
        {
            nextHiddenDecisionTime = 0f;
            return;
        }

        if (senses.IsTargetDirectlyUnderTree)
        {
            DropFromTree(target, true);
            return;
        }

        if (Time.time < nextHiddenDecisionTime)
        {
            return;
        }

        float chance = hiddenDropBaseChance;
        chance += senses.IsTargetClosingOnTree ? hiddenCloserBonus : -hiddenFartherPenalty;
        chance = Mathf.Clamp01(chance);

        if (Random.value < chance)
        {
            DropFromTree(target, false);
            return;
        }

        state.SetAction(TreeSpiderActionType.Watching);
        nextHiddenDecisionTime = Time.time + Random.Range(hiddenDecisionDelayMin, hiddenDecisionDelayMax);
    }

    private void DecideWhileVisible(Transform target)
    {
        if (senses.IsTargetInBiteRange && combat.CanBite)
        {
            bool shouldGrab = senses.IsTargetInGrabRange && combat.CanGrab && Random.value < chooseGrabChance;
            if (shouldGrab)
            {
                combat.StartGrab(target);
            }
            else
            {
                combat.StartBite(target);
            }

            return;
        }

        if (senses.IsTargetInGrabRange && combat.CanGrab && Random.value < chooseGrabChance * 0.65f)
        {
            combat.StartGrab(target);
            return;
        }

        movement.Chase(target);
        state.SetAction(TreeSpiderActionType.Chase);
    }

    private void WanderAfterLosingTarget()
    {
        if (Time.time < wanderUntilTime)
        {
            if (state.currentAction != TreeSpiderActionType.Wander || movement.HasReachedDestination)
            {
                movement.WanderAround(state.lastKnownTargetPosition);
                state.SetAction(TreeSpiderActionType.Wander);
            }

            return;
        }

        TryReturnToNearestTree();
    }

    private void TryReturnToNearestTree()
    {
        if (treeRegistry == null || !treeRegistry.IsReady)
        {
            movement.WanderAround(transform.position);
            state.SetAction(TreeSpiderActionType.Wander);
            return;
        }

        if (!treeRegistry.TryReserveNearestAvailableAnchor(transform.position, this, out int treeIndex, out ResourceForestTreeAnchor anchor))
        {
            movement.WanderAround(transform.position);
            state.SetAction(TreeSpiderActionType.Wander);
            return;
        }

        state.AssignTree(treeIndex, anchor);
        state.isReturningToTree = true;
        movement.ReturnToTree(anchor.trunkBasePosition);
        state.SetAction(TreeSpiderActionType.ReturnToTree);
    }

    private void ContinueReturningToTree()
    {
        if (state.currentTreeIndex < 0)
        {
            state.isReturningToTree = false;
            return;
        }

        if (!movement.HasReachedDestination &&
            (transform.position - state.currentTreeAnchor.trunkBasePosition).sqrMagnitude > returnTreeDistance * returnTreeDistance)
        {
            if (state.currentAction != TreeSpiderActionType.ReturnToTree)
            {
                movement.ReturnToTree(state.currentTreeAnchor.trunkBasePosition);
                state.SetAction(TreeSpiderActionType.ReturnToTree);
            }

            return;
        }

        HideInAssignedTree();
    }

    private void DropFromTree(Transform target, bool guaranteedHit)
    {
        if (treeRegistry != null)
        {
            treeRegistry.ReleaseAnchor(state.currentTreeIndex, this);
        }

        Vector3 dropOrigin = state.currentTreeAnchor.trunkBasePosition;
        Vector3 desiredDropPosition = guaranteedHit && target != null
            ? target.position
            : dropOrigin;

        if (!movement.TryGetGroundedPositionNear(desiredDropPosition, 6f, out Vector3 groundedDropPosition))
        {
            groundedDropPosition = desiredDropPosition;
        }

        RevealSpider(groundedDropPosition);
        state.ClearTree();
        state.SetTarget(target);
        state.SetAction(TreeSpiderActionType.DropAmbush);
        nextHiddenDecisionTime = 0f;
        combat.StartDropAmbush(target, guaranteedHit);
    }

    private void HideInAssignedTree()
    {
        if (state.currentTreeIndex < 0)
        {
            return;
        }

        movement.Stop();
        movement.SetAgentEnabled(false);
        transform.position = state.currentTreeAnchor.hidePosition;
        state.isHidden = true;
        state.isReturningToTree = false;
        state.isBusy = false;
        state.SetAction(TreeSpiderActionType.Hidden);
        SetVisible(false);
    }

    private void RevealSpider(Vector3 groundedPosition)
    {
        state.isHidden = false;
        state.isReturningToTree = false;
        SetVisible(true);
        movement.SetAgentEnabled(true);
        movement.WarpTo(groundedPosition);
    }

    private void SetVisible(bool isVisible)
    {
        foreach (Renderer renderer in cachedRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = isVisible;
            }
        }

        foreach (Collider currentCollider in cachedColliders)
        {
            if (currentCollider == null)
            {
                continue;
            }

            if (currentCollider.transform == transform || currentCollider.transform.IsChildOf(transform))
            {
                currentCollider.enabled = isVisible;
            }
        }
    }

    private void HandleDied(TreeSpiderHealth deadSpider)
    {
        if (treeRegistry != null)
        {
            treeRegistry.ReleaseAnchor(state.currentTreeIndex, this);
        }
    }

    private void EnsureEnemyLayer()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
        {
            gameObject.layer = enemyLayer;
        }
    }

    private void EnsureCollider()
    {
        if (GetComponent<Collider>() != null)
        {
            return;
        }

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.35f, 0f);
        capsule.height = 0.8f;
        capsule.radius = 0.45f;
    }

    private void ConfigureRigidbody()
    {
        if (body == null)
        {
            return;
        }

        body.useGravity = false;
        body.isKinematic = true;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }
}
