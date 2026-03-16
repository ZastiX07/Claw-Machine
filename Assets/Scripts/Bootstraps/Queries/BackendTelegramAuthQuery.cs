using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class BackendTelegramAuthQuery : BootstrapLoadQuery
{
    private const string DefaultTelegramAuthPath = "/v1/auth/telegram";
    private const string DefaultDevAuthPath = "/v1/auth/dev";
    private const string DefaultAccessTokenPlayerPrefsKey = "backend_access_token";
    private const int DefaultRequestTimeoutSeconds = 10;
    private const int DefaultMinTokenRemainingSeconds = 15;

    [Serializable]
    private sealed class DirectAuthRequestDto
    {
        public string initData;
    }

    [Serializable]
    private sealed class DirectAuthResponseDto
    {
        public string accessToken = string.Empty;
        public int expiresInSec = 0;
    }

    [Serializable]
    private sealed class DirectDevAuthRequestDto
    {
        public string devUserId;
    }

    [Serializable]
    private sealed class DirectDevAuthResponseDto
    {
        public string accessToken = string.Empty;
        public int expiresInSec = 0;
    }

    [Header("References")]
    [SerializeField] private ClawBackendApiClient _apiClient;
    [SerializeField] private ClawBackendSettingsAsset _backendSettings;

    [Header("InitData Sources (priority top-down)")]
    [SerializeField] private string _explicitInitData;
    [SerializeField] private bool _readInitDataFromUrlQuery = true;
    [SerializeField] private bool _readInitDataFromUrlFragment = true;
    [SerializeField] private string _urlQueryParameterName = "tgWebAppData";
    [SerializeField] private bool _readInitDataFromPlayerPrefs = false;
    [SerializeField] private string _initDataPlayerPrefsKey = "telegram_init_data";
    [SerializeField, Range(1, 4)] private int _maxUrlDecodePasses = 2;

    [Header("Token Persistence")]
    [SerializeField] private bool _persistAccessTokenToPlayerPrefs = true;
    [SerializeField] private bool _skipIfAccessTokenAlreadyExists = true;

    [Header("Development Auth Fallback (No Telegram)")]
    [SerializeField] private bool _allowDevAuthFallback = true;
    [SerializeField] private bool _allowDevAuthFallbackInEditor = true;
    [SerializeField] private string _devUserId = "unity-editor";
    [SerializeField] private bool _appendPlatformToDevUserId = true;
    [SerializeField] private bool _preferDeviceUniqueIdentifier = true;

    [Header("Failure Policy")]
    [SerializeField] private bool _failIfBackendApiClientMissing = false;
    [SerializeField] private bool _failIfBackendNotConfigured = false;
    [SerializeField] private bool _failIfNoInitData = false;
    [SerializeField] private bool _failIfAuthRequestFails = true;
    [SerializeField] private bool _debugLogs = true;

    private string ConfiguredBackendBaseUrl => _backendSettings == null ? string.Empty : _backendSettings.BaseUrl;

    private string ConfiguredTelegramAuthPath
    {
        get
        {
            var configured = _backendSettings == null ? string.Empty : _backendSettings.TelegramAuthPath;
            return string.IsNullOrWhiteSpace(configured) ? DefaultTelegramAuthPath : configured;
        }
    }

    private string ConfiguredDevAuthPath
    {
        get
        {
            var configured = _backendSettings == null ? string.Empty : _backendSettings.DevAuthPath;
            return string.IsNullOrWhiteSpace(configured) ? DefaultDevAuthPath : configured;
        }
    }

    private string ConfiguredAccessTokenPlayerPrefsKey
    {
        get
        {
            var configured = _backendSettings == null ? string.Empty : _backendSettings.AccessTokenPlayerPrefsKey;
            return string.IsNullOrWhiteSpace(configured) ? DefaultAccessTokenPlayerPrefsKey : configured;
        }
    }

    private int ConfiguredRequestTimeoutSeconds =>
        _backendSettings == null ? DefaultRequestTimeoutSeconds : _backendSettings.RequestTimeoutSeconds;

    private int ConfiguredMinTokenRemainingSeconds =>
        _backendSettings == null ? DefaultMinTokenRemainingSeconds : _backendSettings.MinTokenRemainingSeconds;

    public override async Task ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        Log("Query started.");
        TelegramMiniAppSession.Clear();

        if (_apiClient == null)
            _apiClient = FindFirstObjectByType<ClawBackendApiClient>();

        var canUseApiClient = _apiClient != null && _apiClient.IsConfigured;
        Log($"ApiClient found: {_apiClient != null}, canUseApiClient: {canUseApiClient}.");
        if (_apiClient == null)
        {
            if (string.IsNullOrWhiteSpace(ConfiguredBackendBaseUrl))
                HandleFailure(context, _failIfBackendApiClientMissing, "Backend auth skipped: API client not found.");
            else
                context.ReportStatus("Backend auth: API client not found, using backend settings asset.");
        }

        if (!canUseApiClient && string.IsNullOrWhiteSpace(ConfiguredBackendBaseUrl))
        {
            HandleFailure(context, _failIfBackendNotConfigured, "Backend auth skipped: backend base URL is not configured.");
            return;
        }

        var hasInitData = TryResolveInitData(out var initData, out var initDataSource);
        if (hasInitData)
            TelegramMiniAppSession.SetFromInitData(initData, initDataSource);

        var hasAccessToken = HasAccessToken(canUseApiClient);

        if (_skipIfAccessTokenAlreadyExists && hasAccessToken && !hasInitData)
        {
            Log("Skipped because access token already exists.");
            context.ReportStatus("Backend auth: access token already present.");
            return;
        }

        var shouldUseDevFallback = !hasInitData && ShouldUseDevAuthFallback();
        if (!hasInitData && !shouldUseDevFallback)
        {
            HandleFailure(context, _failIfNoInitData, "Backend auth skipped: Telegram initData not found and no valid access token is available.");
            return;
        }
        AuthResult authResult;
        if (shouldUseDevFallback)
        {
            var resolvedDevUserId = ResolveDevUserId();
            Log($"Telegram initData missing. Using dev auth fallback with devUserId={resolvedDevUserId}.");
            context.ReportStatus("Backend auth: initData missing, using development auth fallback...");

            var devRequest = new ClawBackendApiClient.DevAuthRequestDto
            {
                devUserId = resolvedDevUserId
            };

            authResult = canUseApiClient
                ? await AuthenticateDevViaApiClientAsync(devRequest, cancellationToken)
                : await AuthenticateDevViaDirectRequestAsync(devRequest, cancellationToken);
        }
        else
        {
            Log($"initData source: {initDataSource}, length: {initData.Length}.");
            if (hasAccessToken && _skipIfAccessTokenAlreadyExists)
                Log("Access token exists, but fresh initData is available. Re-authenticating.");

            context.ReportStatus("Backend auth: authorizing Telegram session...");

            var requestBody = new ClawBackendApiClient.TelegramAuthRequestDto
            {
                initData = initData
            };

            authResult = canUseApiClient
                ? await AuthenticateViaApiClientAsync(requestBody, cancellationToken)
                : await AuthenticateViaDirectRequestAsync(requestBody, cancellationToken);
        }

        if (!authResult.IsSuccess || string.IsNullOrWhiteSpace(authResult.AccessToken))
        {
            var error = authResult.ErrorMessage;
            Log($"Auth failed: {error}");
            HandleFailure(context, _failIfAuthRequestFails, $"Backend auth failed: {error}");
            return;
        }

        if (canUseApiClient)
        {
            _apiClient.SetAccessToken(authResult.AccessToken, _persistAccessTokenToPlayerPrefs);
        }
        else if (_persistAccessTokenToPlayerPrefs && !string.IsNullOrWhiteSpace(ConfiguredAccessTokenPlayerPrefsKey))
        {
            PlayerPrefs.SetString(ConfiguredAccessTokenPlayerPrefsKey, authResult.AccessToken);
            PlayerPrefs.Save();
        }

        Log($"Auth succeeded. Token prefix: {MaskToken(authResult.AccessToken)}");
        context.ReportStatus("Backend auth completed.");
    }

    private bool HasAccessToken(bool canUseApiClient)
    {
        if (canUseApiClient)
            return _apiClient.HasAccessToken;

        var playerPrefsKey = ConfiguredAccessTokenPlayerPrefsKey;
        if (string.IsNullOrWhiteSpace(playerPrefsKey))
            return false;
        if (!PlayerPrefs.HasKey(playerPrefsKey))
            return false;

        var token = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
        var minRemaining = Mathf.Max(0, ConfiguredMinTokenRemainingSeconds);
        if (ClawBackendApiClient.IsAccessTokenUsable(token, minRemaining))
            return true;

        if (!string.IsNullOrWhiteSpace(token))
            Log("Stored backend access token is expired or invalid. Removing it.");

        PlayerPrefs.DeleteKey(playerPrefsKey);
        PlayerPrefs.Save();
        return false;
    }

    private async Task<AuthResult> AuthenticateViaApiClientAsync(
        ClawBackendApiClient.TelegramAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        Log("Authenticating via ClawBackendApiClient...");
        var result = await _apiClient.AuthenticateTelegramAsync(requestBody, cancellationToken);
        if (!result.IsSuccess || result.Data == null)
        {
            return new AuthResult
            {
                IsSuccess = false,
                AccessToken = string.Empty,
                ErrorMessage = result == null ? "Unknown error." : result.ErrorMessage
            };
        }

        return new AuthResult
        {
            IsSuccess = !string.IsNullOrWhiteSpace(result.Data.accessToken),
            AccessToken = result.Data.accessToken,
            ErrorMessage = string.IsNullOrWhiteSpace(result.Data.accessToken) ? "Empty access token in auth response." : string.Empty
        };
    }

    private async Task<AuthResult> AuthenticateDevViaApiClientAsync(
        ClawBackendApiClient.DevAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        Log("Authenticating via ClawBackendApiClient (dev fallback)...");
        var result = await _apiClient.AuthenticateDevAsync(requestBody, cancellationToken);
        if (!result.IsSuccess || result.Data == null)
        {
            return new AuthResult
            {
                IsSuccess = false,
                AccessToken = string.Empty,
                ErrorMessage = result == null ? "Unknown error." : result.ErrorMessage
            };
        }

        return new AuthResult
        {
            IsSuccess = !string.IsNullOrWhiteSpace(result.Data.accessToken),
            AccessToken = result.Data.accessToken,
            ErrorMessage = string.IsNullOrWhiteSpace(result.Data.accessToken) ? "Empty access token in dev auth response." : string.Empty
        };
    }

    private async Task<AuthResult> AuthenticateViaDirectRequestAsync(
        ClawBackendApiClient.TelegramAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        var baseUrl = ConfiguredBackendBaseUrl.TrimEnd('/');
        var authPath = ConfiguredTelegramAuthPath;
        var normalizedPath = authPath.StartsWith("/") ? authPath : "/" + authPath;
        var url = baseUrl + normalizedPath;
        Log($"Authenticating via direct request: {url}");

        var directRequest = new DirectAuthRequestDto
        {
            initData = requestBody.initData
        };

        var json = JsonUtility.ToJson(directRequest);
        var body = System.Text.Encoding.UTF8.GetBytes(json);

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, ConfiguredRequestTimeoutSeconds);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        using var registration = cancellationToken.Register(request.Abort);
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        var rawBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        var isSuccess = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
        if (!isSuccess)
        {
            var errorMessage = !string.IsNullOrWhiteSpace(request.error)
                ? request.error
                : rawBody;

            return new AuthResult
            {
                IsSuccess = false,
                AccessToken = string.Empty,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Direct auth request failed." : errorMessage
            };
        }
        Log($"Direct auth HTTP {request.responseCode}.");

        DirectAuthResponseDto response = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
            response = JsonUtility.FromJson<DirectAuthResponseDto>(rawBody);

        return new AuthResult
        {
            IsSuccess = response != null && !string.IsNullOrWhiteSpace(response.accessToken),
            AccessToken = response == null ? string.Empty : response.accessToken,
            ErrorMessage = response == null || string.IsNullOrWhiteSpace(response.accessToken)
                ? "Direct auth returned empty token."
                : string.Empty
        };
    }

    private async Task<AuthResult> AuthenticateDevViaDirectRequestAsync(
        ClawBackendApiClient.DevAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        var baseUrl = ConfiguredBackendBaseUrl.TrimEnd('/');
        var authPath = ConfiguredDevAuthPath;
        var normalizedPath = authPath.StartsWith("/") ? authPath : "/" + authPath;
        var url = baseUrl + normalizedPath;
        Log($"Authenticating via direct dev request: {url}");

        var directRequest = new DirectDevAuthRequestDto
        {
            devUserId = requestBody.devUserId
        };

        var json = JsonUtility.ToJson(directRequest);
        var body = System.Text.Encoding.UTF8.GetBytes(json);

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, ConfiguredRequestTimeoutSeconds);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        using var registration = cancellationToken.Register(request.Abort);
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        var rawBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        var isSuccess = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
        if (!isSuccess)
        {
            var errorMessage = !string.IsNullOrWhiteSpace(request.error)
                ? request.error
                : rawBody;

            return new AuthResult
            {
                IsSuccess = false,
                AccessToken = string.Empty,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Direct dev auth request failed." : errorMessage
            };
        }
        Log($"Direct dev auth HTTP {request.responseCode}.");

        DirectDevAuthResponseDto response = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
            response = JsonUtility.FromJson<DirectDevAuthResponseDto>(rawBody);

        return new AuthResult
        {
            IsSuccess = response != null && !string.IsNullOrWhiteSpace(response.accessToken),
            AccessToken = response == null ? string.Empty : response.accessToken,
            ErrorMessage = response == null || string.IsNullOrWhiteSpace(response.accessToken)
                ? "Direct dev auth returned empty token."
                : string.Empty
        };
    }

    private bool ShouldUseDevAuthFallback()
    {
        if (!_allowDevAuthFallback)
            return false;

        if (Application.isEditor && !_allowDevAuthFallbackInEditor)
            return false;

        return true;
    }

    private string ResolveDevUserId()
    {
        var candidate = (_devUserId ?? string.Empty).Trim();
        if (_preferDeviceUniqueIdentifier)
        {
            var deviceUniqueId = (SystemInfo.deviceUniqueIdentifier ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(deviceUniqueId) &&
                !string.Equals(deviceUniqueId, "Unsupported identifier", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(deviceUniqueId, "n/a", StringComparison.OrdinalIgnoreCase))
            {
                candidate = deviceUniqueId;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = "unity-editor";

        if (_appendPlatformToDevUserId)
            candidate = $"{candidate}-{Application.platform.ToString().ToLowerInvariant()}";

        return NormalizeDevUserId(candidate);
    }

    private static string NormalizeDevUserId(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return "unity-editor";

        var source = rawValue.Trim().ToLowerInvariant();
        var buffer = new char[source.Length];
        var index = 0;
        var prevWasDash = false;
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            var isAllowed = char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-';
            var normalized = isAllowed ? ch : '-';
            if (normalized == '-')
            {
                if (prevWasDash)
                    continue;
                prevWasDash = true;
            }
            else
            {
                prevWasDash = false;
            }

            buffer[index++] = normalized;
            if (index >= 64)
                break;
        }

        while (index > 0 && buffer[index - 1] == '-')
            index--;

        if (index == 0)
            return "unity-editor";

        return new string(buffer, 0, index);
    }

    private bool TryResolveInitData(out string initData, out string source)
    {
        source = string.Empty;
        if (!string.IsNullOrWhiteSpace(_explicitInitData))
        {
            initData = _explicitInitData.Trim();
            source = "explicit";
            return true;
        }

        if (TryGetInitDataFromUrl(out initData, out source))
        {
            return true;
        }

        if (_readInitDataFromPlayerPrefs && !string.IsNullOrWhiteSpace(_initDataPlayerPrefsKey) && PlayerPrefs.HasKey(_initDataPlayerPrefsKey))
        {
            var playerPrefsValue = PlayerPrefs.GetString(_initDataPlayerPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(playerPrefsValue))
            {
                initData = playerPrefsValue.Trim();
                source = "player_prefs";
                return true;
            }
        }

        initData = string.Empty;
        source = "missing";
        return false;
    }

    private bool TryGetInitDataFromUrl(out string value, out string source)
    {
        value = string.Empty;
        source = string.Empty;

        var url = Application.absoluteURL;
        var key = _urlQueryParameterName;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return false;

        if (_readInitDataFromUrlQuery && TryReadUrlSectionParameter(url, '?', key, out value))
        {
            source = "url_query";
            return true;
        }

        if (_readInitDataFromUrlFragment && TryReadUrlSectionParameter(url, '#', key, out value))
        {
            source = "url_fragment";
            return true;
        }

        return false;
    }

    private bool TryReadUrlSectionParameter(string url, char sectionToken, string key, out string value)
    {
        value = string.Empty;
        var sectionStart = url.IndexOf(sectionToken);
        if (sectionStart < 0 || sectionStart >= url.Length - 1)
            return false;

        var section = url.Substring(sectionStart + 1);
        if (sectionToken == '?')
        {
            var fragmentIndex = section.IndexOf('#');
            if (fragmentIndex >= 0)
                section = section.Substring(0, fragmentIndex);
        }
        else
        {
            var innerQueryIndex = section.IndexOf('?');
            if (innerQueryIndex >= 0 && innerQueryIndex < section.Length - 1)
                section = section.Substring(innerQueryIndex + 1);
        }

        if (!TryReadParameter(section, key, out var rawValue))
            return false;

        value = DecodeUrlValue(rawValue);
        return !string.IsNullOrWhiteSpace(value);
    }

    private bool TryReadParameter(string section, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(section))
            return false;

        var pairs = section.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            var splitIndex = pair.IndexOf('=');
            var rawName = splitIndex >= 0 ? pair.Substring(0, splitIndex) : pair;
            var rawValue = splitIndex >= 0 && splitIndex < pair.Length - 1 ? pair.Substring(splitIndex + 1) : string.Empty;

            var name = UnityWebRequest.UnEscapeURL(rawName);
            if (!string.Equals(name, key, StringComparison.Ordinal))
                continue;

            value = rawValue;
            return true;
        }

        return false;
    }

    private string DecodeUrlValue(string value)
    {
        var decoded = value ?? string.Empty;
        var decodePasses = Mathf.Clamp(_maxUrlDecodePasses, 1, 4);
        for (var pass = 0; pass < decodePasses; pass++)
        {
            var unescaped = UnityWebRequest.UnEscapeURL(decoded);
            if (string.Equals(unescaped, decoded, StringComparison.Ordinal))
                break;
            decoded = unescaped;
        }

        return decoded.Trim();
    }

    private static void HandleFailure(BootstrapContext context, bool shouldThrow, string message)
    {
        if (shouldThrow)
            throw new InvalidOperationException(message);

        context.ReportStatus(message);
    }

    private void Log(string message)
    {
        if (!_debugLogs)
            return;

        Debug.Log($"[BootstrapBackendAuth] {message}", this);
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "<empty>";

        var trimmed = token.Trim();
        if (trimmed.Length <= 8)
            return trimmed;

        return trimmed.Substring(0, 8) + "...";
    }

    private struct AuthResult
    {
        public bool IsSuccess;
        public string AccessToken;
        public string ErrorMessage;
    }
}
