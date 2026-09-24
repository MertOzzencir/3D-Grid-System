using UnityEngine;

public class Axe : ToolBase
{
    protected override bool OnUseWithoutTarget()
    {
        return false;
    }
}