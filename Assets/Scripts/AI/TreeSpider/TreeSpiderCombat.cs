using System.Collections;
using UnityEngine;

public class TreeSpiderCombat : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] private int biteDamage = 5;
    [SerializeField] private int dropDamage = 5;
    [SerializeField] private int grabDamage = 12;

    [Header("Timings")]
    [SerializeField] private float biteWindup = 0.18f;
    [SerializeField] private float biteRecovery = 0.32f;
    [SerializeField] private float grabChargeTime = 0.85f;
    [SerializeField] private float grabHoldTime = 4.25f;
    [SerializeField] private float dropDelay = 0.16f;
    [SerializeField] private float dropRecovery = 0.28f;

    [Header("Cooldowns")]
    [SerializeField] private float biteCooldown = 1.05f;
    [SerializeField] private float grabCooldown = 3.8f;
    [SerializeField] private float dropCooldown = 2f;

    [Header("Hit Detection")]
    [SerializeField] private Transform mouth;
    [SerializeField] private float biteRadius = 1f;
    [SerializeField] private float grabRadius = 1.2f;
    [SerializeField] private float grabEscapeStruggle = 4.2f;
    [SerializeField] private LayerMask playerLayerMask = ~0;

    private TreeSpiderState state;
    private TreeSpiderMovement movement;
    private Coroutine activeRoutine;
    private float nextBiteTime;
    private float nextGrabTime;
    private float nextDropTime;

    public bool CanBite => activeRoutine == null && Time.time >= nextBiteTime;
    public bool CanGrab => activeRoutine == null && Time.time >= nextGrabTime;
    public bool CanDrop => activeRoutine == null && Time.time >= nextDropTime;

    private void Awake()
    {
        state = GetComponent<TreeSpiderState>();
        movement = GetComponent<TreeSpiderMovement>();

        if (mouth == null)
        {
            Transform mouthTransform = transform.Find("Mouth");
            mouth = mouthTransform != null ? mouthTransform : transform;
        }
    }

    public bool StartBite(Transform target)
    {
        if (!CanBite)
        {
            return false;
        }

        activeRoutine = StartCoroutine(BiteRoutine(target));
        return true;
    }

    public bool StartGrab(Transform target)
    {
        if (!CanGrab)
        {
            return false;
        }

        activeRoutine = StartCoroutine(GrabRoutine(target));
        return true;
    }

    public bool StartDropAmbush(Transform target, bool guaranteedHit)
    {
        if (!CanDrop)
        {
            return false;
        }

        activeRoutine = StartCoroutine(DropRoutine(target, guaranteedHit));
        return true;
    }

    private IEnumerator BiteRoutine(Transform target)
    {
        BeginBusyAction(TreeSpiderActionType.Bite);
        movement?.Stop();
        movement?.FaceTarget(target);

        yield return new WaitForSeconds(biteWindup);

        PlayerHealth victim = FindPlayerHealthNearMouth(target, biteRadius);
        if (victim != null)
        {
            victim.TakeDamage(biteDamage);
        }

        yield return new WaitForSeconds(biteRecovery);

        nextBiteTime = Time.time + biteCooldown;
        EndBusyAction();
    }

    private IEnumerator GrabRoutine(Transform target)
    {
        BeginBusyAction(TreeSpiderActionType.Grab);
        movement?.Stop();
        movement?.FaceTarget(target);

        yield return new WaitForSeconds(grabChargeTime);

        PlayerHealth victim = FindPlayerHealthNearMouth(target, grabRadius);
        if (victim != null && !victim.IsDead)
        {
            TreeSpiderGrabbedVictim grabbedVictim = victim.GetComponent<TreeSpiderGrabbedVictim>();
            if (grabbedVictim == null)
            {
                grabbedVictim = victim.gameObject.AddComponent<TreeSpiderGrabbedVictim>();
            }

            grabbedVictim.BeginGrab(mouth, grabHoldTime, grabEscapeStruggle, grabDamage);

            while (grabbedVictim != null && grabbedVictim.IsHolding)
            {
                movement?.FaceTarget(victim.transform);
                yield return null;
            }
        }

        nextGrabTime = Time.time + grabCooldown;
        EndBusyAction();
    }

    private IEnumerator DropRoutine(Transform target, bool guaranteedHit)
    {
        BeginBusyAction(TreeSpiderActionType.DropAmbush);
        movement?.Stop();

        yield return new WaitForSeconds(dropDelay);

        PlayerHealth victim = guaranteedHit
            ? FindPlayerHealthOnTarget(target)
            : FindPlayerHealthNearMouth(target, biteRadius);

        if (victim != null)
        {
            victim.TakeDamage(dropDamage);
        }

        yield return new WaitForSeconds(dropRecovery);

        nextDropTime = Time.time + dropCooldown;
        EndBusyAction();
    }

    private PlayerHealth FindPlayerHealthNearMouth(Transform preferredTarget, float radius)
    {
        PlayerHealth preferred = FindPlayerHealthOnTarget(preferredTarget);
        if (preferred != null && Vector3.Distance(mouth.position, preferred.transform.position) <= radius + 0.75f)
        {
            return preferred;
        }

        Collider[] hits = Physics.OverlapSphere(
            mouth.position,
            radius,
            playerLayerMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            PlayerHealth playerHealth = hit.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                playerHealth = hit.GetComponentInParent<PlayerHealth>();
            }

            if (playerHealth != null && !playerHealth.IsDead)
            {
                return playerHealth;
            }
        }

        return null;
    }

    private static PlayerHealth FindPlayerHealthOnTarget(Transform target)
    {
        if (target == null)
        {
            return null;
        }

        PlayerHealth health = target.GetComponent<PlayerHealth>();
        if (health != null)
        {
            return health;
        }

        return target.GetComponentInParent<PlayerHealth>();
    }

    private void BeginBusyAction(TreeSpiderActionType actionType)
    {
        if (state != null)
        {
            state.isBusy = true;
            state.SetAction(actionType);
        }
    }

    private void EndBusyAction()
    {
        if (state != null)
        {
            state.isBusy = false;
        }

        activeRoutine = null;
    }
}
