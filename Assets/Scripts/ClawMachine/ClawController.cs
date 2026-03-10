using UnityEngine;

public class ClawController : MonoBehaviour
{
    private enum ClawState
    {
        Idle,
        Opening,
        Dropping,
        Closing,
        Rising,
        MovingToDropPoint,
        Releasing,
        WaitingAtDropPoint,
        ReturningToCenter
    }

    [Header("Input")]
    [SerializeField] private InputController _input;

    [Header("Rails")]
    [SerializeField] private MovementController _movement;

    [Header("Vertical Motion")]
    [SerializeField] private Transform _clawRoot;
    [SerializeField] private float _dropDistance = 0.6f;
    [SerializeField] private bool _useDropRaycast = true;
    [SerializeField] private Transform _dropRaycastOrigin;
    [SerializeField] private LayerMask _dropRaycastMask = ~0;
    [SerializeField, Min(0f)] private float _dropRaycastSurfaceOffset = 0.02f;
    [SerializeField] private bool _debugDrawDropRay = false;
    [SerializeField] private Color _debugDropRayNoHitColor = Color.red;
    [SerializeField] private Color _debugDropRayHitColor = Color.green;
    [SerializeField, Min(0f)] private float _debugDropRayDuration = 2f;
    [SerializeField] private float _dropSpeed = 0.8f;
    [SerializeField] private float _riseSpeed = 0.9f;

    [Header("Cable Scale")]
    [SerializeField] private Transform _cable;
    [SerializeField] private float _cableTopScaleY = 1f;
    [SerializeField] private float _cableBottomAtDrop = 0.8f;
    [SerializeField] private float _cableBottomScaleY = 15.4f;

    [Header("Fingers")]
    [SerializeField] private Transform[] _fingers;
    [SerializeField] private FingerContactSensor[] _fingerSensors;
    [SerializeField, Min(0f)] private float _openedFingerX = 25f;
    [SerializeField, Min(0f)] private float _fingersOpenSpeed = 520f;
    [SerializeField, Min(0f)] private float _fingersCloseSpeed = 260f;
    [SerializeField, Min(0f)] private float _fingerContactCloseThreshold = 0.35f;
    [SerializeField, Min(0f)] private float _minCloseBeforeBlocking = 4f;
    [SerializeField, Min(0f)] private float _maxCloseDuration = 0.9f;
    [SerializeField, Min(0f)] private float _dropPointPauseSeconds = 1f;

    [Header("Grab Hybrid")]
    [SerializeField] private Rigidbody _clawBody;
    [SerializeField, Min(1)] private int _requiredFingersForGrab = 3;
    [SerializeField, Min(0f)] private float _requiredGripHoldSeconds = 0.08f;
    [SerializeField] private bool _enableOneFingerLessLuckyGrab = true;
    [SerializeField, Range(0f, 1f)] private float _oneFingerLessLuckyGrabChance = 0.35f;
    [SerializeField] private LayerMask _grabbableLayers = ~0;
    [SerializeField] private bool _allowKinematicToyGrab = false;
    [SerializeField] private bool _lockGrabbedRotation = true;
    [SerializeField, Min(0f)] private float _jointBreakForce = 0f;
    [SerializeField, Min(0f)] private float _jointBreakTorque = 0f;
    [SerializeField] private bool _disableGrabbedGravity = true;
    [SerializeField] private bool _holdGrabbedAsKinematic = true;
    [SerializeField] private bool _stabilizeGrabFollow = true;
    [SerializeField, Min(0f)] private float _maxAllowedGrabLag = 0.006f;

    [Header("Weak Grip Slip")]
    [SerializeField] private bool _enableWeakGripSlip = true;
    [SerializeField, Min(1)] private int _weakGripMaxFingerContacts = 2;
    [SerializeField, Range(0f, 2f)] private float _weakGripEdgeOffsetNormalized = 0.6f;
    [SerializeField, Range(0f, 1f)] private float _weakGripDropRiseProgressMin = 0.3f;
    [SerializeField, Range(0f, 1f)] private float _weakGripDropRiseProgressMax = 0.6f;

    [Header("Swing (Code)")]
    [SerializeField] private Transform _swingVisual;
    [SerializeField, Min(0f)] private float _swingFromVelocity = 1.8f;
    [SerializeField, Min(0f)] private float _swingMaxAngle = 12f;
    [SerializeField, Min(0f)] private float _swingSpring = 18f;
    [SerializeField, Min(0f)] private float _swingDamping = 6f;

    private float _topLocalY;
    private float _dropTargetLocalY;

