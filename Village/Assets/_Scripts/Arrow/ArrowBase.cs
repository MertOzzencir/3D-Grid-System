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

    [Tooltip("Bu oku kesmek için gereken balta vuruşu sayısı")]
    [SerializeField, Min(1)] private int maxHealth = 3;

    private int hitsTaken;

    public bool isUsed;

    public int MaxHealth => maxHealth;
    public int Health => Mathf.Max(0, maxHealth - hitsTaken);

    // Kesilen parçanın devrileceği yatay yön (baltadan uzağa). Tree, kesimden önce ayarlar.
    public Vector3 FallDirection { get; set; }

    // Kesimden çıkan odunun animasyona başlayacağı nokta
    public virtual Vector3 SpawnOrigin => animatedPart != null ? animatedPart.position : transform.position;

    // Bir vuruş alır. Can bittiyse true döner (ok kesilmeye hazır).
    public bool TakeHit()
    {
        if (isUsed) return false;
        hitsTaken++;
        return Health == 0;
    }
    public Lenghts GetLength()
    {
        return length;
    }

    // Bu okun temsil ettiği odun parçasının uzunluğu (WoodCatalog'da aranır)
    public int WoodLength => length switch
    {
        Lenghts.BR_1 => 1,
        Lenghts.BR_2 => 2,
        Lenghts.BR_3 => 3,
        _ => 1
    };
    public virtual Transform VisualPart()
    {
        return animatedPart;
    }
       public Transform GetTransform()
    {
        return selectedActiveTransform;
    }
    // Oku ve ondan koparılmış parçayı (ArrowChild animatedPart'ı ayırıyor) birlikte yok eder
    public void DestroyWithParts()
    {
        if (animatedPart != null && !animatedPart.IsChildOf(transform))
            Destroy(animatedPart.gameObject);
        Destroy(gameObject);
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

// Değerler sabit: Unity enum'ı sayı olarak kaydediyor, BR_3 = 3 kalmazsa kayıtlı oklar kayar
public enum Lenghts
{
    BR_1 = 0,
    BR_2 = 1,
    BR_3 = 3
}