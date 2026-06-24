using System.Collections;
using UnityEngine;

public class MammothCombat : MonoBehaviour
{
    [System.Serializable]
    private class MammothContactProfile
    {
        [Tooltip("Optional body part that owns this contact volume. Leave empty to use the mammoth root.")]
        public Transform origin;
        public Vector3 localCenterOffset = Vector3.zero;
        public Vector3 localHitAxis = Vector3.forward;
        public Vector3 localAttackDirection = Vector3.forward;
        [Min(0.05f)] public float radius = 1f;
        [Min(0f)] public float capsuleLength = 0f;
        [Range(1f, 180f)] public float arcDegrees = 180f;
        [Range(0f, 1f)] public float minimumQuality = 0.25f;
        [Range(0.05f, 1f)] public float glancingDamageMultiplier = 0.55f;
        [Range(0.05f, 1.5f)] public float solidDamageMultiplier = 1f;
        [Range(0f, 0.25f)] public float preferredTargetScoreBonus = 0.05f;

        public static MammothContactProfile Create(
            Vector3 centerOffset,
            Vector3 hitAxis,
            Vector3 attackDirection,
            float radius,
            float capsuleLength,
            float arcDegrees,
            float minimumQuality,
            float glancingDamageMultiplier,
            float solidDamageMultiplier)
        {
            return new MammothContactProfile
            {
                localCenterOffset = centerOffset,
                localHitAxis = hitAxis,
                localAttackDirection = attackDirection,
                radius = radius,
                capsuleLength = capsuleLength,
                arcDegrees = arcDegrees,
                minimumQuality = minimumQuality,
                glancingDamageMultiplier = glancingDamageMultiplier,
                solidDamageMultiplier = solidDamageMultiplier
            };
        }

        public void EnsureUsable(MammothContactProfile fallback, float legacyRadius)
        {
            if (radius <= 0.05f)
            {
                radius = legacyRadius > 0.05f ? legacyRadius : fallback.radius;
            }

            if (capsuleLength < 0f)
            {
                capsuleLength = fallback.capsuleLength;
            }

            if (localHitAxis.sqrMagnitude <= 0.0001f)
            {
                localHitAxis = fallback.localHitAxis;
            }

            if (localAttackDirection.sqrMagnitude <= 0.0001f)
            {
                localAttackDirection = fallback.localAttackDirection;
            }

            if (arcDegrees <= 0f)
            {
                arcDegrees = fallback.arcDegrees;
            }

            if (solidDamageMultiplier < glancingDamageMultiplier)
            {
                solidDamageMultiplier = glancingDamageMultiplier;
            }
        }
    }

    private struct ContactVolume
    {
        public Vector3 center;
        public Vector3 capsuleStart;
        public Vector3 capsuleEnd;
        public Vector3 attackDirection;
        public float radius;
        public bool isCapsule;
    }

    private struct ContactCandidate
    {
        public PlayerHealth health;
        public Collider collider;
        public float quality;
        public float score;
    }

    [Header("Damage")]
    [SerializeField] private int normalAttackDamage = 15;
    [SerializeField] private int stompDamage = 25;
    [SerializeField] private int twistAttackDamage = 20;
    [SerializeField] private int chargeDamage = 35;

    [Header("Legacy Ranges")]
    [SerializeField] private float normalAttackRadius = 4f;
    [SerializeField] private float stompRadius = 3.2f;
    [SerializeField] private float twistAttackRadius = 4.5f;
    [SerializeField] private float chargeHitRadius = 3.5f;

    [Header("Contact Profiles")]
    [SerializeField] private MammothContactProfile normalAttackContact = CreateNormalFallback();
    [SerializeField] private MammothContactProfile stompContact = CreateStompFallback();
    [SerializeField] private MammothContactProfile twistContact = CreateTwistFallback();
    [SerializeField] private MammothContactProfile chargeContact = CreateChargeFallback();

    [Header("Timings")]
    [SerializeField] private float normalAttackDuration = 0.8f;
    [SerializeField] private float stompDuration = 1.1f;
    [SerializeField] private float twistDuration = 1f;
    [SerializeField] private float chargeDamageDelay = 0.4f;

    [Header("Cooldowns")]
    [SerializeField] private float normalAttackCooldown = 1.5f;
    [SerializeField] private float stompCooldown = 3f;
    [SerializeField] private float twistCooldown = 4f;
    [SerializeField] private float chargeCooldown = 5f;
    [SerializeField] private float threatenCooldown = 2.8f;

    [Header("Threat Display")]
    [SerializeField] private float threatenDuration = 1.1f;

