using UnityEngine;

// Taşınan bir parçayı tutabilen hedef (kesme kütüğü): parça bırakılınca (sağ tık) hedef üstündeyse ona konur
public interface IHoldTarget
{
    bool TryHold(GridPlaceable item);
}
