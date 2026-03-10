using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class ToyMachineFiller : MonoBehaviour
{
#if UNITY_EDITOR
    private const string DefaultGiftsFolder = "Assets/Models/Gifts";
#endif

    [Serializable]
    public sealed class ToyEntry
    {
        public string Name;
        public GameObject Prefab;

        [Min(0f)]
        public float SpawnWeight = 1f;

        [Range(0f, 1f)]
        public float Rarity = 0f;

        public Vector2 ScaleRange = new Vector2(1f, 1f);
    }

    [Header("Spawn Area")]
    [SerializeField] private Vector3 _spawnAreaCenter = new Vector3(0f, 0.32f, 0f);
    [SerializeField] private Vector3 _spawnAreaSize = new Vector3(0.48f, 0.42f, 0.48f);

    [Header("Spawn Setup")]
    [SerializeField, Min(1)] private int _toyCount = 35;
    [SerializeField] private Transform _spawnParent;
    [SerializeField] private string _generatedRootName = "Generated Toys";
    [SerializeField] private bool _refillOnStart = true;
    [SerializeField] private Vector2 _yawRange = new Vector2(0f, 360f);
    [SerializeField] private bool _randomTilt = true;
    [SerializeField] private float _maxTiltAngle = 20f;
    [SerializeField] private bool _useFixedSeed = true;
    [SerializeField] private int _seed = 12345;

    [Header("Physics Defaults")]
    [SerializeField] private bool _ensureRigidbody = true;
    [SerializeField] private bool _ensureCollider = true;
    [SerializeField] private float _rigidbodyMass = 1f;
    [SerializeField] private RigidbodyInterpolation _rigidbodyInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private CollisionDetectionMode _collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

    [Header("Toys")]
    [SerializeField] private List<ToyEntry> _toys = new List<ToyEntry>();

    public IReadOnlyList<ToyEntry> Toys => _toys;

#if UNITY_EDITOR
    private void Reset()
    {
        AutoPopulateFromGiftsFolderIfEmpty();
    }

    private void OnValidate()
    {
        _spawnAreaSize.x = Mathf.Max(0.01f, _spawnAreaSize.x);
        _spawnAreaSize.y = Mathf.Max(0.01f, _spawnAreaSize.y);
        _spawnAreaSize.z = Mathf.Max(0.01f, _spawnAreaSize.z);
        AutoPopulateFromGiftsFolderIfEmpty();
    }

    public void AutoPopulateFromGiftsFolderIfEmpty()
    {
        if (_toys.Count > 0)
            return;

        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { DefaultGiftsFolder });
        if (modelGuids.Length == 0)
            return;

        Array.Sort(modelGuids, StringComparer.Ordinal);

        for (var i = 0; i < modelGuids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            var prefab = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            if (prefab == null)
                continue;

            var rarity = modelGuids.Length <= 1 ? 0f : (float)i / (modelGuids.Length - 1);
            _toys.Add(new ToyEntry
            {
                Name = prefab.name,
                Prefab = prefab,
                SpawnWeight = 1f,
                Rarity = rarity,
                ScaleRange = Vector2.one
            });
        }
    }
#endif

    private void Start()
    {
        if (!_refillOnStart)
            return;

        Refill();
    }

    [ContextMenu("Refill Toys")]
    public void Refill()
    {
        ClearGenerated();
        GenerateToys();
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
        var valid = 0;

        for (var i = 0; i < _toys.Count; i++)
        {
            var entry = _toys[i];
            if (entry == null || entry.Prefab == null)
                continue;

            if (entry.SpawnWeight <= 0f)
                continue;

            valid++;
        }

        return valid;
    }

    public void GenerateToys()
    {
        var validEntries = GetValidEntries();
        if (validEntries.Count == 0)
        {
            Debug.LogWarning($"{nameof(ToyMachineFiller)} on '{name}' has no valid toy entries.", this);
            return;
        }

        var parent = ResolveSpawnParent();
        var generatedRoot = CreateGeneratedRoot(parent);
        var random = CreateRandom();
        var planned = CreatePlannedSet(validEntries, _toyCount, random);

        // Rare items are spawned first and therefore assigned to the lowest height layers.
        planned.Sort((a, b) => b.Rarity.CompareTo(a.Rarity));

        for (var i = 0; i < planned.Count; i++)
        {
            SpawnOne(planned[i], i, planned.Count, generatedRoot, random);
        }
    }

    private List<ToyEntry> GetValidEntries()
    {
        var result = new List<ToyEntry>();

        for (var i = 0; i < _toys.Count; i++)
        {
            var entry = _toys[i];
            if (entry == null || entry.Prefab == null)
                continue;

            if (entry.SpawnWeight <= 0f)
                continue;

            result.Add(entry);
        }

        return result;
    }

    private void SpawnOne(ToyEntry entry, int index, int totalCount, Transform root, System.Random random)
    {
        var prefab = entry.Prefab;
        if (prefab == null)
            return;

        var localPosition = GetLocalSpawnPosition(index, totalCount, random);
        var rotation = GetSpawnRotation(random);
        var instance = Instantiate(prefab, root);
        instance.transform.SetLocalPositionAndRotation(localPosition, rotation);

        var scaleMultiplier = NextRange(random, entry.ScaleRange.x, entry.ScaleRange.y);
        instance.transform.localScale *= Mathf.Max(0.0001f, scaleMultiplier);

        EnsurePhysics(instance);
    }

    private Vector3 GetLocalSpawnPosition(int index, int totalCount, System.Random random)
    {
        var clampedCount = Mathf.Max(1, totalCount);
        var segmentMin = (float)index / clampedCount;
        var segmentMax = (float)(index + 1) / clampedCount;
        var y01 = Mathf.Lerp(segmentMin, segmentMax, (float)random.NextDouble());

        var half = _spawnAreaSize * 0.5f;
        var x = NextRange(random, -half.x, half.x);
        var y = Mathf.Lerp(-half.y, half.y, y01);
        var z = NextRange(random, -half.z, half.z);

        return _spawnAreaCenter + new Vector3(x, y, z);
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

    private List<ToyEntry> CreatePlannedSet(List<ToyEntry> entries, int count, System.Random random)
    {
        var result = new List<ToyEntry>(Mathf.Max(0, count));
        for (var i = 0; i < count; i++)
            result.Add(PickWeighted(entries, random));

        return result;
    }

    private static ToyEntry PickWeighted(List<ToyEntry> entries, System.Random random)
    {
        var sum = 0f;
        for (var i = 0; i < entries.Count; i++)
            sum += Mathf.Max(0f, entries[i].SpawnWeight);

        if (sum <= Mathf.Epsilon)
            return entries[0];

        var pick = NextRange(random, 0f, sum);
        var acc = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            acc += Mathf.Max(0f, entries[i].SpawnWeight);
            if (pick <= acc)
                return entries[i];
        }

        return entries[entries.Count - 1];
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
    }

    private static float NextRange(System.Random random, float min, float max)
    {
        if (min > max)
            (min, max) = (max, min);

        if (Mathf.Approximately(min, max))
            return min;

        return min + (float)random.NextDouble() * (max - min);
    }
}
