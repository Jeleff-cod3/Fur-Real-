using System;
using UnityEngine;

public class TreeSpiderHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth = 75;

    private int currentHealth;
    private bool isDead;

    public event Action<TreeSpiderHealth> Died;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;

    private void Awake()
    {
        currentHealth = Mathf.Max(1, maxHealth);
    }

    public void TakeDamage(int damage)
    {
        if (isDead)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(0, damage));
        Debug.Log($"{gameObject.name} took {damage} damage. HP: {currentHealth}/{maxHealth}");

        if (currentHealth > 0)
        {
            return;
        }

        isDead = true;
        Died?.Invoke(this);
        Destroy(gameObject);
    }
}
