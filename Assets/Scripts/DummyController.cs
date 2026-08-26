using UnityEngine;

public class DummyController : AbstractController
{
    public override Vector2 GetMovementInput()
    {
        return Vector2.zero;
    }

    public override bool GetAttackInput()
    {
        return true;
    }

    public override bool GetSpellInput()
    {
        return false;
    }
}