    private Vector2[] _fingerYZ;
    private float[] _closedFingerX;
    private float[] _currentFingerX;
    private float[] _targetFingerX;
    private float[] _closeStartFingerX;
    private bool[] _fingerBlocked;

    private float _closeTimer;
    private Rigidbody _gripCandidateBody;
    private Vector3 _gripCandidatePoint;
    private float _gripCandidateTimer;
    private Rigidbody _luckyGrabDecisionBody;
    private bool _hasLuckyGrabDecision;
    private bool _luckyGrabAccepted;

    private ConfigurableJoint _grabJoint;
    private Rigidbody _grabbedBody;
    private bool _grabbedBodyHadGravity;
    private bool _grabbedBodyWasKinematic;
    private Quaternion _grabbedRotationOffset;
    private Vector3 _grabbedLocalPositionToClawRoot;
    private Quaternion _grabbedLocalRotationToClawRoot;

    private Quaternion _swingNeutralLocalRotation;
    private Vector2 _swingAngles;
    private Vector2 _swingAngularVelocity;
    private Vector3 _lastClawWorldPosition;
    private bool _hasClawPosition;
    private bool _dropMoveStarted;
    private float _dropPointPauseTimer;
    private Vector2 _moveInput;
    private ClawState _state = ClawState.Idle;
    private bool _hasScheduledWeakGripSlip;
    private float _scheduledWeakGripSlipProgress;

    void Awake()
    {
        if (_movement == null)
            _movement = FindFirstObjectByType<MovementController>();

        if (_clawRoot == null)
            _clawRoot = transform;
        if (_dropRaycastOrigin == null)
            _dropRaycastOrigin = _clawRoot;
        if (_swingVisual == null)
            _swingVisual = _clawRoot;

        _topLocalY = _clawRoot.localPosition.y;
        _dropTargetLocalY = CalculateDropTargetLocalY(_topLocalY);
        if (_swingVisual != null)
            _swingNeutralLocalRotation = _swingVisual.localRotation;

        if (_clawRoot != null)
        {
            _lastClawWorldPosition = _clawRoot.position;
            _hasClawPosition = true;
        }

        EnsureClawBody();
        CacheFingerData();
        EnsureFingerSensors();
        ApplyAllFingerRotations();
        UpdateCableScale();

        if (_input != null)
        {
            _input.ButtonPressed += OnButtonPressed;
            _input.MovementChanged += OnMovementChanged;
        }
    }

    void OnDestroy()
    {
        ReleaseGrabbedBody();

        if (_input != null)
        {
            _input.SetControlLocked(false);
            _input.ButtonPressed -= OnButtonPressed;
            _input.MovementChanged -= OnMovementChanged;
        }
    }

    void Update()
    {
        var dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        UpdateState(dt);
        UpdateSwing(dt);
        UpdateCableScale();
        DrawDropRayDebug();
    }

    void FixedUpdate()
    {
        if (_holdGrabbedAsKinematic)
            return;

        if (!_stabilizeGrabFollow || _grabJoint == null || _grabbedBody == null || _clawBody == null)
            return;

        var clawAnchorWorld = _clawBody.transform.TransformPoint(_grabJoint.anchor);
        var bodyAnchorWorld = _grabbedBody.transform.TransformPoint(_grabJoint.connectedAnchor);
        var lag = clawAnchorWorld - bodyAnchorWorld;
        var lagDistance = lag.magnitude;
        if (lagDistance <= Mathf.Max(0f, _maxAllowedGrabLag))
            return;

        _grabbedBody.MovePosition(_grabbedBody.position + lag);
        if (_lockGrabbedRotation)
            _grabbedBody.MoveRotation(_clawBody.rotation * _grabbedRotationOffset);

        _grabbedBody.linearVelocity = Vector3.zero;
        _grabbedBody.angularVelocity = Vector3.zero;
    }

    void LateUpdate()
    {
        if (!_holdGrabbedAsKinematic || _grabbedBody == null || _clawRoot == null)
            return;

        var targetRotation = _lockGrabbedRotation
            ? _clawRoot.rotation * _grabbedLocalRotationToClawRoot
            : _grabbedBody.rotation;

        _grabbedBody.position = _clawRoot.TransformPoint(_grabbedLocalPositionToClawRoot);

        if (_lockGrabbedRotation)
            _grabbedBody.rotation = targetRotation;
    }

    public void OpenFingers()
    {
        SetAllFingerTargets(_openedFingerX);
        ClearFingerBlocks();
    }

    public void CloseFingers()
    {
        SetAllFingerTargets(0f);
    }

    public void SetFingerOpenX(float openX)
    {
        SetAllFingerTargets(Mathf.Max(0f, openX));
    }

