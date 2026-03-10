using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InputController : MonoBehaviour
{
    [SerializeField] private Joystick _movementJoystick;
    [SerializeField] private Button _clawButton;
    [SerializeField, Range(0f, 1f)] private float _movementDeadzone = 0.08f;
    [SerializeField, Min(0f)] private float _movementChangeEpsilon = 0.01f;

    public event Action<Vector2> MovementChanged;
    public event Action ButtonPressed;

    public bool IsControlLocked => _isControlLocked;

    private Vector2 _lastMovement;
    private bool _isControlLocked;
    private InputAction _keyboardMoveAction;
    private InputAction _keyboardClawAction;

    void Awake()
    {
        if (_clawButton != null)
            _clawButton.onClick.AddListener(OnButtonPress);

        _keyboardMoveAction = new InputAction(name: "KeyboardMove", type: InputActionType.Value);
        _keyboardMoveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        _keyboardClawAction = new InputAction(name: "KeyboardClaw", type: InputActionType.Button, binding: "<Keyboard>/space");
        _keyboardClawAction.performed += OnKeyboardClawPerformed;
    }

    void Start()
    {
        MovementChanged?.Invoke(GetMovement());
    }

    void OnEnable()
    {
        _keyboardMoveAction?.Enable();
        _keyboardClawAction?.Enable();
    }

    void OnDisable()
    {
        _keyboardMoveAction?.Disable();
        _keyboardClawAction?.Disable();
    }

    private void OnButtonPress()
    {
        if (_isControlLocked)
            return;

        ButtonPressed?.Invoke();
    }

    private void OnKeyboardClawPerformed(InputAction.CallbackContext _)
    {
        OnButtonPress();
    }

    public void SetControlLocked(bool isLocked)
    {
        if (_isControlLocked == isLocked)
            return;

        _isControlLocked = isLocked;

        if (_clawButton != null)
            _clawButton.interactable = !_isControlLocked;

        var movement = _isControlLocked ? Vector2.zero : GetMovement();
        _lastMovement = movement;
        MovementChanged?.Invoke(movement);
    }

    void Update()
    {
        if (_isControlLocked)
            return;

        var movement = GetMovement();
        var epsilon = Mathf.Max(0f, _movementChangeEpsilon);
        if ((movement - _lastMovement).sqrMagnitude > epsilon * epsilon)
            UpdateMovement(movement);
    }

    void UpdateMovement(Vector2 movement)
    {
        _lastMovement = movement;
        MovementChanged?.Invoke(_lastMovement);
    }

    Vector2 GetMovement()
    {
        var joystickMove = _movementJoystick == null
            ? Vector2.zero
            : new Vector2(_movementJoystick.Horizontal, _movementJoystick.Vertical);

        var keyboardMove = _keyboardMoveAction != null
            ? _keyboardMoveAction.ReadValue<Vector2>()
            : Vector2.zero;

        var movement = Vector2.ClampMagnitude(joystickMove + keyboardMove, 1f);
        var deadzone = Mathf.Clamp01(_movementDeadzone);
        var magnitude = movement.magnitude;
        if (magnitude <= deadzone)
            return Vector2.zero;

        var normalizedMagnitude = (magnitude - deadzone) / Mathf.Max(0.0001f, 1f - deadzone);
        return movement.normalized * Mathf.Clamp01(normalizedMagnitude);
    }

    void OnDestroy()
    {
        if (_clawButton != null)
            _clawButton.onClick.RemoveListener(OnButtonPress);

        if (_keyboardClawAction != null)
            _keyboardClawAction.performed -= OnKeyboardClawPerformed;

        _keyboardMoveAction?.Dispose();
        _keyboardClawAction?.Dispose();
    }
}
