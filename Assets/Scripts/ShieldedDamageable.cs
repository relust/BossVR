using UnityEngine;

public class ShieldedDamageable : Damageable
{
    private Shield _activeShield;

    public void SetShield(Shield shield)
    {
        _activeShield = shield;
    }

    public override void TakeDamage(float damage, Collider attackerCollider = null)
    {
        if (_activeShield != null && _activeShield.isActiveAndEnabled && attackerCollider != null)
        {
            if (_activeShield.IsBlocking(attackerCollider, Collider))
            {
                return; // Damage negated
            }
        }

        base.TakeDamage(damage, attackerCollider);
    }
}
