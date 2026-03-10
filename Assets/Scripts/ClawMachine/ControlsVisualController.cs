using System.Collections;
using UnityEngine;

public class ControlsVisualController : MonoBehaviour
{
    [SerializeField] private InputController _input;
    [SerializeField] private Transform _stickObject;
    [SerializeField] private float _maxTiltEulerAngle = 15f;
    [SerializeField] private Transform _buttonObject;
    [SerializeField] private float _buttonPressOffset = 0.01f;
    [SerializeField] private float _buttonPressDuration = 0.06f;

    private Quaternion _neutralStickLocalRotation;
    private Vector3 _neutralButtonLocalPosition;
    private Coroutine _buttonPressRoutine;

    void Awake()
    {
        if (_stickObject != null)
            _neutralStickLocalRotation = _stickObject.localRotation;

        if (_buttonObject != null)
            _neutralButtonLocalPosition = _buttonObject.localPosition;

        if (_input == null)
            return;

        _input.MovementChanged += OnMovementChanged;
        _input.ButtonPressed += OnButtonPressed;
    }

    void OnDestroy()
    {
        if (_input == null)
            return;

        _input.MovementChanged -= OnMovementChanged;
        _input.ButtonPressed -= OnButtonPressed;
    }

    private void OnButtonPressed()
    {
        if (_buttonObject == null)
            return;

        if (_buttonPressRoutine != null)
            StopCoroutine(_buttonPressRoutine);

        _buttonPressRoutine = StartCoroutine(AnimateButtonPress());
    }

    private IEnumerator AnimateButtonPress()
    {
        var pressedLocalPosition = _neutralButtonLocalPosition + Vector3.down * _buttonPressOffset;

        if (_buttonPressDuration <= 0f)
        {
            _buttonObject.localPosition = pressedLocalPosition;
            _buttonObject.localPosition = _neutralButtonLocalPosition;
            _buttonPressRoutine = null;
            yield break;
        }

        yield return AnimateButtonLocalPosition(_neutralButtonLocalPosition, pressedLocalPosition, _buttonPressDuration);
        yield return AnimateButtonLocalPosition(pressedLocalPosition, _neutralButtonLocalPosition, _buttonPressDuration);
        _buttonPressRoutine = null;
    }

    private IEnumerator AnimateButtonLocalPosition(Vector3 from, Vector3 to, float duration)
    {
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(elapsed / duration);
            _buttonObject.localPosition = Vector3.Lerp(from, to, progress);
            yield return null;
        }

        _buttonObject.localPosition = to;
    }

    private void OnMovementChanged(Vector2 move)
    {
        if (_stickObject == null)
            return;

        var clampedMove = Vector2.ClampMagnitude(move, 1f);
        var tilt = Quaternion.Euler(-clampedMove.y * _maxTiltEulerAngle, 0f, clampedMove.x * _maxTiltEulerAngle);
        _stickObject.localRotation = _neutralStickLocalRotation * tilt;
    }
}
