using UnityEngine;

public class MovementController : MonoBehaviour
{
    private enum AutoRailTarget
    {
        None,
        DropPoint,
        Center
    }

    [SerializeField] private InputController _input;
    [Header("Rails")]
    [SerializeField] private Transform _mainRail;
    [SerializeField] private Transform _childRail;

    [Header("Main Rail (local Z)")]
    [SerializeField] private Vector2 _mainRailZRange = new Vector2(-0.388f, 0.388f);
    [SerializeField] private bool _invertZ;

    [Header("Child Rail (local X)")]
    [SerializeField] private Vector2 _childRailXRange = new Vector2(-0.0799f, -0.7753f);
    [SerializeField] private bool _invertX;

    [Header("Motion Feel")]
    [SerializeField] private float _mainMaxSpeed = 0.2f;
    [SerializeField] private float _childMaxSpeed = 0.2f;
    [SerializeField] private float _acceleration = 0.7f;
    [SerializeField] private float _deceleration = 1f;
    [SerializeField] private float _stopSpeed = 0.005f;

    [Header("Auto Sequence")]
    [SerializeField] private float _dropPointMainRailZ = 0.3368343f;
    [SerializeField] private float _dropPointChildRailX = -0.1601201f;
    [SerializeField] private float _mainAutoSpeed = 0.18f;
    [SerializeField] private float _childAutoSpeed = 0.18f;
    [SerializeField] private float _autoAcceleration = 0.45f;
    [SerializeField] private float _autoDeceleration = 0.7f;
    [SerializeField] private float _autoStopSpeed = 0.01f;
    [SerializeField] private float _autoPositionResponsiveness = 4f;
    [SerializeField] private float _autoReachThreshold = 0.0001f;

    public bool IsAutoMoving => _autoRailTarget != AutoRailTarget.None;

    private float _mainVelocityZ;
    private float _childVelocityX;
    private Vector2 _moveInput;
    private float _centerMainRailZ;
    private float _centerChildRailX;
    private float _autoTargetMainRailZ;
    private float _autoTargetChildRailX;
    private AutoRailTarget _autoRailTarget;

    void Awake()
    {
        if (_input != null)
            _input.MovementChanged += OnMovementChanged;

        if (_mainRail != null)
            _centerMainRailZ = _mainRail.localPosition.z;

        if (_childRail != null)
            _centerChildRailX = _childRail.localPosition.x;
    }

    void Start()
    {
        OnMovementChanged(Vector2.zero);
    }

    void Update()
    {
        var dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        if (IsAutoMoving)
        {
            UpdateAutoMove(dt);
            return;
        }

        UpdateManualMove(dt);
    }

    public void StartAutoMoveToDropPoint()
    {
        StartAutoMove(_dropPointMainRailZ, _dropPointChildRailX, AutoRailTarget.DropPoint);
    }

    public void StartAutoMoveToCenter()
    {
        StartAutoMove(_centerMainRailZ, _centerChildRailX, AutoRailTarget.Center);
    }

    private void StartAutoMove(float mainRailZ, float childRailX, AutoRailTarget target)
    {
        _autoTargetMainRailZ = ClampToRange(mainRailZ, _mainRailZRange);
        _autoTargetChildRailX = ClampToRange(childRailX, _childRailXRange);
        _mainVelocityZ = 0f;
        _childVelocityX = 0f;
        _moveInput = Vector2.zero;
        _autoRailTarget = target;
    }

    private void UpdateAutoMove(float dt)
    {
        var mainReached = _mainRail == null;
        if (_mainRail != null)
        {
            var mainLocal = _mainRail.localPosition;
            mainReached = MoveAxisAutoWithInertia(ref mainLocal.z, _autoTargetMainRailZ, ref _mainVelocityZ, _mainAutoSpeed, dt);
            _mainRail.localPosition = mainLocal;
        }

        var childReached = _childRail == null;
        if (_childRail != null)
        {
            var childLocal = _childRail.localPosition;
            childReached = MoveAxisAutoWithInertia(ref childLocal.x, _autoTargetChildRailX, ref _childVelocityX, _childAutoSpeed, dt);
            _childRail.localPosition = childLocal;
        }

        if (mainReached && childReached)
            _autoRailTarget = AutoRailTarget.None;
    }

