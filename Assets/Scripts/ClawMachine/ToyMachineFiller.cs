using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class ToyMachineFiller : MonoBehaviour
{
    private const int MaxSpawnAreaPickAttempts = 32;

    public enum SpawnQuarter
    {
        BottomLeft = 0,
        BottomRight = 1,
        TopLeft = 2,
        TopRight = 3
    }

    [Header("Catalog")]
    [SerializeField] private ToyCatalogAsset _toyCatalog;

    [Header("Spawn Area")]
    [SerializeField] private Vector3 _spawnAreaCenter = new Vector3(0f, 0.32f, 0f);
    [SerializeField] private Vector3 _spawnAreaSize = new Vector3(0.48f, 0.42f, 0.48f);
    [SerializeField] private bool _useLShapedArea = true;
    [SerializeField] private SpawnQuarter _excludedQuarter = SpawnQuarter.BottomLeft;

    [Header("Spawn Setup")]
    [SerializeField] private Transform _spawnParent;
    [SerializeField] private string _generatedRootName = "Generated Toys";
    [SerializeField] private bool _refillOnStart = true;
    [SerializeField] private Vector2 _yawRange = new Vector2(0f, 360f);
    [SerializeField] private bool _randomTilt = true;
    [SerializeField] private float _maxTiltAngle = 20f;
    [SerializeField] private bool _useFixedSeed = true;
    [SerializeField] private int _seed = 12345;

    [Header("Backend Spawn Plan")]
    [SerializeField] private ClawBackendApiClient _backendApiClient;
    [SerializeField] private string _machineId = "main";

    [Header("Physics Defaults")]
    [SerializeField] private bool _ensureRigidbody = true;
    [SerializeField] private bool _ensureCollider = true;
    [SerializeField] private float _rigidbodyMass = 1f;
    [SerializeField] private RigidbodyInterpolation _rigidbodyInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private CollisionDetectionMode _collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

    private CancellationTokenSource _destroyCancellationTokenSource;
    private bool _refillInProgress;

    private void Awake()
    {
        if (_toyCatalog == null)
            _toyCatalog = Resources.Load<ToyCatalogAsset>("ToyCatalog");

        _destroyCancellationTokenSource = new CancellationTokenSource();
        if (_backendApiClient == null)
            _backendApiClient = FindFirstObjectByType<ClawBackendApiClient>();
    }

    private async void Start()
    {
        if (!_refillOnStart)
            return;

        await RefillAsync();
    }

    private void OnDestroy()
    {
        if (_destroyCancellationTokenSource == null)
            return;

        _destroyCancellationTokenSource.Cancel();
        _destroyCancellationTokenSource.Dispose();
        _destroyCancellationTokenSource = null;
    }

    private void OnValidate()
    {
        _spawnAreaSize.x = Mathf.Max(0.01f, _spawnAreaSize.x);
        _spawnAreaSize.y = Mathf.Max(0.01f, _spawnAreaSize.y);
        _spawnAreaSize.z = Mathf.Max(0.01f, _spawnAreaSize.z);
    }

    [ContextMenu("Refill Toys")]
    public void Refill()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"{nameof(ToyMachineFiller)} refill is runtime-only and requires backend spawn plan.", this);
            return;
        }

        _ = RefillAsync();
    }

    public async Task RefillAsync()
    {
        if (_refillInProgress)
            return;

        _refillInProgress = true;
        try
        {
            ClearGenerated();
            var plan = await GetSpawnPlanAsync(DestroyToken);
            GenerateToys(plan);
        }
        catch (OperationCanceledException)
        {
            // Scene was destroyed while waiting for backend.
        }
        finally
        {
            _refillInProgress = false;
        }
    }

    [ContextMenu("Clear Generated Toys")]
    public void ClearGenerated()
    {
        var generatedRoot = FindGeneratedRoot();
        if (generatedRoot == null)
            return;

        if (Application.isPlaying)
            Destroy(generatedRoot.gameObject);
        else
            DestroyImmediate(generatedRoot.gameObject);
    }

    public int CountValidEntries()
    {
        return GetValidCatalogEntries().Count;
    }

    public void GenerateToys(IReadOnlyList<string> toyIds)
    {
        var validEntries = GetValidCatalogEntries();
        if (validEntries.Count == 0)
        {
            Debug.LogWarning($"{nameof(ToyMachineFiller)} on '{name}' has no valid catalog entries.", this);
            return;
        }

        if (toyIds == null || toyIds.Count == 0)
        {
            Debug.LogWarning($"{nameof(ToyMachineFiller)} on '{name}' got empty spawn plan.", this);
            return;
        }

        var parent = ResolveSpawnParent();
        var generatedRoot = CreateGeneratedRoot(parent);
        var random = CreateRandom();

        for (var i = 0; i < toyIds.Count; i++)
        {
            SpawnOne(toyIds[i], i, toyIds.Count, generatedRoot, random);
        }
    }

    private async Task<IReadOnlyList<string>> GetSpawnPlanAsync(
        CancellationToken cancellationToken)
    {
        if (_backendApiClient == null)
            _backendApiClient = FindFirstObjectByType<ClawBackendApiClient>();

        if (_backendApiClient == null || !_backendApiClient.IsConfigured)
        {
            Debug.LogWarning(
                $"{nameof(ToyMachineFiller)} backend spawn plan skipped: API client is missing or unconfigured.",
                this);
            return Array.Empty<string>();
        }

        var result = await _backendApiClient.GetMachineSpawnPlanAsync(_machineId, cancellationToken);
        if (!result.IsSuccess || result.Data == null || result.Data.items == null || result.Data.items.Length == 0)
        {
            var error = result == null ? "unknown error" : result.ErrorMessage;
            Debug.LogWarning(
                $"{nameof(ToyMachineFiller)} failed to get backend spawn plan: {error}",
                this);
            return Array.Empty<string>();
        }

        var toyIds = new List<string>(result.Data.items.Length);
        for (var i = 0; i < result.Data.items.Length; i++)
        {
            var toyId = result.Data.items[i] != null ? (result.Data.items[i].toyId ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(toyId))
                continue;

            toyIds.Add(toyId);
        }

        if (toyIds.Count == 0)
        {
            Debug.LogWarning(
                $"{nameof(ToyMachineFiller)} backend spawn plan had no valid toy ids.",
                this);
            return Array.Empty<string>();
        }

        return toyIds;
    }

    private List<ToyCatalogAsset.Entry> GetValidCatalogEntries()
    {
        var result = new List<ToyCatalogAsset.Entry>();
        if (_toyCatalog == null)
            return result;

        var entries = _toyCatalog.Entries;
        if (entries == null)
            return result;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null)
                continue;
            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Prefab == null)
                continue;

            result.Add(entry);
        }

        return result;
    }

    private void SpawnOne(
        string toyId,
        int index,
        int totalCount,
        Transform root,
        System.Random random)
    {
        if (!TryResolveCatalogEntry(toyId, out var entry))
            return;

        var localPosition = GetLocalSpawnPosition(index, totalCount, random);
        var rotation = GetSpawnRotation(random);
        var instance = Instantiate(entry.Prefab, root);
        instance.transform.SetLocalPositionAndRotation(localPosition, rotation);

        var scaleMultiplier = Mathf.Max(0.0001f, entry.Scale);
        instance.transform.localScale *= Mathf.Max(0.0001f, scaleMultiplier);

        EnsurePhysics(instance);
    }

    private bool TryResolveCatalogEntry(
        string toyId,
        out ToyCatalogAsset.Entry entry)
    {
        entry = null;

        if (_toyCatalog == null)
            return false;

        if (_toyCatalog.TryGetEntry(toyId, out entry) && entry.Prefab != null)
            return true;
        Debug.LogWarning(
            $"{nameof(ToyMachineFiller)} could not resolve toyId='{toyId}'.",
            this);
        return false;
    }

    private Vector3 GetLocalSpawnPosition(int index, int totalCount, System.Random random)
    {
        var clampedCount = Mathf.Max(1, totalCount);
        var segmentMin = (float)index / clampedCount;
        var segmentMax = (float)(index + 1) / clampedCount;
        var y01 = Mathf.Lerp(segmentMin, segmentMax, (float)random.NextDouble());

        var half = _spawnAreaSize * 0.5f;
        var x = 0f;
        var y = Mathf.Lerp(-half.y, half.y, y01);
        var z = 0f;
        PickSpawnXZ(random, half, out x, out z);

        return _spawnAreaCenter + new Vector3(x, y, z);
    }

    private void PickSpawnXZ(System.Random random, Vector3 half, out float x, out float z)
    {
        x = 0f;
        z = 0f;

        for (var attempt = 0; attempt < MaxSpawnAreaPickAttempts; attempt++)
        {
            var candidateX = NextRange(random, -half.x, half.x);
            var candidateZ = NextRange(random, -half.z, half.z);
            if (IsInsideExcludedQuarter(candidateX, candidateZ))
                continue;

            x = candidateX;
            z = candidateZ;
            return;
        }

        // Fallback in case of extreme edge settings.
        x = NextRange(random, 0f, half.x);
        z = NextRange(random, 0f, half.z);
    }

    private bool IsInsideExcludedQuarter(float x, float z)
    {
        if (!_useLShapedArea)
            return false;

        switch (_excludedQuarter)
        {
            case SpawnQuarter.BottomLeft:
                return x < 0f && z < 0f;
            case SpawnQuarter.BottomRight:
                return x >= 0f && z < 0f;
            case SpawnQuarter.TopLeft:
                return x < 0f && z >= 0f;
            case SpawnQuarter.TopRight:
                return x >= 0f && z >= 0f;
            default:
                return false;
        }
    }

    private Quaternion GetSpawnRotation(System.Random random)
    {
        var yaw = NextRange(random, _yawRange.x, _yawRange.y);
        if (!_randomTilt)
            return Quaternion.Euler(0f, yaw, 0f);

        var maxTilt = Mathf.Max(0f, _maxTiltAngle);
        var tiltX = NextRange(random, -maxTilt, maxTilt);
        var tiltZ = NextRange(random, -maxTilt, maxTilt);
        return Quaternion.Euler(tiltX, yaw, tiltZ);
    }

    private void EnsurePhysics(GameObject instance)
    {
        if (_ensureCollider && instance.GetComponentInChildren<Collider>() == null)
            TryAddFallbackCollider(instance);

        if (!_ensureRigidbody)
            return;

        var rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.AddComponent<Rigidbody>();

        rb.mass = Mathf.Max(0.0001f, _rigidbodyMass);
        rb.interpolation = _rigidbodyInterpolation;
        rb.collisionDetectionMode = _collisionDetectionMode;
    }

    private static void TryAddFallbackCollider(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var collider = instance.AddComponent<SphereCollider>();
        collider.center = instance.transform.InverseTransformPoint(bounds.center);

        var lossyScale = instance.transform.lossyScale;
        var maxScale = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z));
        collider.radius = maxScale <= Mathf.Epsilon
            ? bounds.extents.magnitude
            : bounds.extents.magnitude / maxScale;
    }

    private System.Random CreateRandom()
    {
        if (_useFixedSeed)
            return new System.Random(_seed);

        return new System.Random(Environment.TickCount ^ GetInstanceID());
    }

    private Transform ResolveSpawnParent()
    {
        return _spawnParent != null ? _spawnParent : transform;
    }

    private Transform FindGeneratedRoot()
    {
        var parent = ResolveSpawnParent();
        return parent.Find(_generatedRootName);
    }

    private Transform CreateGeneratedRoot(Transform parent)
    {
        var existing = FindGeneratedRoot();
        if (existing != null)
            return existing;

        var rootObject = new GameObject(_generatedRootName);
        var rootTransform = rootObject.transform;
        rootTransform.SetParent(parent, false);
        return rootTransform;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.15f, 0.9f, 1f, 0.35f);
        Gizmos.DrawCube(_spawnAreaCenter, _spawnAreaSize);

        Gizmos.color = new Color(0.15f, 0.9f, 1f, 1f);
        Gizmos.DrawWireCube(_spawnAreaCenter, _spawnAreaSize);

        if (!_useLShapedArea)
            return;

        var quarterSize = new Vector3(_spawnAreaSize.x * 0.5f, _spawnAreaSize.y, _spawnAreaSize.z * 0.5f);
        var quarterOffset = GetQuarterOffset(_excludedQuarter, _spawnAreaSize);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawCube(_spawnAreaCenter + quarterOffset, quarterSize);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
        Gizmos.DrawWireCube(_spawnAreaCenter + quarterOffset, quarterSize);
    }

    private static Vector3 GetQuarterOffset(SpawnQuarter quarter, Vector3 size)
    {
        var x = size.x * 0.25f;
        var z = size.z * 0.25f;

        switch (quarter)
        {
            case SpawnQuarter.BottomLeft:
                return new Vector3(-x, 0f, -z);
            case SpawnQuarter.BottomRight:
                return new Vector3(x, 0f, -z);
            case SpawnQuarter.TopLeft:
                return new Vector3(-x, 0f, z);
            case SpawnQuarter.TopRight:
                return new Vector3(x, 0f, z);
            default:
                return Vector3.zero;
        }
    }

    private static float NextRange(System.Random random, float min, float max)
    {
        if (min > max)
            (min, max) = (max, min);

        if (Mathf.Approximately(min, max))
            return min;

        return min + (float)random.NextDouble() * (max - min);
    }

    private CancellationToken DestroyToken =>
        _destroyCancellationTokenSource == null
            ? CancellationToken.None
            : _destroyCancellationTokenSource.Token;
}
