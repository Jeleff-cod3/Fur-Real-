using UnityEngine;

public class MammothBrain : MonoBehaviour
{
    [Header("Decision Settings")]
    [SerializeField] private float decisionInterval = 0.35f;
    [SerializeField] private float recentDamageMemoryTime = 3f;
    [SerializeField] private float targetMemoryDuration = 4.5f;

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
        bool damagedRecently = state != null && state.WasDamagedRecently(recentDamageMemoryTime);
        bool canSeeTarget = senses.HasTarget && senses.CanSeeTarget;
        bool hasRecentTargetMemory = state != null && state.HasRecentTargetMemory(targetMemoryDuration);

        if (damagedRecently)
        {
            personality?.AddAnger(0.08f);
            personality?.AddFear(0.04f);
        }

        if (!canSeeTarget)
        {
            if (hasRecentTargetMemory)
            {
                if (lowHealth && damagedRecently && flightDrive > fightDrive + 0.15f)
                {
                    return MammothActionType.RunAway;
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

        if (lowHealth && flightDrive > fightDrive)
        {
            return MammothActionType.RunAway;
        }

        if (combat != null && senses.IsTargetBehind && senses.IsTargetInTwistAttackRange && combat.CanTwistAttack)
        {
            return MammothActionType.TwistAttack;
        }

        if (combat != null && senses.IsTargetInStompRange && combat.CanStomp)
        {
            return MammothActionType.Stomp;
        }

        if (combat != null && senses.IsTargetInNormalAttackRange && combat.CanNormalAttack)
        {
            return MammothActionType.NormalAttack;
        }

        if (combat != null && senses.IsTargetInChargeRange && combat.CanCharge && fightDrive > 0.45f)
        {
            return MammothActionType.Charge;
        }

        if (senses.IsTargetInChaseRange && fightDrive >= flightDrive)
        {
            return MammothActionType.ChasePlayer;
        }

        if (flightDrive > fightDrive + 0.2f)
        {
            return MammothActionType.RunAway;
        }

        return MammothActionType.ChasePlayer;
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
