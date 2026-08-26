using UnityEngine;

public class Projectile : MonoBehaviour
{
    private Collider _collider;
    public float Damage { get; set; }

    public Team Team { get; set; }

    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Wall"))
        {
            DestroySelf();
            return;
        }

        Damageable damageable = other.GetComponent<Damageable>();
        if (damageable != null && damageable.Team != Team)
        {
            damageable.TakeDamage(Damage, _collider);
            DestroySelf();
        }
    }

    private void DestroySelf()
    {
        Destroy(gameObject);
    }
}
