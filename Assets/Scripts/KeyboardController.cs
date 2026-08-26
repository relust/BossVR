using System;
using UnityEngine;

[Serializable]
public class KeyboardController : AbstractController
{
    public override Vector2 GetMovementInput()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        return new Vector2(horizontal, vertical);
    }

    public override bool GetAttackInput()
    {
        return Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
    }

    public override bool GetSpellInput()
    {
        return Input.GetKey(KeyCode.F) || Input.GetMouseButton(1);
    }
}
