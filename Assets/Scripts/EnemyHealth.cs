using UnityEngine;
using System;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    private bool hasDied;
    private float ignoreNetworkDeathUntil;

    private MammothState mammothState;
    private MammothPersonality mammothPersonality;

    public event Action<int, int> HealthChanged;
    public event Action<EnemyHealth> Died;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float HealthPercent => maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
    public bool IsDead => currentHealth <= 0 || hasDied;

    private void Awake()
    {
        CacheComponents();
        ResetHealthToFull(1.5f);
    }

    private void OnEnable()
    {
        CacheComponents();

        if (currentHealth <= 0)
        {
            ResetHealthToFull(1.5f);
        }

        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void ResetHealthToFull(float networkDeathProtectionSeconds = 1.5f)
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = maxHealth;
        hasDied = false;
        ignoreNetworkDeathUntil = Time.time + networkDeathProtectionSeconds;

        CacheComponents();
        ResetMammothState();

        HealthChanged?.Invoke(currentHealth, maxHealth);

        Debug.Log($"{gameObject.name} health reset to {currentHealth}/{maxHealth}");
    }

    public void TakeDamage(int damage)
    {
        if (IsDead)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        HealthChanged?.Invoke(currentHealth, maxHealth);
        MultiplayerPrototype.NotifyEnemyDamaged(this, damage);

        if (mammothState != null)
        {
            mammothState.MarkDamaged();
        }

        if (mammothPersonality != null)
        {
            mammothPersonality.AddAnger(0.18f);
            mammothPersonality.AddFear(0.08f);
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
        mammothState.lastDamageTime = -999f;
        mammothState.lastTargetSeenTime = 0f;
        mammothState.lastTargetLostTime = 0f;
        mammothState.lastActionChangeTime = Time.time;
    }
}