using UnityEngine;

[CreateAssetMenu(fileName = "ClawBackendSettings", menuName = "Claw Machine/Backend Settings")]
public sealed class ClawBackendSettingsAsset : ScriptableObject
{
    [SerializeField] private string _baseUrl;
    [SerializeField] private string _telegramAuthPath = "/v1/auth/telegram";
    [SerializeField] private string _devAuthPath = "/v1/auth/dev";
    [SerializeField] private string _accessTokenPlayerPrefsKey = "backend_access_token";
    [SerializeField, Min(1)] private int _requestTimeoutSeconds = 10;
    [SerializeField, Min(0)] private int _minTokenRemainingSeconds = 15;

    public string BaseUrl => _baseUrl ?? string.Empty;
    public string TelegramAuthPath => _telegramAuthPath ?? string.Empty;
    public string DevAuthPath => _devAuthPath ?? string.Empty;
    public string AccessTokenPlayerPrefsKey => _accessTokenPlayerPrefsKey ?? string.Empty;
    public int RequestTimeoutSeconds => Mathf.Max(1, _requestTimeoutSeconds);
    public int MinTokenRemainingSeconds => Mathf.Max(0, _minTokenRemainingSeconds);
}
