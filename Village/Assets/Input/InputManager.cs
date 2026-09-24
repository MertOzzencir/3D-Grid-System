using System;
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
    public static event Action OnTab;
    public static event Action OnSpace;

    private static InputActions baseInput;

    private void Awake()
    {
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
        OnNumbers?.Invoke(GetNumberFromControl(context), true);
    }

    private void NumbersCanceled(InputAction.CallbackContext context)
    {
        OnNumbers?.Invoke(GetNumberFromControl(context), false);
    }
    private void MouseLeft(InputAction.CallbackContext context) => OnMouseLeft?.Invoke(context.performed ? true : false);
    private void MouseRight(InputAction.CallbackContext context) => OnMouseRight?.Invoke(context.performed ? true : false);
    private void E(InputAction.CallbackContext context) => OnE?.Invoke();
    private void R(InputAction.CallbackContext context) => OnR?.Invoke();
    private void F5(InputAction.CallbackContext context) => OnF5?.Invoke();
    private void TabKey(InputAction.CallbackContext context) => OnTab?.Invoke();
    private void SpaceKey(InputAction.CallbackContext context) => OnSpace?.Invoke();

    void OnEnable()
    {
        baseInput.Player.Numbers.performed += NumbersPerformed;
        baseInput.Player.Numbers.canceled += NumbersCanceled;
        baseInput.Player.F5.performed += F5;
        baseInput.Player.R.performed += R;
        baseInput.Player.E.performed += E;
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
        baseInput.Player.Tab.performed -= TabKey;
        baseInput.Player.Space.performed -= SpaceKey;
        baseInput.Player.MouseLeft.performed -= MouseLeft;
        baseInput.Player.MouseLeft.canceled -= MouseLeft;
        baseInput.Player.MouseRight.performed -= MouseRight;
        baseInput.Player.MouseRight.canceled -= MouseRight;
    }
}