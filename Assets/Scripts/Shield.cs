using UnityEngine;

public class Shield : MonoBehaviour
{
    private const float BlockWidth = 2f;
    private Team _team;
    private Collider _shieldCollider;

    private void Awake()
    {
        _shieldCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        ShieldedDamageable parentDamageable = GetComponentInParent<ShieldedDamageable>();
        if (parentDamageable != null)
        {
            _team = parentDamageable.Team;
            parentDamageable.SetShield(this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled) return;

        Projectile projectile = other.GetComponent<Projectile>();
        if (projectile != null && projectile.Team != _team)
        {
            Destroy(projectile.gameObject);
        }
    }

    public bool IsBlocking(Collider attacker, Collider defender)
    {
        if (_shieldCollider == null || attacker == null || defender == null) return false;

        if (_shieldCollider.bounds.Intersects(attacker.bounds)) 
        {
            return true;
        }

        Vector3 attackerPoint = attacker.ClosestPoint(defender.bounds.center);
        Vector3 defenderPoint = defender.ClosestPoint(attacker.bounds.center);
        
        Vector3 dir = defenderPoint - attackerPoint;
        float dist = dir.magnitude;
        
        if (dist > 0.01f)
        {
            Vector3 halfExtents = new Vector3(BlockWidth / 2f, BlockWidth / 2f, 0.01f);
            RaycastHit[] hits = Physics.BoxCastAll(
                attackerPoint, 
                halfExtents, 
                dir.normalized, 
                Quaternion.LookRotation(dir.normalized), 
                dist, 
                Physics.AllLayers, 
                QueryTriggerInteraction.Collide);

            foreach (var hit in hits)
            {
                if (hit.collider == _shieldCollider)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
