using UnityEngine;

// Elde bir şey yokken sol tıkla tıklanınca haberdar olan obje (örn. kedinin kafasını sevmek).
// InteractableController mouse'un altındaki ilk collider'ın objesinde arar.
public interface IClickable
{
    void OnClicked(RaycastHit hit);
}
