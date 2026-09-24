using System;
using UnityEngine;

public class ArrowRoot : ArrowBase
{
    public override void Logic(Action<ArrowBase> logicCallBack)
    {
        base.Logic(logicCallBack);
        logicCallBack?.Invoke(this);
        Destroy(gameObject);
    }

}
