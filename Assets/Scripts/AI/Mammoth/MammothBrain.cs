using UnityEngine;

public class MammothBrain : MonoBehaviour
{
    [Header("Decision Settings")]
    [SerializeField] private float decisionInterval = 0.35f;
    [SerializeField] private float recentDamageMemoryTime = 3f;
    [SerializeField] private float targetMemoryDuration = 4.5f;
    [SerializeField] private float threatMemoryDuration = 6f;
    [SerializeField] private float warningDistance = 18f;
    [SerializeField] private float warningCooldown = 3.2f;
    [SerializeField] private float sightLossCombatGraceTime = 0.85f;
    [SerializeField] private float desperateRetreatHealthThreshold = 0.18f;

    private MammothState state;
    private MammothPersonality personality;
    private MammothSenses senses;
    private MammothCombat combat;
    private MammothActionController actionController;
    private MammothMovement movement;
    private EnemyHealth health;

    private float nextDecisionTime;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        personality = GetComponent<MammothPersonality>();
        senses = GetComponent<MammothSenses>();
        combat = GetComponent<MammothCombat>();
        actionController = GetComponent<MammothActionController>();
        movement = GetComponent<MammothMovement>();
        health = GetComponent<EnemyHealth>();
    }

    private void Update()
    {
        if (Time.time < nextDecisionTime)
        {
            return;
        }

        nextDecisionTime = Time.time + decisionInterval;

        if (state != null && !state.CanStartNewAction())
        {
            return;
        }

        MammothActionType chosenAction = ChooseAction();
        actionController.Execute(chosenAction);
    }

    private MammothActionType ChooseAction()
    {
        if (senses == null)
        {
            return MammothActionType.Idle;
        }

        if (state != null)
        {
            if (senses.Target != null)
            {
                state.SetTarget(senses.Target);
            }

            if (senses.CanSeeTarget && senses.Target != null)
            {
                state.RememberTargetSighting(senses.Target);
            }
            else if (state.currentTarget != null)
            {
                state.MarkTargetLost();
            }
        }

        float healthPercent = GetHealthPercent();
        float fightDrive = personality != null ? personality.GetFightDrive() : 0.5f;
        float flightDrive = personality != null ? personality.GetFlightDrive() : 0.5f;

        bool lowHealth = personality != null && healthPercent <= personality.panicHealthThreshold;
        bool criticalHealth = healthPercent <= desperateRetreatHealthThreshold;
        bool enraged = personality != null && healthPercent <= personality.enragedHealthThreshold;
        bool damagedRecently = state != null && state.WasDamagedRecently(recentDamageMemoryTime);
        bool canSeeTarget = senses.HasTarget && senses.CanSeeTarget;
        bool canHearTarget = senses.HasTarget && senses.CanHearTarget;
        bool hasRecentTargetMemory = state != null && state.HasRecentTargetMemory(targetMemoryDuration);
        bool hasRecentThreatMemory = state != null && state.HasRecentThreatMemory(threatMemoryDuration);
        bool repeatedDirectionalHits = state != null &&
            state.WasThreatenedFromSameDirectionRecently(threatMemoryDuration, 2);
        bool exhausted = personality != null && personality.IsExhausted;
        bool aggressionTriggered = personality != null && personality.IsAggressionTriggered;
        bool fearTriggered = personality != null && personality.IsFearTriggered;
        bool hasEmbeddedSpears = state != null && state.HasEmbeddedSpears;
        float timeSinceLastSeen = state != null ? state.TimeSinceLastTargetSeen() : Mathf.Infinity;
        bool hasVeryRecentVisualContact = timeSinceLastSeen <= sightLossCombatGraceTime;
        bool hasCloseCombatTarget = senses.HasTarget &&
            (senses.IsTargetInTwistAttackRange ||
            senses.IsTargetInNormalAttackRange ||
            senses.IsTargetInStompRange);
        bool canPathToTarget = senses.HasTarget && movement != null && movement.CanReachTarget(senses.Target);
        bool hasCombatFocus =
            canSeeTarget ||
            canHearTarget ||
            hasRecentTargetMemory ||
            damagedRecently ||
            hasEmbeddedSpears;
        bool shouldHoldCombatFocus =
            senses.HasTarget &&
            canPathToTarget &&
            senses.IsTargetInChaseRange &&
            (canHearTarget || hasVeryRecentVisualContact || damagedRecently || hasCloseCombatTarget || hasEmbeddedSpears);
        bool hasSafeChargePath =
            senses.HasTarget &&
            movement != null &&
            movement.HasSafeChargePath(senses.Target) &&
            (canSeeTarget || hasVeryRecentVisualContact);
        bool shouldWarnBeforeEscalating =
            canSeeTarget &&
            combat != null &&
            combat.CanThreaten &&
            state != null &&
            !state.HasThreatenedRecently(warningCooldown) &&
            senses.DistanceToTarget <= warningDistance &&
            !senses.IsTargetInNormalAttackRange &&
            !aggressionTriggered &&
            !damagedRecently &&
            !hasEmbeddedSpears;

        if (damagedRecently)
        {
            personality?.AddAnger(0.08f);
            personality?.AddFear(0.04f);
            personality?.AddAlertness(0.08f);
        }

        if (!canSeeTarget && !shouldHoldCombatFocus)
        {
            if (hasRecentThreatMemory || canHearTarget || senses.HasSuspiciousStimulus)
            {
                if (lowHealth && damagedRecently && flightDrive > fightDrive + 0.15f)
                {
                    return MammothActionType.RunAway;
                }

                if (repeatedDirectionalHits && !exhausted && fightDrive > flightDrive + 0.08f)
                {
                    personality?.AddAnger(0.04f);
                }

                return MammothActionType.Investigate;
            }

            if (state != null &&
                state.currentAction == MammothActionType.Roam &&
                movement != null &&
                !movement.HasReachedDestination)
            {
                return MammothActionType.Roam;
            }

            float curiosity = personality != null ? personality.curiosity : 0.4f;
            return Random.value < curiosity ? MammothActionType.Roam : MammothActionType.Idle;
        }

        if (criticalHealth && (flightDrive > fightDrive || (personality != null && personality.pain + personality.fatigue > 1f)))
        {
            return MammothActionType.RunAway;
        }

        if (criticalHealth && fearTriggered && !hasCloseCombatTarget)
        {
            return MammothActionType.RunAway;
        }

        if (!canPathToTarget)
        {
            if (criticalHealth && fearTriggered)
            {
                return MammothActionType.RunAway;
            }

            return shouldWarnBeforeEscalating ? MammothActionType.Threaten : MammothActionType.Investigate;
        }

        if (combat != null &&
            hasCombatFocus &&
            senses.IsTargetBehind &&
            senses.IsTargetInTwistAttackRange &&
            combat.CanTwistAttack)
        {
            return MammothActionType.TwistAttack;
        }

        if (combat != null && hasCombatFocus && senses.IsTargetInStompRange && combat.CanStomp)
        {
            return MammothActionType.Stomp;
        }

        if (combat != null && hasCombatFocus && senses.IsTargetInNormalAttackRange && combat.CanNormalAttack)
        {
            return MammothActionType.NormalAttack;
        }

        if (aggressionTriggered &&
            combat != null &&
            senses.IsTargetInChargeRange &&
            combat.CanCharge &&
            hasSafeChargePath &&
            !exhausted)
        {
            return MammothActionType.Charge;
        }

        if (combat != null &&
            senses.IsTargetInChargeRange &&
            combat.CanCharge &&
            hasSafeChargePath &&
            !exhausted &&
            fightDrive > 0.45f)
        {
            if (shouldWarnBeforeEscalating && !enraged && !damagedRecently)
            {
                return MammothActionType.Threaten;
            }

            return MammothActionType.Charge;
        }

        if ((senses.IsTargetInChaseRange || shouldHoldCombatFocus) && fightDrive >= flightDrive)
        {
            if (shouldWarnBeforeEscalating && (repeatedDirectionalHits || hasRecentTargetMemory || damagedRecently))
            {
                return MammothActionType.Threaten;
            }

            return MammothActionType.ChasePlayer;
        }

        if (criticalHealth && fearTriggered)
        {
            return MammothActionType.RunAway;
        }

        if (criticalHealth && flightDrive > fightDrive + 0.2f)
        {
            return MammothActionType.RunAway;
        }

        return shouldWarnBeforeEscalating ? MammothActionType.Threaten : MammothActionType.ChasePlayer;
    }

    private float GetHealthPercent()
    {
        if (health == null)
        {
            return 1f;
        }

        return health.HealthPercent;
    }
}
