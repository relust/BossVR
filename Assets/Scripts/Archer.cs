using UnityEngine;
using System.Collections.Generic;

public class Archer : AbstractPlayer
{
    [SerializeField] private GameObject _arrowPrefab;
    [SerializeField] private Transform _arrowSpawnPos;
    [SerializeField] private float _arrowSpeed = 20f;
    
    [SerializeField] private float _invisibilityDuration = 5f;
    [SerializeField] private float _invisibilityOpacity = 0.2f;
    [SerializeField] private Material _transparentMaterialTemplate;
    [SerializeField] private float _invisibleMovementSpeed = 8f;

    private Damageable _damageable;
    private bool _isInvisible;
    private float _invisibilityEndTime;
    private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private Renderer[] _renderers;

    protected override void Start()
    {
        base.Start();
        _damageable = GetComponent<Damageable>();

        _renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in _renderers)
        {
            _originalMaterials[r] = r.materials;
        }
    }

    protected override float GetCurrentMovementSpeed()
    {
        if (_isInvisible)
        {
            return _invisibleMovementSpeed;
        }
        return base.GetCurrentMovementSpeed();
    }

    protected override void PerformSpell()
    {
        if (BodyAnimator != null)
        {
            BodyAnimator.Play("invisible");
        }

        if (!_isInvisible)
        {
            _isInvisible = true;
            _invisibilityEndTime = Time.time + _invisibilityDuration;

            if (_transparentMaterialTemplate != null)
            {
                foreach (var r in _renderers)
                {
                    Material[] newMats = new Material[r.materials.Length];
                    for (int i = 0; i < newMats.Length; i++)
                    {
                        Material mat = new Material(_transparentMaterialTemplate);
                        if (r.materials[i].HasProperty("_MainTex"))
                        {
                            mat.mainTexture = r.materials[i].mainTexture;
                        }
                        if (mat.HasProperty("_Color"))
                        {
                            Color c = mat.color;
                            c.a = _invisibilityOpacity;
                            mat.color = c;
                        }
                        newMats[i] = mat;
                    }
                    r.materials = newMats;
                }
            }
        }
    }

    protected override void Update()
    {
        base.Update();
        if (_isInvisible && Time.time >= _invisibilityEndTime)
        {
            RevertInvisibility();
        }
    }

    protected override void PerformAttack()
    {
        base.PerformAttack();
        if (_isInvisible)
        {
            RevertInvisibility();
        }
    }

    private void RevertInvisibility()
    {
        _isInvisible = false;
        _lastSpellTime = Time.time;

        foreach (var r in _renderers)
        {
            if (_originalMaterials.TryGetValue(r, out Material[] origMats))
            {
                r.materials = origMats;
            }
        }
    }

    public void SpawnArrow()
    {
        if (_arrowPrefab != null && _arrowSpawnPos != null)
        {
            GameObject arrow = Instantiate(_arrowPrefab, _arrowSpawnPos.position, transform.rotation);
            Rigidbody rb = arrow.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = transform.forward * _arrowSpeed;
            }
            
            Projectile proj = arrow.GetComponent<Projectile>();
            if (proj != null && _damageable != null)
            {
                proj.Team = _damageable.Team;
                proj.Damage = Damage;
            }
        }
    }
}
