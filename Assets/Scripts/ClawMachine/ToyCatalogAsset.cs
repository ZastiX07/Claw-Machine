using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ToyCatalog", menuName = "Claw Machine/Toy Catalog")]
public sealed class ToyCatalogAsset : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string Id;
        public GameObject Prefab;
        [Min(0.0001f)] public float Scale = 1f;
    }

    [SerializeField] private List<Entry> _entries = new List<Entry>();

    public IReadOnlyList<Entry> Entries => _entries;

    public bool TryGetEntry(string toyId, out Entry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(toyId))
            return false;

        var normalizedId = toyId.Trim();
        for (var i = 0; i < _entries.Count; i++)
        {
            var candidate = _entries[i];
            if (candidate == null)
                continue;
            if (!string.Equals(candidate.Id?.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase))
                continue;

            entry = candidate;
            return true;
        }

        return false;
    }
}