    private void OnButtonPressed()
    {
        if (_state != ClawState.Idle)
            return;

        if (_input != null)
            _input.SetControlLocked(true);

        _topLocalY = _clawRoot.localPosition.y;
        _dropTargetLocalY = CalculateDropTargetLocalY(_topLocalY);
        _dropMoveStarted = false;
        _dropPointPauseTimer = 0f;
        ResetGripDetection();
        ResetLuckyGrabDecision();
        ClearWeakGripSlipSchedule();
        OpenFingers();
        _state = ClawState.Opening;
    }

    private void OnMovementChanged(Vector2 move)
    {
        _moveInput = Vector2.ClampMagnitude(move, 1f);
    }

    private void UpdateState(float dt)
    {
        if (_clawRoot == null)
            return;

        switch (_state)
        {
            case ClawState.Idle:
                CloseFingers();
                MoveFingersTowardsTargets(_fingersCloseSpeed, dt, false);
                break;

            case ClawState.Opening:
                if (MoveFingersTowardsTargets(_fingersOpenSpeed, dt, false))
                    _state = ClawState.Dropping;
                break;

            case ClawState.Dropping:
                MoveFingersTowardsTargets(_fingersOpenSpeed, dt, false);
                if (MoveClawYTowards(_dropTargetLocalY, _dropSpeed, dt))
                    BeginClosing();
                break;

            case ClawState.Closing:
                _closeTimer += dt;
                var closingSettled = MoveFingersTowardsTargets(_fingersCloseSpeed, dt, true);
                UpdateGripDetection(dt);

                var maxCloseDuration = Mathf.Max(0f, _maxCloseDuration);
                var hasBlockedFingers = HasAnyFingerBlocked();
                var canAdvanceBecauseSettled = closingSettled && !hasBlockedFingers;
                var canAdvanceBecauseGrabbed = _grabbedBody != null;
                var canAdvanceBecauseTimeout = _closeTimer >= maxCloseDuration;

                if (canAdvanceBecauseSettled || canAdvanceBecauseGrabbed || canAdvanceBecauseTimeout)
                    _state = ClawState.Rising;
                break;

            case ClawState.Rising:
                MoveFingersTowardsTargets(_fingersCloseSpeed, dt, true);
                var reachedTop = MoveClawYTowards(_topLocalY, _riseSpeed, dt);
                EvaluateWeakGripSlipDuringRise();
                if (!_dropMoveStarted && HasReachedHalfRise() && _movement != null)
                {
                    _movement.StartAutoMoveToDropPoint();
                    _dropMoveStarted = true;
                }

                if (reachedTop)
                {
                    if (_movement != null)
                    {
                        if (!_dropMoveStarted)
                        {
                            _movement.StartAutoMoveToDropPoint();
                            _dropMoveStarted = true;
                        }

                        if (_movement.IsAutoMoving)
                            _state = ClawState.MovingToDropPoint;
                        else
                            EnterDropRelease();
                    }
                    else
                    {
                        EnterDropRelease();
                    }
                }
                break;

            case ClawState.MovingToDropPoint:
                MoveFingersTowardsTargets(_fingersCloseSpeed, dt, true);
                if (_movement == null || !_movement.IsAutoMoving)
                    EnterDropRelease();
                break;

            case ClawState.Releasing:
                MoveFingersTowardsTargets(_fingersOpenSpeed, dt, false);
                _dropPointPauseTimer += dt;
                if (_dropPointPauseTimer >= Mathf.Max(0f, _dropPointPauseSeconds))
                    _state = ClawState.WaitingAtDropPoint;
                break;

            case ClawState.WaitingAtDropPoint:
                if (_movement != null)
                {
                    _movement.StartAutoMoveToCenter();
                    _state = ClawState.ReturningToCenter;
                }
                else
                {
                    CompleteCycle();
                }
                break;

            case ClawState.ReturningToCenter:
                MoveFingersTowardsTargets(_fingersOpenSpeed, dt, false);
                if (_movement == null || !_movement.IsAutoMoving)
                    CompleteCycle();
                break;
        }
    }

    private void BeginClosing()
    {
        CacheCloseStartFingerAngles();
        CloseFingers();
        ClearFingerBlocks();
        ResetGripDetection();
        ResetLuckyGrabDecision();
        ClearWeakGripSlipSchedule();
        _closeTimer = 0f;
        _state = ClawState.Closing;
    }

    private void CompleteCycle()
    {
        _state = ClawState.Idle;
        _dropMoveStarted = false;
        _dropPointPauseTimer = 0f;
        ResetGripDetection();
        ResetLuckyGrabDecision();
        ClearWeakGripSlipSchedule();

        if (_input != null)
            _input.SetControlLocked(false);
    }

