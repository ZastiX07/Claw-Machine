using UnityEngine;

[DisallowMultipleComponent]
public sealed class ToyInstanceMetadata : MonoBehaviour
{
    [SerializeField] private string _toyId;

    public string ToyId => string.IsNullOrWhiteSpace(_toyId) ? string.Empty : _toyId.Trim();

    public void SetToyId(string toyId)
    {
        _toyId = string.IsNullOrWhiteSpace(toyId) ? string.Empty : toyId.Trim();
    }
}
