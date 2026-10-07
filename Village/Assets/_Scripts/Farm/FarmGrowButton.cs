using UnityEngine;

// Tarlanın büyüme düğmesi: boş elle sol tıklanınca FarmGrower'a haber verir (collider bu objede, IClickable).
public class FarmGrowButton : MonoBehaviour, IClickable
{
    public FarmGrower Grower { get; set; }

    public void OnClicked(RaycastHit hit) => Grower?.Press();
}
