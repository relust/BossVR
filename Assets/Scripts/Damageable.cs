using UnityEngine;

public class Damageable : MonoBehaviour
{
    [SerializeField] private float _maxHealth;
    [SerializeField] private Team _team;
    [SerializeField] private float _health;

    public Team Team => _team;
    public Collider Collider { get; private set; }

    private void Awake()
    {
        _health = _maxHealth;
        Collider = GetComponent<Collider>();
    }

    public virtual void TakeDamage(float damage, Collider attackerCollider = null)
    {
        _health -= damage;
        if (_health <= 0)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        _health = Mathf.Min(_health + amount, _maxHealth);
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
