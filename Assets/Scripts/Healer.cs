using UnityEngine;

public class Healer : AbstractPlayer
{
    [SerializeField] private GameObject _boltPrefab;
    [SerializeField] private Transform _boltSpawnPos;
    [SerializeField] private float _boltSpeed = 15f;

    [SerializeField] private GameObject _healingProjectilePrefab;
    [SerializeField] private Transform _healingProjectileSpawnPos;
    [SerializeField] private float _healingProjectileSpeed = 15f;
    [SerializeField] private float _healingProjectileUpwardSpeed = 5f;
    [SerializeField] private float _healAmount = 20f;
    private Damageable _damageable;

    protected override void Start()
    {
        base.Start();
        _damageable = GetComponent<Damageable>();
    }

    protected override void PerformSpell()
    {
        if (BodyAnimator != null)
        {
            BodyAnimator.SetTrigger("heal");
        }
    }

    public void SpawnBolt()
    {
        if (_boltPrefab != null && _boltSpawnPos != null)
        {
            GameObject bolt = Instantiate(_boltPrefab, _boltSpawnPos.position, transform.rotation);
            Rigidbody rb = bolt.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = transform.forward * _boltSpeed;
            }
            
            Projectile proj = bolt.GetComponent<Projectile>();
            if (proj != null && _damageable != null)
            {
                proj.Team = _damageable.Team;
                proj.Damage = Damage;
            }
        }
    }

    public void SpawnHealingProjectile()
    {
        if (_healingProjectilePrefab != null && _healingProjectileSpawnPos != null)
        {
            GameObject healingProj = Instantiate(_healingProjectilePrefab, _healingProjectileSpawnPos.position, transform.rotation);
            Rigidbody rb = healingProj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 vel = transform.forward * _healingProjectileSpeed;
                vel.y = _healingProjectileUpwardSpeed;
                rb.linearVelocity = vel;
            }
            
            HeallingProjectile hp = healingProj.GetComponent<HeallingProjectile>();
            if (hp != null && _damageable != null)
            {
                hp.Team = _damageable.Team;
                hp.HealAmount = _healAmount;
                hp.Spawner = _damageable;
            }
        }
    }
}
