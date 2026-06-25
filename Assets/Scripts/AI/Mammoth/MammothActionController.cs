using UnityEngine;

public class MammothActionController : MonoBehaviour
{
    private MammothState state;
    private MammothSenses senses;
    private MammothMovement movement;
    private MammothCombat combat;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        senses = GetComponent<MammothSenses>();
        movement = GetComponent<MammothMovement>();
        combat = GetComponent<MammothCombat>();
    }

    public void Execute(MammothActionType action)
    {
        if (movement == null)
        {
            return;
        }

        Transform target = senses != null && senses.Target != null
            ? senses.Target
            : state != null
                ? state.currentTarget
                : null;

        if (state != null && !state.CanStartNewAction())
        {
            return;
        }

        switch (action)
        {
            case MammothActionType.Idle:
                movement.Stop();
                state?.SetAction(MammothActionType.Idle);
                break;

            case MammothActionType.Roam:
                if (state != null && state.currentAction == MammothActionType.Roam && !movement.HasReachedDestination)
                {
                    return;
                }

                if (movement.Roam())
                {
                    state?.SetAction(MammothActionType.Roam);
                }
                break;

            case MammothActionType.Investigate:
            {
                if (state != null && state.currentAction == MammothActionType.Investigate)
                {
                    return;
                }

                Vector3 investigationPosition = state != null
                    ? state.GetBestInvestigationPosition()
                    : transform.position;

                if (movement.Investigate(investigationPosition))
                {
                    state?.SetAction(MammothActionType.Investigate);
                }
                else
                {
                    movement.Stop();
                    state?.SetAction(MammothActionType.Idle);
                }
                break;
            }

            case MammothActionType.Threaten:
                movement.Stop();
                movement.FaceTarget(target);
                combat?.StartThreatDisplay(target);
                break;

            case MammothActionType.ChasePlayer:
                if (movement.Chase(target))
                {
                    state?.SetAction(MammothActionType.ChasePlayer);
                }
                else if (movement.Investigate(state != null ? state.GetBestInvestigationPosition() : transform.position))
                {
                    state?.SetAction(MammothActionType.Investigate);
                }
                break;

            case MammothActionType.RunAway:
                if (movement.RunAwayFrom(target))
                {
                    state?.SetAction(MammothActionType.RunAway);
                }
                else if (movement.Roam())
                {
                    state?.SetAction(MammothActionType.Roam);
                }
                break;

            case MammothActionType.Charge:
                if (movement.ChargeToward(target))
                {
                    combat?.StartChargeDamageWindow(target);
                }
                else
                {
                    movement.Stop();
                    movement.FaceTarget(target);
                    combat?.StartThreatDisplay(target);
                }
                break;

            case MammothActionType.NormalAttack:
                movement.Stop();
                movement.FaceTarget(target);
                combat?.StartNormalAttack(target);
                break;

            case MammothActionType.Stomp:
                movement.Stop();
                combat?.StartStomp(target);
                break;

            case MammothActionType.TwistAttack:
                movement.Stop();
                combat?.StartTwistAttack(target);
                break;

            case MammothActionType.Recover:
                movement.Stop();
                state?.SetAction(MammothActionType.Recover);
                break;
        }
    }
}
