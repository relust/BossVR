using UnityEngine;
using System.Collections.Generic;

public class Sword : MonoBehaviour
{
    private float _damage;
    private Team _team;
    
    private HashSet<Damageable> _damagedEntities = new HashSet<Damageable>();
    private Collider _collider;
    private Damageable _ownerDamageable;

    public Team Team => _team;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    private void Start()
    {
        _ownerDamageable = GetComponentInParent<Damageable>();
        if (_ownerDamageable != null)
        {
            _team = _ownerDamageable.Team;
        }

        AbstractPlayer parentPlayer = GetComponentInParent<AbstractPlayer>();
        if (parentPlayer != null)
        {
            _damage = parentPlayer.Damage;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Damageable damageable = other.GetComponent<Damageable>();
        if (damageable != null && damageable.Team != _team && !_damagedEntities.Contains(damageable))
        {
            Collider attackerCollider = _ownerDamageable != null ? _ownerDamageable.Collider : _collider;
            damageable.TakeDamage(_damage, attackerCollider);
            _damagedEntities.Add(damageable);
        }
    }

    private void OnDisable()
    {
        _damagedEntities.Clear();
    }
}