    private void EnterDropRelease()
    {
        ReleaseGrabbedBody();
        OpenFingers();
        _dropPointPauseTimer = 0f;
        _state = ClawState.Releasing;
    }

    private void UpdateGripDetection(float dt)
    {
        if (_grabbedBody != null)
            return;

        if (!TryGetDominantContact(out var body, out var contactPoint, out var fingerCount))
        {
            ResetGripDetection();
            return;
        }

        if (!CanAttemptGrabWithFingerCount(body, fingerCount))
        {
            ResetGripDetection();
            return;
        }

        if (_gripCandidateBody != body)
        {
            _gripCandidateBody = body;
            _gripCandidatePoint = contactPoint;
            _gripCandidateTimer = 0f;
            return;
        }

        _gripCandidatePoint = Vector3.Lerp(_gripCandidatePoint, contactPoint, 0.5f);
        _gripCandidateTimer += dt;

        if (_gripCandidateTimer >= Mathf.Max(0f, _requiredGripHoldSeconds))
            TryAttachGrabbedBody(_gripCandidateBody, _gripCandidatePoint, fingerCount);
    }

    private bool TryGetDominantContact(out Rigidbody dominantBody, out Vector3 averagePoint, out int dominantCount)
    {
        dominantBody = null;
        averagePoint = Vector3.zero;
        dominantCount = 0;

        if (_fingerSensors == null || _fingerSensors.Length == 0)
            return false;

        for (var i = 0; i < _fingerSensors.Length; i++)
        {
            if (!TryGetFingerContact(i, out var body, out var point))
                continue;

            var count = 1;
            var pointSum = point;

            for (var j = i + 1; j < _fingerSensors.Length; j++)
            {
                if (!TryGetFingerContact(j, out var otherBody, out var otherPoint))
                    continue;

                if (otherBody != body)
                    continue;

                count++;
                pointSum += otherPoint;
            }

            if (count <= dominantCount)
                continue;

            dominantCount = count;
            dominantBody = body;
            averagePoint = pointSum / count;
        }

        return dominantBody != null;
    }

    private bool TryGetFingerContact(int index, out Rigidbody body, out Vector3 point)
    {
        body = null;
        point = Vector3.zero;

        if (_fingerSensors == null || index < 0 || index >= _fingerSensors.Length)
            return false;

        var sensor = _fingerSensors[index];
        if (sensor == null)
            return false;

        if (!sensor.TryGetPrimaryContact(out body, out point))
            return false;

        return IsBodyGrabbable(body);
    }

    private bool IsBodyGrabbable(Rigidbody body)
    {
        if (!IsBodyInClawInteractionMask(body))
            return false;
        if (!_allowKinematicToyGrab && body.isKinematic)
            return false;

        return true;
    }

    private bool IsBodyInClawInteractionMask(Rigidbody body)
    {
        if (body == null)
            return false;
        if (_clawBody != null && body == _clawBody)
            return false;
        if (_clawRoot != null && body.transform.IsChildOf(_clawRoot))
            return false;

        var layerMask = _grabbableLayers.value;
        var bodyLayerBit = 1 << body.gameObject.layer;
        return (layerMask & bodyLayerBit) != 0;
    }

    private bool TryAttachGrabbedBody(Rigidbody body, Vector3 worldPoint, int fingerCount)
    {
        if (!IsBodyGrabbable(body))
            return false;
        if (!_holdGrabbedAsKinematic && _clawBody == null)
            return false;
        if (_grabbedBody != null)
            return _grabbedBody == body;

        _grabbedBody = body;
        _grabbedBodyHadGravity = body.useGravity;
        _grabbedBodyWasKinematic = body.isKinematic;
        if (_clawRoot != null)
        {
            _grabbedLocalPositionToClawRoot = _clawRoot.InverseTransformPoint(body.position);
            _grabbedLocalRotationToClawRoot = Quaternion.Inverse(_clawRoot.rotation) * body.rotation;
        }

        if (!_holdGrabbedAsKinematic)
        {
            var joint = _clawBody.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = _clawBody.transform.InverseTransformPoint(worldPoint);
            joint.connectedAnchor = body.transform.InverseTransformPoint(worldPoint);
            joint.enableCollision = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.02f;
            joint.projectionAngle = 3f;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            var angularMotion = _lockGrabbedRotation
                ? ConfigurableJointMotion.Locked
                : ConfigurableJointMotion.Free;

            joint.angularXMotion = angularMotion;
            joint.angularYMotion = angularMotion;
            joint.angularZMotion = angularMotion;

            joint.breakForce = _jointBreakForce <= 0f ? Mathf.Infinity : _jointBreakForce;
            joint.breakTorque = _jointBreakTorque <= 0f ? Mathf.Infinity : _jointBreakTorque;

            _grabJoint = joint;
            _grabbedRotationOffset = Quaternion.Inverse(_clawBody.rotation) * body.rotation;
        }

        if (_disableGrabbedGravity)
            body.useGravity = false;
        if (_holdGrabbedAsKinematic)
            body.isKinematic = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        TryScheduleWeakGripSlip(body, worldPoint, fingerCount);
        ResetGripDetection();
        return true;
    }

