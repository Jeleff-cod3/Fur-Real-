using UnityEngine;
using System;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    private bool hasDied;
    private float ignoreNetworkDeathUntil;
    [NonSerialized] private int configuredMaxHealth = -1;

    private MammothState mammothState;
    private MammothPersonality mammothPersonality;
    private MammothSenses mammothSenses;

    public event Action<int, int> HealthChanged;
    public event Action<EnemyHealth> Died;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public int ConfiguredMaxHealth
    {
        get
        {
            CaptureConfiguredMaxHealth();
            return configuredMaxHealth;
        }
    }

    public float HealthPercent => maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
    public bool IsDead => currentHealth <= 0 || hasDied;

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            configuredMaxHealth = Mathf.Max(1, maxHealth);
        }
    }

    private void Awake()
    {
        CaptureConfiguredMaxHealth();
        CacheComponents();
        ResetHealthToFull(1.5f);
    }

    private void OnEnable()
    {
        CaptureConfiguredMaxHealth();
        CacheComponents();

        if (currentHealth <= 0)
        {
            ResetHealthToFull(1.5f);
        }

        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void ResetHealthToFull(float networkDeathProtectionSeconds = 1.5f)
    {
        CaptureConfiguredMaxHealth();
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        hasDied = false;
        ignoreNetworkDeathUntil = Time.time + networkDeathProtectionSeconds;

        CacheComponents();
        ResetMammothState();

        HealthChanged?.Invoke(currentHealth, maxHealth);

        Debug.Log($"{gameObject.name} health reset to {currentHealth}/{maxHealth}");
    }

    public void ResetToConfiguredMaxHealth(float networkDeathProtectionSeconds = 1.5f)
    {
        CaptureConfiguredMaxHealth();
        maxHealth = configuredMaxHealth;
        ResetHealthToFull(networkDeathProtectionSeconds);
    }

    public void SetMaxHealthAndReset(int newMaxHealth, float networkDeathProtectionSeconds = 1.5f)
    {
        CaptureConfiguredMaxHealth();
        maxHealth = Mathf.Max(1, newMaxHealth);
        ResetHealthToFull(networkDeathProtectionSeconds);
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, null, null);
    }

    public void TakeDamage(int damage, Vector3 sourcePosition)
    {
        TakeDamage(damage, null, sourcePosition);
    }

    public void TakeDamage(int damage, Transform sourceTransform)
    {
        TakeDamage(damage, sourceTransform, sourceTransform != null ? sourceTransform.position : (Vector3?)null);
    }

    public void TakeDamage(int damage, Transform sourceTransform, Vector3? sourcePosition)
    {
        if (IsDead)
        {
            return;
        }

        damage = Mathf.Max(0, damage);
        if (damage <= 0)
        {
            return;
        }

        Vector3? resolvedSourcePosition = ResolveSourcePosition(sourceTransform, sourcePosition);
        float normalizedDamage = maxHealth > 0 ? Mathf.Clamp01((float)damage / maxHealth) : 0f;

        currentHealth = Mathf.Max(0, currentHealth - damage);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        MultiplayerPrototype.NotifyEnemyDamaged(this, damage);

        if (mammothState != null)
        {
            mammothState.MarkDamaged(resolvedSourcePosition);
        }

        bool canSeeThreat = mammothSenses != null && mammothSenses.CanSeeTarget;
        bool closeThreat = resolvedSourcePosition.HasValue &&
            Vector3.Distance(transform.position, resolvedSourcePosition.Value) <= 9f;
        bool repeatedThreat = mammothState != null && mammothState.repeatedThreatHitCount >= 2;
        bool hasEmbeddedSpears = mammothState != null && mammothState.HasEmbeddedSpears;

        if (mammothSenses != null && resolvedSourcePosition.HasValue)
        {
            mammothSenses.ReportSuspiciousSound(resolvedSourcePosition.Value, 0.85f + normalizedDamage);
        }

        if (mammothPersonality != null)
        {
            mammothPersonality.RegisterThreatEvent(
                normalizedDamage,
                canSeeThreat,
                closeThreat,
                repeatedThreat,
                hasEmbeddedSpears
            );
        }

        Debug.Log($"{gameObject.name} took {damage} damage. HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void ApplyNetworkHealth(int newCurrentHealth, int newMaxHealth)
    {
        ApplyNetworkHealth(newCurrentHealth, newMaxHealth, 0);
    }

    public void ApplyNetworkHealth(int newCurrentHealth, int newMaxHealth, int damage)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        int clampedHealth = Mathf.Clamp(newCurrentHealth, 0, maxHealth);

        if (clampedHealth <= 0 && Time.time < ignoreNetworkDeathUntil)
        {
            Debug.Log($"{gameObject.name} ignored stale network death during spawn protection.");
            HealthChanged?.Invoke(currentHealth, maxHealth);
            return;
        }

        if (currentHealth == clampedHealth && hasDied == (clampedHealth <= 0))
        {
            return;
        }

        currentHealth = clampedHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            hasDied = false;
        }
    }

    private void Die()
    {
        if (hasDied)
        {
            return;
        }

        hasDied = true;
        Died?.Invoke(this);

        Debug.Log($"{gameObject.name} died.");
        Destroy(gameObject);
    }

    private void CacheComponents()
    {
        if (mammothState == null)
        {
            mammothState = GetComponent<MammothState>();
        }

        if (mammothPersonality == null)
        {
            mammothPersonality = GetComponent<MammothPersonality>();
        }

        if (mammothSenses == null)
        {
            mammothSenses = GetComponent<MammothSenses>();
        }
    }

    private void ResetMammothState()
    {
        if (mammothState == null)
        {
            return;
        }

        mammothState.isBusy = false;
        mammothState.isAttacking = false;
        mammothState.isCharging = false;
        mammothState.isRecovering = false;
        mammothState.currentAction = MammothActionType.Idle;
        mammothState.previousAction = MammothActionType.Idle;
        mammothState.currentTarget = null;
        mammothState.lastKnownTargetPosition = Vector3.zero;
        mammothState.lastHeardThreatPosition = Vector3.zero;
        mammothState.lastDamageSourcePosition = Vector3.zero;
        mammothState.lastDamageDirection = Vector3.zero;
        mammothState.lastDamageTime = -999f;
        mammothState.lastTargetSeenTime = 0f;
        mammothState.lastTargetLostTime = 0f;
        mammothState.lastHeardThreatTime = 0f;
        mammothState.lastThreatenTime = 0f;
        mammothState.repeatedThreatHitCount = 0;
        mammothState.hasDamageSource = false;
        mammothState.embeddedSpearCount = 0;
        mammothState.lastEmbeddedSpearTime = 0f;
        mammothState.lastActionChangeTime = Time.time;
        mammothSenses?.ResetAwareness();
        mammothPersonality?.ResetRuntimeEmotion();
    }

    private void CaptureConfiguredMaxHealth()
    {
        if (configuredMaxHealth < 1)
        {
            configuredMaxHealth = Mathf.Max(1, maxHealth);
        }
    }

    private static Vector3? ResolveSourcePosition(Transform sourceTransform, Vector3? sourcePosition)
    {
        if (sourcePosition.HasValue)
        {
            return sourcePosition.Value;
        }

        if (sourceTransform != null)
        {
            return sourceTransform.position;
        }

        return null;
    }
}
