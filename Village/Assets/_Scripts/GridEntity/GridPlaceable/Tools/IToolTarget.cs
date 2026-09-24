using UnityEngine;

public interface IToolTarget
{
    Vector3 GetToolTargetPosition(IInteractable interacted,out bool accept);
    bool OnToolUsed(IInteractable tool);
    void OnToolTargetExit();
    Quaternion GetToolTargetRotation();
}