    private void ReleaseGrabbedBody()
    {
        if (_grabJoint != null)
            Destroy(_grabJoint);

        if (_grabbedBody != null && _disableGrabbedGravity)
            _grabbedBody.useGravity = _grabbedBodyHadGravity;
        if (_grabbedBody != null && _holdGrabbedAsKinematic)
            _grabbedBody.isKinematic = _grabbedBodyWasKinematic;

        _grabJoint = null;
        _grabbedBody = null;
        _grabbedLocalPositionToClawRoot = Vector3.zero;
        _grabbedLocalRotationToClawRoot = Quaternion.identity;
        ResetLuckyGrabDecision();
        ClearWeakGripSlipSchedule();
        ResetGripDetection();
    }

    private void ResetGripDetection()
    {
        _gripCandidateBody = null;
        _gripCandidatePoint = Vector3.zero;
        _gripCandidateTimer = 0f;
    }

    private bool CanAttemptGrabWithFingerCount(Rigidbody body, int fingerCount)
    {
        var required = Mathf.Max(1, _requiredFingersForGrab);
        if (fingerCount >= required)
            return true;

        if (!_enableOneFingerLessLuckyGrab)
            return false;
        if (required <= 1)
            return false;
        if (fingerCount != required - 1)
            return false;

        return EvaluateOneFingerLessLuckyGrab(body);
    }

    private bool EvaluateOneFingerLessLuckyGrab(Rigidbody body)
    {
        if (body == null)
            return false;

        if (_hasLuckyGrabDecision && _luckyGrabDecisionBody == body)
            return _luckyGrabAccepted;

        _hasLuckyGrabDecision = true;
        _luckyGrabDecisionBody = body;
        _luckyGrabAccepted = Random.value <= Mathf.Clamp01(_oneFingerLessLuckyGrabChance);
        return _luckyGrabAccepted;
    }

    private void ResetLuckyGrabDecision()
    {
        _luckyGrabDecisionBody = null;
        _hasLuckyGrabDecision = false;
        _luckyGrabAccepted = false;
    }

    private void TryScheduleWeakGripSlip(Rigidbody body, Vector3 worldPoint, int fingerCount)
    {
        ClearWeakGripSlipSchedule();

        if (!_enableWeakGripSlip || body == null)
            return;
        if (fingerCount <= 0 || fingerCount > Mathf.Max(1, _weakGripMaxFingerContacts))
            return;
        if (!TryCalculateHorizontalOffsetNormalized(body, worldPoint, out var offsetNormalized))
            return;
        if (offsetNormalized < Mathf.Max(0f, _weakGripEdgeOffsetNormalized))
            return;

        var min = Mathf.Clamp01(_weakGripDropRiseProgressMin);
        var max = Mathf.Clamp01(_weakGripDropRiseProgressMax);
        if (max < min)
        {
            var temp = min;
            min = max;
            max = temp;
        }

        _hasScheduledWeakGripSlip = true;
        _scheduledWeakGripSlipProgress = Random.Range(min, max);
    }

    private void EvaluateWeakGripSlipDuringRise()
    {
        if (!_hasScheduledWeakGripSlip || _grabbedBody == null)
            return;

        if (GetRiseProgress01() < _scheduledWeakGripSlipProgress)
            return;

        ReleaseGrabbedBody();
    }

    private float GetRiseProgress01()
    {
        if (_clawRoot == null)
            return 0f;

        var riseDistance = Mathf.Abs(_topLocalY - _dropTargetLocalY);
        if (riseDistance <= Mathf.Epsilon)
            return 1f;

        var raisedSoFar = _clawRoot.localPosition.y - _dropTargetLocalY;
        return Mathf.Clamp01(raisedSoFar / riseDistance);
    }

