using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static event Action<int, bool> OnNumbers;
    public static event Action<bool> OnMouseLeft;
    public static event Action<bool> OnMouseRight;
    public static event Action OnF5;
    public static event Action OnR;
    public static event Action OnE;
    public static event Action OnQ;
    public static event Action OnTab;
    public static event Action OnSpace;

    private static InputActions baseInput;

    // Oyuncu girişi kilitli (örn. kedi eldiveni yakaladı, eldiven botta): fare tıkları, sayılar, R, Tab, Space iletilmez.
    // Kamera (Q/E, WASD, tekerlek) ve F5 serbest. Her kilitleyen kendini sahip olarak verir; hepsi bırakınca açılır.
    private static readonly HashSet<object> lockOwners = new HashSet<object>();
    public static bool Locked => lockOwners.Count > 0;

    public static void SetLocked(object owner, bool locked)
    {
        bool wasLocked = Locked;
        if (locked) lockOwners.Add(owner);
        else lockOwners.Remove(owner);
        if (wasLocked || !Locked) return;

        // Basılı tuşlar bırakılmış sayılır (kilitliyken bırakma olayı iletilmez): tutulan obje bırakılır, silme durur
        OnMouseLeft?.Invoke(false);
        OnMouseRight?.Invoke(false);
    }

    private void Awake()
    {
        lockOwners.Clear(); // domain reload kapalıyken önceki oturumdan kalmasın
        baseInput = new InputActions();
        baseInput.Enable();
    }

    public static Vector2 MovementVectorNormalized()
    {
        Vector2 movement = baseInput.Player.WASD.ReadValue<Vector2>();
        return movement;
    }

    public static float ScrollDelta()
    {
        if (Mouse.current == null) return 0f;
        return Mouse.current.scroll.ReadValue().y / 120f;
    }

    private int GetNumberFromControl(InputAction.CallbackContext context)
    {
        switch (context.control.name)
        {
            case "1": return 1;
            case "2": return 2;
            case "3": return 3;
            case "4": return 4;
            case "5": return 5;
            case "6": return 6;
            case "7": return 7;
            case "8": return 8;
            case "9": return 9;
            default: return 0;
        }
    }

    private void NumbersPerformed(InputAction.CallbackContext context)
    {
        if (Locked) return;
        OnNumbers?.Invoke(GetNumberFromControl(context), true);
    }

    private void NumbersCanceled(InputAction.CallbackContext context)
    {
        if (Locked) return;
        OnNumbers?.Invoke(GetNumberFromControl(context), false);
    }
    private void MouseLeft(InputAction.CallbackContext context) { if (!Locked) OnMouseLeft?.Invoke(context.performed); }
    private void MouseRight(InputAction.CallbackContext context) { if (!Locked) OnMouseRight?.Invoke(context.performed); }
    private void E(InputAction.CallbackContext context) => OnE?.Invoke();
    private void Q(InputAction.CallbackContext context) => OnQ?.Invoke();
    private void R(InputAction.CallbackContext context) { if (!Locked) OnR?.Invoke(); }
    private void F5(InputAction.CallbackContext context) => OnF5?.Invoke();
    private void TabKey(InputAction.CallbackContext context) { if (!Locked) OnTab?.Invoke(); }
    private void SpaceKey(InputAction.CallbackContext context) { if (!Locked) OnSpace?.Invoke(); }

    void OnEnable()
    {
        baseInput.Player.Numbers.performed += NumbersPerformed;
        baseInput.Player.Numbers.canceled += NumbersCanceled;
        baseInput.Player.F5.performed += F5;
        baseInput.Player.R.performed += R;
        baseInput.Player.E.performed += E;
        baseInput.Player.Q.performed += Q;
        baseInput.Player.Tab.performed += TabKey;
        baseInput.Player.Space.performed += SpaceKey;
        baseInput.Player.MouseLeft.performed += MouseLeft;
        baseInput.Player.MouseLeft.canceled += MouseLeft;
        baseInput.Player.MouseRight.performed += MouseRight;
        baseInput.Player.MouseRight.canceled += MouseRight;
    }



    void OnDisable()
    {
        baseInput.Player.Numbers.performed -= NumbersPerformed;
        baseInput.Player.Numbers.canceled -= NumbersCanceled;
        baseInput.Player.F5.performed -= F5;
        baseInput.Player.R.performed -= R;
        baseInput.Player.E.performed -= E;
        baseInput.Player.Q.performed -= Q;
        baseInput.Player.Tab.performed -= TabKey;
        baseInput.Player.Space.performed -= SpaceKey;
        baseInput.Player.MouseLeft.performed -= MouseLeft;
        baseInput.Player.MouseLeft.canceled -= MouseLeft;
        baseInput.Player.MouseRight.performed -= MouseRight;
        baseInput.Player.MouseRight.canceled -= MouseRight;
    }
}