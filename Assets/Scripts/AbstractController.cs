using System;
using UnityEngine;

[Serializable]
public abstract class AbstractController
{
    public abstract Vector2 GetMovementInput();
    public abstract bool GetAttackInput();
    public abstract bool GetSpellInput();
}
