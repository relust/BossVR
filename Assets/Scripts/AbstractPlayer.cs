using UnityEngine;

public abstract class AbstractPlayer : MonoBehaviour
{
    [SerializeField] private float _spellCooldown;
    [SerializeField] private float _damage;
    [SerializeField] private float _movementSpeed = 5f;
    [SerializeField] private float _combatMovementSpeed = 2f;
    [SerializeField] private float _rotationSpeed = 720f;
    [SerializeField] private Animator _legsAnimator;
    [SerializeField] private Animator _bodyAnimator;

    public float Damage => _damage;
    protected Animator BodyAnimator => _bodyAnimator;

    private AbstractController _controller;
    private bool _allowFollowup;
    private int _attackCombo = 0;
    protected float _lastSpellTime = -Mathf.Infinity;

    public void SetController(AbstractController controller)
    {
        _controller = controller;
    }

    protected virtual void Start()
    {
        if (_controller == null)
        {
            _controller = new DummyController();
        }
    }

    protected virtual void Update()
    {
        Move(_controller.GetMovementInput());

        if (_bodyAnimator != null && _bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("idle"))
        {
            _allowFollowup = false;
            _attackCombo = 0;
        }

        if (_controller.GetAttackInput())
        {
            Attack();
        }

        if (_controller.GetSpellInput())
        {
            CastSpell();
        }
    }

    public virtual void Move(Vector2 joystick)
    {
        Vector3 moveDir = new Vector3(joystick.x, 0, joystick.y);
        
        if (Camera.main != null)
        {
            float camYaw = Camera.main.transform.eulerAngles.y;
            moveDir = Quaternion.Euler(0, camYaw, 0) * moveDir;
        }

        float currentSpeed = GetCurrentMovementSpeed();

        transform.position += moveDir * currentSpeed * Time.deltaTime;

        if (moveDir != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
        }

        if (_legsAnimator != null)
        {
            if (joystick != Vector2.zero)
            {
                _legsAnimator.Play("run");
            }
            else
            {
                _legsAnimator.Play("idle");
            }
        }
    }

    protected virtual float GetCurrentMovementSpeed()
    {
        bool isIdle = _bodyAnimator == null || _bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("idle");
        return isIdle ? _movementSpeed : _combatMovementSpeed;
    }

    public void Attack()
    {
        if (_bodyAnimator == null) return;

        bool isIdle = _bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("idle");

        if (isIdle || _allowFollowup)
        {
            CancelFollowup();

            if (_attackCombo == 0)
            {
                _bodyAnimator.Play("attackInit");
                _attackCombo = 2;
            }
            else if (_attackCombo == 1)
            {
                _bodyAnimator.Play("attack1");
                _attackCombo = 2;
            }
            else if (_attackCombo == 2)
            {
                _bodyAnimator.Play("attack2");
                _attackCombo = 1;
            }

            PerformAttack();
        }
    }

    public void AllowFollowup()
    {
        _allowFollowup = true;
    }

    public void CancelFollowup()
    {
        _allowFollowup = false;
    }

    protected virtual void PerformAttack() { }

    public void CastSpell()
    {
        if (_bodyAnimator == null) return;

        bool isIdle = _bodyAnimator.GetCurrentAnimatorStateInfo(0).IsName("idle");

        if ((isIdle || _allowFollowup) && Time.time >= _lastSpellTime + _spellCooldown)
        {
            CancelFollowup();
            _lastSpellTime = Time.time;
            PerformSpell();
        }
    }

    protected abstract void PerformSpell();
}
