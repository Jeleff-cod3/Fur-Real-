using System;
using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    private float invulnerableUntilTime;

    public event Action<PlayerHealth> Died;
    public event Action<int, int> HealthChanged;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float HealthPercent => maxHealth <= 0 ? 0f : (float)currentHealth / maxHealth;
    public bool IsDead => currentHealth <= 0;
    public bool IsInvulnerable => Time.time < invulnerableUntilTime;

    private void Awake()
    {
        currentHealth = maxHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(int damage)
    {
        if (IsDead || IsInvulnerable)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        HealthChanged?.Invoke(currentHealth, maxHealth);

        Debug.Log($"{gameObject.name} took {damage} damage. HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (IsDead)
        {
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void RestoreFullHealth()
    {
        currentHealth = maxHealth;
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void SetInvulnerableFor(float durationSeconds)
    {
        invulnerableUntilTime = Mathf.Max(invulnerableUntilTime, Time.time + Mathf.Max(0f, durationSeconds));
    }

    private void Die()
    {
        Died?.Invoke(this);
        Debug.Log($"{gameObject.name} died.");
    }
}