    private bool MoveAxisAutoWithInertia(ref float current, float target, ref float velocity, float maxSpeed, float dt)
    {
        var threshold = Mathf.Max(0f, _autoReachThreshold);
        var stopSpeed = Mathf.Max(0f, _autoStopSpeed);
        var distance = target - current;
        var absDistance = Mathf.Abs(distance);

        if (absDistance <= threshold && Mathf.Abs(velocity) <= stopSpeed)
        {
            current = target;
            velocity = 0f;
            return true;
        }

        var clampedMaxSpeed = Mathf.Max(0f, maxSpeed);
        var responsiveness = Mathf.Max(0f, _autoPositionResponsiveness);
        var desiredSpeed = Mathf.Clamp(distance * responsiveness, -clampedMaxSpeed, clampedMaxSpeed);
        var accelerating = Mathf.Abs(desiredSpeed) > Mathf.Abs(velocity);
        var rate = accelerating ? _autoAcceleration : _autoDeceleration;
        velocity = Mathf.MoveTowards(velocity, desiredSpeed, Mathf.Max(0f, rate) * dt);

        var previousDistance = target - current;
        current += velocity * dt;
        var currentDistance = target - current;

        // If axis crossed target during this step, snap to finish cleanly.
        if (Mathf.Sign(previousDistance) != Mathf.Sign(currentDistance))
        {
            current = target;
            velocity = 0f;
            return true;
        }

        if (Mathf.Abs(currentDistance) <= threshold && Mathf.Abs(velocity) <= stopSpeed)
        {
            current = target;
            velocity = 0f;
            return true;
        }

        return false;
    }

    private void UpdateManualMove(float dt)
    {
        if (_mainRail != null)
        {
            var mainLocal = _mainRail.localPosition;
            var mainInput = _invertZ ? -_moveInput.y : _moveInput.y;
            mainLocal.z = MoveAxisWithInertia(mainLocal.z, mainInput, ref _mainVelocityZ, _mainRailZRange, _mainMaxSpeed, dt);
            _mainRail.localPosition = mainLocal;
        }

        if (_childRail != null)
        {
            var childLocal = _childRail.localPosition;
            var childInput = _invertX ? -_moveInput.x : _moveInput.x;
            childLocal.x = MoveAxisWithInertia(childLocal.x, childInput, ref _childVelocityX, _childRailXRange, _childMaxSpeed, dt);
            _childRail.localPosition = childLocal;
        }
    }

    void OnDestroy()
    {
        if (_input == null)
            return;

        _input.MovementChanged -= OnMovementChanged;
    }

    private void OnMovementChanged(Vector2 move)
    {
        if (IsAutoMoving)
            return;

        _moveInput = Vector2.ClampMagnitude(move, 1f);
    }

    private float MoveAxisWithInertia(float current, float inputAxis, ref float velocity, Vector2 range, float maxSpeed, float dt)
    {
        var min = Mathf.Min(range.x, range.y);
        var max = Mathf.Max(range.x, range.y);
        var desiredSpeed = Mathf.Clamp(inputAxis, -1f, 1f) * Mathf.Max(0f, maxSpeed);
        var sameDirection = Mathf.Approximately(velocity, 0f) || Mathf.Sign(desiredSpeed) == Mathf.Sign(velocity);
        var accelerating = Mathf.Abs(desiredSpeed) > Mathf.Abs(velocity);
        var rate = sameDirection && accelerating ? _acceleration : _deceleration;
        velocity = Mathf.MoveTowards(velocity, desiredSpeed, Mathf.Max(0f, rate) * dt);

        current += velocity * dt;
        current = Mathf.Clamp(current, min, max);

        if (Mathf.Abs(desiredSpeed) <= Mathf.Epsilon && Mathf.Abs(velocity) <= Mathf.Max(0f, _stopSpeed))
            velocity = 0f;

        return current;
    }

    private static float ClampToRange(float value, Vector2 range)
    {
        var min = Mathf.Min(range.x, range.y);
        var max = Mathf.Max(range.x, range.y);
        return Mathf.Clamp(value, min, max);
    }
}
