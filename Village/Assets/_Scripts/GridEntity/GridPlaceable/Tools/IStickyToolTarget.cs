using UnityEngine;

// Bir kez yapışınca mouse collider'dan çıksa da bırakılmayan hedef. GridDragMotor her kare önce bunu sorar;
// hedef "hâlâ bendesin" dediği sürece ray ile yeni hedef aranmaz. Bırakma kuralını (örn. mouse'un hedefin
// düzleminde belli bir mesafeden uzaklaşması) hedef kendisi belirler.
public interface IStickyToolTarget : IToolTarget
{
    bool IsStillTargeted(IInteractable interacted, Ray mouseRay);
}
