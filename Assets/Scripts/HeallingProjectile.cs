using UnityEngine;

public class HeallingProjectile : MonoBehaviour
{
    [SerializeField] private float _gravityMultiplier = 1f;

    public float HealAmount { get; set; }
    public Team Team { get; set; }
    public Damageable Spawner { get; set; }

    private float _spawnTime;
    private int _bounces = 0;
    private Rigidbody _rb;
    private Collider _collider;
    private Collider[] _ignoredColliders;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        if (_rb != null)
        {
            _rb.useGravity = false;
        }
    }

    private void Start()
    {
        _spawnTime = Time.time;
        if (Spawner != null && _collider != null)
        {
            _ignoredColliders = Spawner.GetComponentsInChildren<Collider>();
            foreach (var col in _ignoredColliders)
            {
                Physics.IgnoreCollision(_collider, col, true);
            }
        }
    }

    private void Update()
    {
        if (_ignoredColliders != null && Time.time >= _spawnTime + 1f)
        {
            foreach (var col in _ignoredColliders)
            {
                if (col != null && _collider != null)
                {
                    Physics.IgnoreCollision(_collider, col, false);
                }
            }
            _ignoredColliders = null;
        }
    }

    private void FixedUpdate()
    {
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.AddForce(Physics.gravity * _gravityMultiplier, ForceMode.Acceleration);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        Damageable damageable = collision.collider.GetComponent<Damageable>();
        if (damageable != null && damageable.Team == Team)
        {
            if (damageable != Spawner || Time.time >= _spawnTime + 1f)
            {
                damageable.Heal(HealAmount);
                Destroy(gameObject);
                return;
            }
        }

        if (collision.gameObject.CompareTag("Ground"))
        {
            _bounces++;
            if (_bounces >= 3)
            {
                if (_rb != null)
                {
                    _rb.isKinematic = true;
                    _rb.linearVelocity = Vector3.zero;
                }

                float halfHeight = _collider != null ? _collider.bounds.extents.y : 0.5f;
                Vector3 pos = transform.position;
                pos.y = halfHeight;
                transform.position = pos;
            }
        }
    }
}