    [Header("Hit Detection")]
    [SerializeField] private LayerMask playerLayerMask = ~0;
    [SerializeField] private bool logContactMisses;

    private MammothState state;
    private MammothMovement movement;
    private MammothPersonality personality;
    private float nextNormalAttackTime;
    private float nextStompTime;
    private float nextTwistTime;
    private float nextChargeTime;
    private float nextThreatenTime;

    public bool CanNormalAttack => Time.time >= nextNormalAttackTime;
    public bool CanStomp => Time.time >= nextStompTime;
    public bool CanTwistAttack => Time.time >= nextTwistTime;
    public bool CanCharge => Time.time >= nextChargeTime;
    public bool CanThreaten => Time.time >= nextThreatenTime;

    private void Awake()
    {
        state = GetComponent<MammothState>();
        movement = GetComponent<MammothMovement>();
        personality = GetComponent<MammothPersonality>();
        EnsureContactProfiles();
    }

    private void OnValidate()
    {
        EnsureContactProfiles();
    }

    public void StartNormalAttack(Transform target)
    {
        if (!CanNormalAttack)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.NormalAttack,
            target,
            normalAttackDamage,
            normalAttackContact,
            normalAttackDuration,
            () => nextNormalAttackTime = Time.time + normalAttackCooldown
        ));
    }

    public void StartStomp(Transform target)
    {
        if (!CanStomp)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.Stomp,
            target,
            stompDamage,
            stompContact,
            stompDuration,
            () => nextStompTime = Time.time + stompCooldown
        ));
    }

    public void StartTwistAttack(Transform target)
    {
        if (!CanTwistAttack)
        {
            return;
        }

        StartCoroutine(AttackRoutine(
            MammothActionType.TwistAttack,
            target,
            twistAttackDamage,
            twistContact,
            twistDuration,
            () => nextTwistTime = Time.time + twistCooldown
        ));
    }

    public void StartChargeDamageWindow(Transform target)
    {
        if (!CanCharge)
        {
            return;
        }

        StartCoroutine(ChargeDamageRoutine(target));
    }

    public void StartThreatDisplay(Transform target)
    {
        if (!CanThreaten)
        {
            return;
        }

        StartCoroutine(ThreatDisplayRoutine(target));
    }

    private IEnumerator AttackRoutine(
        MammothActionType actionType,
        Transform target,
        int damage,
        MammothContactProfile contactProfile,
        float duration,
        System.Action setCooldown)
    {
        if (state != null)
        {
            state.isBusy = true;
            state.isAttacking = true;
            state.SetAction(actionType);
        }

        Debug.Log($"Mammoth started {actionType}.");

        yield return new WaitForSeconds(duration * 0.45f);

        TryDamageTarget(target, damage, contactProfile, actionType.ToString());

        yield return new WaitForSeconds(duration * 0.55f);

        setCooldown?.Invoke();

        if (state != null)
        {
            state.isAttacking = false;
            state.isBusy = false;
        }

        Debug.Log($"Mammoth finished {actionType}.");
    }

    private IEnumerator ChargeDamageRoutine(Transform target)
    {
        if (state != null)
        {
            state.isCharging = true;
            state.isBusy = true;
            state.SetAction(MammothActionType.Charge);
        }

        nextChargeTime = Time.time + chargeCooldown;

        yield return new WaitForSeconds(chargeDamageDelay);

        TryDamageTarget(target, chargeDamage, chargeContact, "Charge");

        yield return new WaitForSeconds(0.8f);

        if (state != null)
        {
            state.isCharging = false;
            state.isBusy = false;
        }
    }

    private IEnumerator ThreatDisplayRoutine(Transform target)
    {
        if (state != null)
        {
            state.isBusy = true;
            state.isRecovering = true;
            state.SetAction(MammothActionType.Threaten);
            state.RecordThreatDisplay();
        }

        personality?.AddAlertness(0.08f);
        personality?.AddAnger(0.04f);
        nextThreatenTime = Time.time + threatenCooldown;

        float endTime = Time.time + threatenDuration;

        while (Time.time < endTime)
        {
            if (movement != null && target != null)
            {
                movement.FaceTarget(target);
            }

            yield return null;
        }

        if (state != null)
        {
            state.isRecovering = false;
            state.isBusy = false;
        }
    }

    private void TryDamageTarget(Transform target, int baseDamage, MammothContactProfile contactProfile, string attackName)
    {
        if (target == null || contactProfile == null || baseDamage <= 0)
        {
            return;
        }

        ContactVolume volume = BuildContactVolume(contactProfile);
        Collider[] hits = volume.isCapsule
            ? Physics.OverlapCapsule(volume.capsuleStart, volume.capsuleEnd, volume.radius, playerLayerMask, QueryTriggerInteraction.Ignore)
            : Physics.OverlapSphere(volume.center, volume.radius, playerLayerMask, QueryTriggerInteraction.Ignore);

        if (!TryFindBestContact(hits, target, contactProfile, volume, out ContactCandidate bestContact))
        {
            if (logContactMisses)
            {
                Debug.Log($"Mammoth {attackName} missed. No contact met the quality threshold.");
            }

            return;
        }

        float damageMultiplier = Mathf.Lerp(
            contactProfile.glancingDamageMultiplier,
            contactProfile.solidDamageMultiplier,
            bestContact.quality
        );
        int resolvedDamage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * damageMultiplier));

        bestContact.health.TakeDamage(resolvedDamage);
        Debug.Log(
            $"Mammoth {attackName} hit {bestContact.health.gameObject.name} for {resolvedDamage} damage " +
            $"(quality {bestContact.quality:0.00}, collider {bestContact.collider.name})."
        );
    }

    private bool TryFindBestContact(
        Collider[] hits,
        Transform target,
        MammothContactProfile profile,
        ContactVolume volume,
        out ContactCandidate bestContact)
    {
        bestContact = default(ContactCandidate);
        bool foundContact = false;

        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            PlayerHealth playerHealth = hit.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                playerHealth = hit.GetComponentInParent<PlayerHealth>();
            }

            if (playerHealth == null || playerHealth.IsDead)
            {
                continue;
            }

            float quality = CalculateContactQuality(hit, playerHealth.transform, profile, volume);
            if (quality < profile.minimumQuality)
            {
                continue;
            }

            float score = quality;
            if (IsPreferredTarget(playerHealth.transform, target))
            {
                score += profile.preferredTargetScoreBonus;
            }

            if (!foundContact || score > bestContact.score)
            {
                foundContact = true;
                bestContact = new ContactCandidate
                {
                    health = playerHealth,
                    collider = hit,
                    quality = quality,
                    score = score,
                };
            }
        }

        return foundContact;
    }

    private float CalculateContactQuality(
        Collider hit,
        Transform playerTransform,
        MammothContactProfile profile,
        ContactVolume volume)
    {
        Vector3 contactAnchor = volume.isCapsule
            ? ClosestPointOnSegment(playerTransform.position, volume.capsuleStart, volume.capsuleEnd)
            : volume.center;
        Vector3 contactPoint = hit.ClosestPoint(contactAnchor);
        float contactDistance = volume.isCapsule
            ? DistancePointToSegment(contactPoint, volume.capsuleStart, volume.capsuleEnd)
            : Vector3.Distance(contactPoint, volume.center);

        float depthQuality = Mathf.Clamp01(1f - contactDistance / Mathf.Max(0.001f, volume.radius));
        if (depthQuality <= 0f)
        {
            return 0f;
        }

        float alignmentQuality = CalculateAlignmentQuality(playerTransform.position, volume.center, volume.attackDirection, profile.arcDegrees);
        if (alignmentQuality <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(depthQuality * 0.72f + alignmentQuality * 0.28f);
    }

    private ContactVolume BuildContactVolume(MammothContactProfile profile)
    {
        Transform origin = profile.origin != null ? profile.origin : transform;
        Vector3 center = origin.TransformPoint(profile.localCenterOffset);
        Vector3 hitAxis = ResolveDirection(origin, profile.localHitAxis, Vector3.forward);
        Vector3 attackDirection = ResolveDirection(origin, profile.localAttackDirection, Vector3.forward);
        float halfLength = Mathf.Max(0f, profile.capsuleLength * 0.5f);

        return new ContactVolume
        {
            center = center,
            capsuleStart = center - hitAxis * halfLength,
            capsuleEnd = center + hitAxis * halfLength,
            attackDirection = attackDirection,
            radius = Mathf.Max(0.05f, profile.radius),
            isCapsule = halfLength > 0.001f
        };
    }

    private static float CalculateAlignmentQuality(Vector3 playerPosition, Vector3 contactCenter, Vector3 attackDirection, float arcDegrees)
    {
        if (arcDegrees >= 179.9f)
        {
            return 1f;
        }

        Vector3 toPlayer = playerPosition - contactCenter;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= 0.0001f)
        {
            return 1f;
        }

        Vector3 flatAttackDirection = attackDirection;
        flatAttackDirection.y = 0f;

        if (flatAttackDirection.sqrMagnitude <= 0.0001f)
        {
            return 1f;
        }

        float dot = Vector3.Dot(flatAttackDirection.normalized, toPlayer.normalized);
        float minimumDot = Mathf.Cos(arcDegrees * 0.5f * Mathf.Deg2Rad);
        return dot < minimumDot ? 0f : Mathf.InverseLerp(minimumDot, 1f, dot);
    }

    private static float DistancePointToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        return Vector3.Distance(point, ClosestPointOnSegment(point, segmentStart, segmentEnd));
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;

        if (lengthSquared <= 0.0001f)
        {
            return segmentStart;
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) / lengthSquared);
        return segmentStart + segment * t;
    }

    private static Vector3 ResolveDirection(Transform origin, Vector3 localDirection, Vector3 fallback)
    {
        Vector3 direction = localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : fallback;
        return origin.TransformDirection(direction).normalized;
    }

    private static bool IsPreferredTarget(Transform playerTransform, Transform target)
    {
        if (playerTransform == null || target == null)
        {
            return false;
        }

        return playerTransform == target ||
            playerTransform.IsChildOf(target) ||
            target.IsChildOf(playerTransform);
    }

    private void EnsureContactProfiles()
    {
        MammothContactProfile normalFallback = CreateNormalFallback();
        MammothContactProfile stompFallback = CreateStompFallback();
        MammothContactProfile twistFallback = CreateTwistFallback();
        MammothContactProfile chargeFallback = CreateChargeFallback();

        if (normalAttackContact == null)
        {
            normalAttackContact = normalFallback;
        }

        if (stompContact == null)
        {
            stompContact = stompFallback;
        }

        if (twistContact == null)
        {
            twistContact = twistFallback;
        }

        if (chargeContact == null)
        {
            chargeContact = chargeFallback;
        }

        normalAttackContact.EnsureUsable(normalFallback, normalAttackRadius);
        stompContact.EnsureUsable(stompFallback, stompRadius);
        twistContact.EnsureUsable(twistFallback, twistAttackRadius);
        chargeContact.EnsureUsable(chargeFallback, chargeHitRadius);
    }

    private static MammothContactProfile CreateNormalFallback()
    {
        return MammothContactProfile.Create(
            new Vector3(0f, 1.25f, 2.35f),
            Vector3.forward,
            Vector3.forward,
            1.45f,
            1.7f,
            80f,
            0.28f,
            0.55f,
            1.1f
        );
    }

    private static MammothContactProfile CreateStompFallback()
    {
        return MammothContactProfile.Create(
            new Vector3(0f, 0.65f, 0.95f),
            Vector3.right,
            Vector3.forward,
            1.15f,
            2.8f,
            150f,
            0.3f,
            0.65f,
            1.15f
        );
    }

    private static MammothContactProfile CreateTwistFallback()
    {
        return MammothContactProfile.Create(
            new Vector3(0f, 1.15f, -0.95f),
            Vector3.right,
            Vector3.back,
            1.45f,
            4f,
            130f,
            0.28f,
            0.55f,
            1.05f
        );
    }

    private static MammothContactProfile CreateChargeFallback()
    {
        return MammothContactProfile.Create(
            new Vector3(0f, 1.25f, 2.65f),
            Vector3.forward,
            Vector3.forward,
            1.35f,
            3f,
            55f,
            0.32f,
            0.7f,
            1.25f
        );
    }

    private void OnDrawGizmosSelected()
    {
        EnsureContactProfiles();
        DrawContactGizmo(normalAttackContact, new Color(1f, 0.45f, 0.2f, 0.35f));
        DrawContactGizmo(stompContact, new Color(1f, 0.1f, 0.1f, 0.35f));
        DrawContactGizmo(twistContact, new Color(0.35f, 0.7f, 1f, 0.35f));
        DrawContactGizmo(chargeContact, new Color(1f, 0.9f, 0.15f, 0.35f));
    }

    private void DrawContactGizmo(MammothContactProfile profile, Color color)
    {
        if (profile == null)
        {
            return;
        }

        ContactVolume volume = BuildContactVolume(profile);
        Gizmos.color = color;

        if (volume.isCapsule)
        {
            Gizmos.DrawWireSphere(volume.capsuleStart, volume.radius);
            Gizmos.DrawWireSphere(volume.capsuleEnd, volume.radius);
            Gizmos.DrawLine(volume.capsuleStart, volume.capsuleEnd);
        }
        else
        {
            Gizmos.DrawWireSphere(volume.center, volume.radius);
        }

        Gizmos.DrawRay(volume.center, volume.attackDirection * (volume.radius + Mathf.Max(0.5f, profile.capsuleLength * 0.5f)));
    }
}
