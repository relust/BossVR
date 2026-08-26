using UnityEngine;

public class Knight : AbstractPlayer
{
    protected override void PerformSpell()
    {
        if (BodyAnimator != null)
        {
            BodyAnimator.SetTrigger("shield");
        }
    }
}