    private bool TryCalculateHorizontalOffsetNormalized(Rigidbody body, Vector3 worldPoint, out float normalizedOffset)
    {
        normalizedOffset = 0f;
        if (body == null)
            return false;

        var center = body.worldCenterOfMass;
        var centerXZ = new Vector2(center.x, center.z);
        var pointXZ = new Vector2(worldPoint.x, worldPoint.z);
        var horizontalOffset = Vector2.Distance(centerXZ, pointXZ);

        var hasBounds = TryGetBodyBounds(body, out var bounds);
        var horizontalExtent = hasBounds
            ? Mathf.Max(bounds.extents.x, bounds.extents.z)
            : 0.05f;

        horizontalExtent = Mathf.Max(0.001f, horizontalExtent);
        normalizedOffset = horizontalOffset / horizontalExtent;
        return true;
    }

    private bool TryGetBodyBounds(Rigidbody body, out Bounds bounds)
    {
        bounds = new Bounds();
        if (body == null)
            return false;

        var colliders = body.GetComponentsInChildren<Collider>(includeInactive: false);
        var hasBounds = false;

        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private void ClearWeakGripSlipSchedule()
    {
        _hasScheduledWeakGripSlip = false;
        _scheduledWeakGripSlipProgress = 0f;
    }

    private bool HasReachedHalfRise()
    {
        var riseDistance = Mathf.Abs(_topLocalY - _dropTargetLocalY);
        if (riseDistance <= Mathf.Epsilon)
            return true;

        var raisedSoFar = _clawRoot.localPosition.y - _dropTargetLocalY;
        return raisedSoFar >= riseDistance * 0.5f;
    }

    private void UpdateSwing(float dt)
    {
        if (_clawRoot == null || _swingVisual == null)
            return;

        if (!_hasClawPosition)
        {
            _lastClawWorldPosition = _clawRoot.position;
            _hasClawPosition = true;
            return;
        }

        var worldDelta = _clawRoot.position - _lastClawWorldPosition;
        _lastClawWorldPosition = _clawRoot.position;

        var parent = _swingVisual.parent;
        var localDelta = parent != null ? parent.InverseTransformVector(worldDelta) : worldDelta;
        var localVelocity = localDelta / dt;
        var maxAngle = Mathf.Max(0f, _swingMaxAngle);

        var targetX = Mathf.Clamp(-localVelocity.z * Mathf.Max(0f, _swingFromVelocity), -maxAngle, maxAngle);
        var targetZ = Mathf.Clamp(localVelocity.x * Mathf.Max(0f, _swingFromVelocity), -maxAngle, maxAngle);

        targetX += -_moveInput.y * maxAngle * 0.25f;
        targetZ += _moveInput.x * maxAngle * 0.25f;
        targetX = Mathf.Clamp(targetX, -maxAngle, maxAngle);
        targetZ = Mathf.Clamp(targetZ, -maxAngle, maxAngle);

        var targetAngles = new Vector2(targetX, targetZ);
        var spring = Mathf.Max(0f, _swingSpring);
        var damping = Mathf.Max(0f, _swingDamping);
        var angularAcceleration = (targetAngles - _swingAngles) * spring - _swingAngularVelocity * damping;

        _swingAngularVelocity += angularAcceleration * dt;
        _swingAngles += _swingAngularVelocity * dt;
        _swingAngles.x = Mathf.Clamp(_swingAngles.x, -maxAngle, maxAngle);
        _swingAngles.y = Mathf.Clamp(_swingAngles.y, -maxAngle, maxAngle);

        _swingVisual.localRotation = _swingNeutralLocalRotation * Quaternion.Euler(_swingAngles.x, 0f, _swingAngles.y);
    }

    private void EnsureClawBody()
    {
        if (_holdGrabbedAsKinematic)
        {
            if (_clawRoot != null)
            {
                var anchor = _clawRoot.Find("__GrabJointAnchor");
                if (anchor != null)
                    Destroy(anchor.gameObject);
            }

            _clawBody = null;
            return;
        }

        if (_clawBody != null && _clawRoot != null && _clawBody.transform == _clawRoot)
            _clawBody = null;

        if (_clawBody == null && _clawRoot != null)
        {
            const string anchorName = "__GrabJointAnchor";
            var anchor = _clawRoot.Find(anchorName);
            if (anchor == null)
            {
                var anchorObject = new GameObject(anchorName);
                anchor = anchorObject.transform;
                anchor.SetParent(_clawRoot, false);
                anchor.localPosition = Vector3.zero;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
            }

            _clawBody = anchor.GetComponent<Rigidbody>();
            if (_clawBody == null)
                _clawBody = anchor.gameObject.AddComponent<Rigidbody>();
        }

        if (_clawBody == null)
            return;

        _clawBody.isKinematic = true;
        _clawBody.useGravity = false;
        _clawBody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void CacheFingerData()
    {
        if (_fingers == null)
        {
            _fingerYZ = new Vector2[0];
            _closedFingerX = new float[0];
            _currentFingerX = new float[0];
            _targetFingerX = new float[0];
            _closeStartFingerX = new float[0];
            _fingerBlocked = new bool[0];
            return;
        }

        var count = _fingers.Length;
        _fingerYZ = new Vector2[count];
        _closedFingerX = new float[count];
        _currentFingerX = new float[count];
        _targetFingerX = new float[count];
        _closeStartFingerX = new float[count];
        _fingerBlocked = new bool[count];

        for (var i = 0; i < count; i++)
        {
            var finger = _fingers[i];
            if (finger == null)
                continue;

            var euler = finger.localEulerAngles;
            _fingerYZ[i] = new Vector2(euler.y, euler.z);

            var closedX = NormalizeFingerX(euler.x);
            _closedFingerX[i] = closedX;
            _currentFingerX[i] = closedX;
            _targetFingerX[i] = closedX;
            _closeStartFingerX[i] = closedX;
            _fingerBlocked[i] = false;
        }
    }

    private void EnsureFingerSensors()
    {
        if (_fingers == null)
        {
            _fingerSensors = new FingerContactSensor[0];
            return;
        }

        if (_fingerSensors == null || _fingerSensors.Length != _fingers.Length)
            _fingerSensors = new FingerContactSensor[_fingers.Length];

        for (var i = 0; i < _fingers.Length; i++)
        {
            if (_fingerSensors[i] != null)
                continue;

            var finger = _fingers[i];
            if (finger == null)
                continue;

            _fingerSensors[i] = finger.GetComponent<FingerContactSensor>();
            if (_fingerSensors[i] == null)
                _fingerSensors[i] = finger.gameObject.AddComponent<FingerContactSensor>();
        }
    }

    private static float NormalizeFingerX(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return Mathf.Max(0f, angle);
    }

    private void ApplyAllFingerRotations()
    {
        if (_fingers == null)
            return;

        for (var i = 0; i < _fingers.Length; i++)
            ApplyFingerX(i, _currentFingerX[i]);
    }

    private void ApplyFingerX(int index, float x)
    {
        if (_fingers == null || index < 0 || index >= _fingers.Length)
            return;

        var finger = _fingers[index];
        if (finger == null)
            return;

        var euler = finger.localEulerAngles;
        euler.x = Mathf.Max(0f, x);
        euler.y = _fingerYZ[index].x;
        euler.z = _fingerYZ[index].y;
        finger.localEulerAngles = euler;
    }

    private void SetAllFingerTargets(float openOffset)
    {
        if (_targetFingerX == null || _closedFingerX == null)
            return;

        var clampedOffset = Mathf.Max(0f, openOffset);
        var count = Mathf.Min(_targetFingerX.Length, _closedFingerX.Length);
        for (var i = 0; i < count; i++)
            _targetFingerX[i] = _closedFingerX[i] + clampedOffset;
    }

    private void ClearFingerBlocks()
    {
        if (_fingerBlocked == null)
            return;

        for (var i = 0; i < _fingerBlocked.Length; i++)
            _fingerBlocked[i] = false;
    }

    private void CacheCloseStartFingerAngles()
    {
        if (_closeStartFingerX == null || _currentFingerX == null)
            return;

        var count = Mathf.Min(_closeStartFingerX.Length, _currentFingerX.Length);
        for (var i = 0; i < count; i++)
            _closeStartFingerX[i] = _currentFingerX[i];
    }

    private bool HasAnyFingerBlocked()
    {
        if (_fingerBlocked == null)
            return false;

        for (var i = 0; i < _fingerBlocked.Length; i++)
        {
            if (_fingerBlocked[i])
                return true;
        }

        return false;
    }

    private bool MoveFingersTowardsTargets(float speed, float dt, bool allowBlockingWhileClosing)
    {
        if (_fingers == null || _currentFingerX == null || _targetFingerX == null)
            return true;

        var allReached = true;
        var step = Mathf.Max(0f, speed) * dt;
        var contactThreshold = Mathf.Max(0f, _fingerContactCloseThreshold);

        for (var i = 0; i < _fingers.Length; i++)
        {
            if (_fingers[i] == null)
                continue;

            var isClosingDirection = _currentFingerX[i] > _targetFingerX[i] + 0.0001f;
            if (!allowBlockingWhileClosing || !isClosingDirection)
            {
                _fingerBlocked[i] = false;
            }
            else
            {
                var closedAmount = _closeStartFingerX != null && i < _closeStartFingerX.Length
                    ? Mathf.Max(0f, _closeStartFingerX[i] - _currentFingerX[i])
                    : 0f;
                var canBlockByProgress = closedAmount >= Mathf.Max(0f, _minCloseBeforeBlocking);
                var stillAwayFromClosed = _currentFingerX[i] > _closedFingerX[i] + contactThreshold;
                var touchingGrabbable = IsFingerTouchingGrabbable(i);
                _fingerBlocked[i] = canBlockByProgress && stillAwayFromClosed && touchingGrabbable;
            }

            if (_fingerBlocked[i])
                continue;

            _currentFingerX[i] = Mathf.MoveTowards(_currentFingerX[i], _targetFingerX[i], step);
            ApplyFingerX(i, _currentFingerX[i]);

            if (!Mathf.Approximately(_currentFingerX[i], _targetFingerX[i]))
                allReached = false;
        }

        return allReached;
    }

    private bool IsFingerTouchingGrabbable(int index)
    {
        if (_fingerSensors == null || index < 0 || index >= _fingerSensors.Length)
            return false;

        var sensor = _fingerSensors[index];
        if (sensor == null)
            return false;

        if (!sensor.TryGetPrimaryContact(out var body, out _))
            return false;

        return IsBodyInClawInteractionMask(body);
    }

    private bool MoveClawYTowards(float targetY, float speed, float dt)
    {
        var localPosition = _clawRoot.localPosition;
        localPosition.y = Mathf.MoveTowards(localPosition.y, targetY, Mathf.Max(0f, speed) * dt);
        _clawRoot.localPosition = localPosition;
        return Mathf.Approximately(localPosition.y, targetY);
    }

    private float CalculateDropTargetLocalY(float topLocalY)
    {
        var maxDropDistance = Mathf.Max(0f, _dropDistance);
        var fallbackTarget = topLocalY - maxDropDistance;
        if (!TryGetDropRayHit(out _, out _, out var targetWorld, out _))
            return fallbackTarget;

        var targetLocalY = _clawRoot.parent != null
            ? _clawRoot.parent.InverseTransformPoint(targetWorld).y
            : targetWorld.y;

        return Mathf.Min(topLocalY, targetLocalY);
    }

    private bool TryGetDropRayHit(out Vector3 origin, out Vector3 nearestPoint, out Vector3 targetWorld, out float maxDropDistance)
    {
        origin = Vector3.zero;
        nearestPoint = Vector3.zero;
        targetWorld = Vector3.zero;
        maxDropDistance = Mathf.Max(0f, _dropDistance);

        var rayOrigin = _dropRaycastOrigin != null ? _dropRaycastOrigin : _clawRoot;
        if (!_useDropRaycast || rayOrigin == null || _clawRoot == null || maxDropDistance <= Mathf.Epsilon)
            return false;

        origin = rayOrigin.position;
        var hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDropDistance,
            _dropRaycastMask,
            QueryTriggerInteraction.Ignore);

        var foundHit = false;
        var nearestDistance = float.MaxValue;

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(_clawRoot))
                continue;
            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestPoint = hit.point;
                foundHit = true;
            }
        }

        if (!foundHit)
            return false;

        targetWorld = new Vector3(origin.x, nearestPoint.y + Mathf.Max(0f, _dropRaycastSurfaceOffset), origin.z);
        return true;
    }

    private void DrawDropRayDebug()
    {
        if (!_debugDrawDropRay)
            return;

        var rayOrigin = _dropRaycastOrigin != null ? _dropRaycastOrigin : _clawRoot;
        if (rayOrigin == null)
            return;

        var maxDropDistance = Mathf.Max(0f, _dropDistance);
        var origin = rayOrigin.position;
        Debug.DrawRay(origin, Vector3.down * maxDropDistance, _debugDropRayNoHitColor, _debugDropRayDuration);

        if (!TryGetDropRayHit(out _, out var nearestPoint, out var targetWorld, out _))
            return;

        Debug.DrawLine(origin, nearestPoint, _debugDropRayHitColor, _debugDropRayDuration);
        Debug.DrawLine(nearestPoint, targetWorld, _debugDropRayHitColor, _debugDropRayDuration);
    }

    private void UpdateCableScale()
    {
        if (_cable == null || _clawRoot == null)
            return;

        var loweredDistance = Mathf.Max(0f, _topLocalY - _clawRoot.localPosition.y);
        var dropForMaxScale = Mathf.Max(0.0001f, _cableBottomAtDrop);
        var t = Mathf.Clamp01(loweredDistance / dropForMaxScale);
        var scale = _cable.localScale;
        scale.y = Mathf.Lerp(_cableTopScaleY, _cableBottomScaleY, t);
        _cable.localScale = scale;
    }
}
