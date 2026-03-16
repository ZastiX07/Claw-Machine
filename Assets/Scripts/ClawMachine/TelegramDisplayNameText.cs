using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TelegramDisplayNameText : MonoBehaviour
{
    [SerializeField] private TMP_Text _label;
    [SerializeField] private string _prefix = string.Empty;
    [SerializeField] private string _fallbackText = "Guest";
    [SerializeField] private bool _hideIfEmpty;

    private void Awake()
    {
        if (_label == null)
            _label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        TelegramMiniAppSession.SessionChanged += OnSessionChanged;
        Refresh();
    }

    private void OnDisable()
    {
        TelegramMiniAppSession.SessionChanged -= OnSessionChanged;
    }

    [ContextMenu("Refresh")]
    public void Refresh()
    {
        if (_label == null)
            return;

        var displayName = TelegramMiniAppSession.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            if (_hideIfEmpty)
            {
                _label.gameObject.SetActive(false);
                return;
            }

            _label.gameObject.SetActive(true);
            _label.text = _fallbackText ?? string.Empty;
            return;
        }

        _label.gameObject.SetActive(true);
        _label.text = string.IsNullOrWhiteSpace(_prefix) ? displayName : _prefix + displayName;
    }

    private void OnSessionChanged()
    {
        Refresh();
    }
}
