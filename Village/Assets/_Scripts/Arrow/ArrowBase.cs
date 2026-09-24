using System;
using UnityEngine;

public class ArrowBase : MonoBehaviour
{
    [SerializeField] private Transform visual;
    [SerializeField] protected Lenghts length;
    [SerializeField] protected GameObject[] closeVisuals;
    [SerializeField] protected GameObject[] openVisuals;
    [SerializeField] protected Transform animatedPart;
    [SerializeField] private Transform selectedActiveTransform;

    public bool isUsed;
    public Lenghts GetLength()
    {
        return length;
    }
    public virtual Transform VisualPart()
    {
        return animatedPart;
    }
       public Transform GetTransform()
    {
        return selectedActiveTransform;
    }
    public void OnUsed(Action<ArrowBase> logicCallBack)
    {
        isUsed = true;
        visual.gameObject.SetActive(false);
        foreach (var a in openVisuals) a.SetActive(true);
        foreach (var a in closeVisuals) a.SetActive(false);
        Logic(logicCallBack);
    }
    public virtual void Logic(Action<ArrowBase> logicCallBack)
    {
    }
    public void Selected(Vector3 localPosition, bool isSelected)
    {
        float currentY = transform.localPosition.y;

        if (isSelected)
        {
            Vector3 localRight = Quaternion.Euler(transform.localEulerAngles) * Vector3.right;
            localRight.y = 0f;

            transform.localPosition = localPosition + transform.right / 3f + Vector3.up * currentY;
        }
        else
        {
            transform.localPosition = localPosition + Vector3.up * currentY;
        }
    }
}

public enum Lenghts
{
    BR_1,
    BR_2,
    BR_2_BR1,
    BR_3
}