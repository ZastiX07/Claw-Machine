using System.Collections.Generic;
using UnityEngine;

public sealed class FingerContactSensor : MonoBehaviour
{
    private struct ContactState
    {
        public float LastSeenTime;
        public Vector3 LastContactPoint;
        public int SeenFrames;
    }

    [SerializeField, Min(0.01f)] private float _contactPersistSeconds = 0.12f;
    [SerializeField] private bool _useOverlapFallback = true;
    [SerializeField, Min(0f)] private float _overlapPadding = 0.002f;
    [SerializeField] private Collider[] _probeColliders;

    private readonly Dictionary<Rigidbody, ContactState> _contacts = new();
    private readonly List<Rigidbody> _staleBodies = new();
    private readonly Collider[] _overlapBuffer = new Collider[24];

    void Awake()
    {
        CacheProbeCollidersIfNeeded();
    }

    void FixedUpdate()
    {
        if (_useOverlapFallback)
            ProbeOverlaps();

        PruneStaleContacts();
    }

    public bool TryGetPrimaryContact(out Rigidbody body, out Vector3 worldPoint)
    {
        if (_useOverlapFallback)
            ProbeOverlaps();

        PruneStaleContacts();

        body = null;
        worldPoint = Vector3.zero;
        var bestScore = int.MinValue;

        foreach (var contact in _contacts)
        {
            if (contact.Key == null)
                continue;

            var score = contact.Value.SeenFrames;
            if (score <= bestScore)
                continue;

            bestScore = score;
            body = contact.Key;
            worldPoint = contact.Value.LastContactPoint;
        }

        return body != null;
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterCollider(other);
    }

    private void OnTriggerExit(Collider other)
    {
        RemoveCollider(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        RegisterCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RegisterCollision(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision == null)
            return;

        RemoveCollider(collision.collider);
    }

    private void RegisterCollider(Collider other)
    {
        if (other == null)
            return;

        var body = other.attachedRigidbody;
        if (body == null)
            return;

        var contactPoint = other.ClosestPoint(transform.position);
        RegisterBody(body, contactPoint);
    }

    private void RegisterCollision(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        var body = collision.rigidbody;
        if (body == null)
            body = collision.collider.attachedRigidbody;
        if (body == null)
            return;

        var contactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : collision.collider.ClosestPoint(transform.position);

        RegisterBody(body, contactPoint);
    }

    private void RegisterBody(Rigidbody body, Vector3 contactPoint)
    {
        if (body == null)
            return;

        var state = _contacts.TryGetValue(body, out var existing)
            ? existing
            : new ContactState();

        state.LastSeenTime = Time.time;
        state.LastContactPoint = contactPoint;
        state.SeenFrames = Mathf.Min(state.SeenFrames + 1, 100000);
        _contacts[body] = state;
    }

    private void ProbeOverlaps()
    {
        CacheProbeCollidersIfNeeded();
        if (_probeColliders == null || _probeColliders.Length == 0)
            return;

        for (var i = 0; i < _probeColliders.Length; i++)
        {
            var probe = _probeColliders[i];
            if (probe == null || !probe.enabled || !probe.gameObject.activeInHierarchy)
                continue;

            var bounds = probe.bounds;
            if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
                continue;

            var extents = bounds.extents + Vector3.one * Mathf.Max(0f, _overlapPadding);
            var hitCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                extents,
                _overlapBuffer,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Collide);

            for (var hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                var hit = _overlapBuffer[hitIndex];
                if (hit == null || hit == probe)
                    continue;
                if (hit.transform.IsChildOf(transform))
                    continue;

                var body = hit.attachedRigidbody;
                if (body == null)
                    continue;

                var contactPoint = hit.ClosestPoint(bounds.center);
                RegisterBody(body, contactPoint);
            }
        }
    }

    private void CacheProbeCollidersIfNeeded()
    {
        if (_probeColliders != null && _probeColliders.Length > 0)
            return;

        _probeColliders = GetComponentsInChildren<Collider>(includeInactive: false);
    }

    private void RemoveCollider(Collider other)
    {
        if (other == null)
            return;

        var body = other.attachedRigidbody;
        if (body == null)
            return;

        _contacts.Remove(body);
    }

    private void PruneStaleContacts()
    {
        if (_contacts.Count == 0)
            return;

        var staleBefore = Time.time - Mathf.Max(0.01f, _contactPersistSeconds);
        _staleBodies.Clear();

        foreach (var contact in _contacts)
        {
            var body = contact.Key;
            var state = contact.Value;
            if (body == null || state.LastSeenTime < staleBefore)
                _staleBodies.Add(body);
        }

        for (var i = 0; i < _staleBodies.Count; i++)
            _contacts.Remove(_staleBodies[i]);
    }
}
