using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class ClawBackendApiClient : MonoBehaviour
{
    [Serializable]
    public sealed class ApiResult<TData>
    {
        public bool IsSuccess;
        public long StatusCode;
        public string ErrorMessage;
        public string RawBody;
        public TData Data;
    }

    [Serializable]
    public sealed class StartAttemptRequestDto
    {
        public string machineId;
        public string clientBuild;
        public string configVersion;
    }

    [Serializable]
    public sealed class EconomySnapshotDto
    {
        public int ticketsLeft;
    }

    [Serializable]
    public sealed class StartAttemptResponseDto
    {
        public string attemptId;
        public string attemptToken;
        public long serverNowMs;
        public int inputWindowMs;
        public EconomySnapshotDto economySnapshot;
    }

    [Serializable]
    public sealed class MachineSpawnPlanRequestDto
    {
    }

    [Serializable]
    public sealed class MachineSpawnPlanItemDto
    {
        public string toyId;
    }

    [Serializable]
    public sealed class MachineSpawnPlanResponseDto
    {
        public string machineId;
        public long serverNowMs;
        public MachineSpawnPlanItemDto[] items;
    }

    [Serializable]
    public sealed class InputPacketDto
    {
        public int seq;
        public long clientTimeMs;
        public float moveX;
        public float moveY;
    }

    [Serializable]
    public sealed class InputBatchRequestDto
    {
        public InputPacketDto[] packets;
    }

    [Serializable]
    public sealed class InputBatchResponseDto
    {
        public int acceptedSeqUpTo;
        public long serverNowMs;
        public string[] warnings;
    }

    [Serializable]
    public sealed class ContactHintDto
    {
        public string toyHintId;
        public int fingers;
    }

    [Serializable]
    public sealed class ResolveClientSummaryDto
    {
        public long pressTimeMs;
        public long closeStartMs;
        public bool localGrabObserved;
        public ContactHintDto[] contactHints;
    }

    [Serializable]
    public sealed class ResolveAttemptRequestDto
    {
        public ResolveClientSummaryDto clientSummary;
    }

    [Serializable]
    public sealed class RewardDto
    {
        public string id;
        public string code;
        public float rarity;
    }

    [Serializable]
    public sealed class ResolveAttemptResponseDto
    {
        public string attemptId;
        public string status;
        public string result;
        public RewardDto reward;
        public string spawnOnWinToyId;
        public string seedReveal;
        public int riskScore;
    }

    [Serializable]
    public sealed class ClaimRewardRequestDto
    {
        public string attemptId;
    }

    [Serializable]
    public sealed class ClaimRewardResponseDto
    {
        public string status;
        public RewardDto reward;
    }

    [Serializable]
    public sealed class TelegramAuthRequestDto
    {
        public string initData;
    }

    [Serializable]
    public sealed class DevAuthRequestDto
    {
        public string devUserId;
    }

    [Serializable]
    public sealed class TelegramAuthResponseDto
    {
        public string accessToken;
        public int expiresInSec;
    }

    [Serializable]
    private sealed class AccessTokenPayloadDto
    {
        public long exp = 0;
    }

    private const string DefaultTelegramAuthPath = "/v1/auth/telegram";
    private const string DefaultDevAuthPath = "/v1/auth/dev";
    private const string DefaultAccessTokenPlayerPrefsKey = "backend_access_token";
    private const int DefaultRequestTimeoutSeconds = 10;
    private const int DefaultMinTokenRemainingSeconds = 15;

    [Header("Backend Config")]
    [SerializeField] private ClawBackendSettingsAsset _backendSettings;

    [Header("Runtime")]
    [SerializeField] private string _accessToken;

    [Header("Request Policy")]
    [SerializeField] private bool _allowAnonymousRequestsWhenNoToken = true;

    [Header("Logs")]
    [SerializeField] private bool _logFailures = true;
    [SerializeField] private bool _logSuccessResponses = false;
    private bool _anonymousModeLogged;

    private string ConfiguredBaseUrl => _backendSettings == null ? string.Empty : _backendSettings.BaseUrl;

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

    private int ConfiguredTimeoutSeconds =>
        _backendSettings == null ? DefaultRequestTimeoutSeconds : _backendSettings.RequestTimeoutSeconds;

    private int ConfiguredMinTokenRemainingSeconds =>
        _backendSettings == null ? DefaultMinTokenRemainingSeconds : _backendSettings.MinTokenRemainingSeconds;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConfiguredBaseUrl);
    public bool HasAccessToken => !string.IsNullOrWhiteSpace(ResolveAccessToken());
    public string BaseUrl => ConfiguredBaseUrl;
    public string AccessTokenPlayerPrefsKey => ConfiguredAccessTokenPlayerPrefsKey;

    public bool TryGetAccessToken(out string accessToken)
    {
        accessToken = ResolveAccessToken();
        return !string.IsNullOrWhiteSpace(accessToken);
    }

    public void SetAccessToken(string accessToken, bool persistToPlayerPrefs = false)
    {
        _accessToken = (accessToken ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(_accessToken))
            _anonymousModeLogged = false;
        if (_logSuccessResponses)
            Debug.Log($"[BackendApi] Access token set. prefix={MaskToken(_accessToken)}", this);

        var playerPrefsKey = ConfiguredAccessTokenPlayerPrefsKey;
        if (!persistToPlayerPrefs || string.IsNullOrWhiteSpace(playerPrefsKey))
            return;

        if (string.IsNullOrWhiteSpace(_accessToken))
            PlayerPrefs.DeleteKey(playerPrefsKey);
        else
            PlayerPrefs.SetString(playerPrefsKey, _accessToken);

        PlayerPrefs.Save();
    }

    public async Task<ApiResult<TelegramAuthResponseDto>> AuthenticateTelegramAsync(
        TelegramAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        return await SendJsonAsync<TelegramAuthRequestDto, TelegramAuthResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            ConfiguredTelegramAuthPath,
            requestBody,
            headers: null,
            cancellationToken);
    }

    public async Task<ApiResult<TelegramAuthResponseDto>> AuthenticateDevAsync(
        DevAuthRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        return await SendJsonAsync<DevAuthRequestDto, TelegramAuthResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            ConfiguredDevAuthPath,
            requestBody,
            headers: null,
            cancellationToken);
    }

    public async Task<ApiResult<StartAttemptResponseDto>> StartAttemptAsync(
        StartAttemptRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAuthorizedHeaders(null, includeIdempotency: true, out var headers))
            return CreateFailure<StartAttemptResponseDto>(0, "Access token is missing.", string.Empty);

        return await SendJsonAsync<StartAttemptRequestDto, StartAttemptResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            "/v1/attempts/start",
            requestBody,
            headers,
            cancellationToken);
    }

    public async Task<ApiResult<MachineSpawnPlanResponseDto>> GetMachineSpawnPlanAsync(
        string machineId,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAuthorizedHeaders(null, includeIdempotency: false, out var headers))
            return CreateFailure<MachineSpawnPlanResponseDto>(0, "Access token is missing.", string.Empty);

        var escapedMachineId = Uri.EscapeDataString(machineId ?? string.Empty);
        var path = $"/v1/machines/{escapedMachineId}/spawn-plan";
        return await SendJsonAsync<MachineSpawnPlanRequestDto, MachineSpawnPlanResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            path,
            new MachineSpawnPlanRequestDto(),
            headers,
            cancellationToken);
    }

    public async Task<ApiResult<InputBatchResponseDto>> SendInputsAsync(
        string attemptId,
        string attemptToken,
        InputBatchRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAuthorizedHeaders(attemptToken, includeIdempotency: false, out var headers))
            return CreateFailure<InputBatchResponseDto>(0, "Access token is missing.", string.Empty);

        var escapedAttemptId = Uri.EscapeDataString(attemptId ?? string.Empty);
        var path = $"/v1/attempts/{escapedAttemptId}/inputs";
        return await SendJsonAsync<InputBatchRequestDto, InputBatchResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            path,
            requestBody,
            headers,
            cancellationToken);
    }

    public async Task<ApiResult<ResolveAttemptResponseDto>> ResolveAttemptAsync(
        string attemptId,
        string attemptToken,
        ResolveAttemptRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAuthorizedHeaders(attemptToken, includeIdempotency: true, out var headers))
            return CreateFailure<ResolveAttemptResponseDto>(0, "Access token is missing.", string.Empty);

        var escapedAttemptId = Uri.EscapeDataString(attemptId ?? string.Empty);
        var path = $"/v1/attempts/{escapedAttemptId}/resolve";
        return await SendJsonAsync<ResolveAttemptRequestDto, ResolveAttemptResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            path,
            requestBody,
            headers,
            cancellationToken);
    }

    public async Task<ApiResult<ClaimRewardResponseDto>> ClaimRewardAsync(
        ClaimRewardRequestDto requestBody,
        CancellationToken cancellationToken)
    {
        if (!TryBuildAuthorizedHeaders(null, includeIdempotency: true, out var headers))
            return CreateFailure<ClaimRewardResponseDto>(0, "Access token is missing.", string.Empty);

        return await SendJsonAsync<ClaimRewardRequestDto, ClaimRewardResponseDto>(
            UnityWebRequest.kHttpVerbPOST,
            "/v1/rewards/claim",
            requestBody,
            headers,
            cancellationToken);
    }

    private async Task<ApiResult<TResponse>> SendJsonAsync<TRequest, TResponse>(
        string method,
        string path,
        TRequest requestBody,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken) where TResponse : class
    {
        if (!IsConfigured)
            return CreateFailure<TResponse>(0, "Backend base URL is not configured.", string.Empty);

        var url = BuildUrl(path);
        var json = requestBody == null ? "{}" : JsonUtility.ToJson(requestBody);
        var bodyBytes = Encoding.UTF8.GetBytes(json);

        using var request = new UnityWebRequest(url, method);
        request.uploadHandler = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, ConfiguredTimeoutSeconds);

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        if (headers != null)
        {
            foreach (var pair in headers)
                request.SetRequestHeader(pair.Key, pair.Value);
        }

        AppendTelegramMiniAppHeaders(request);

        using var registration = cancellationToken.Register(request.Abort);
        var operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        var rawBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        var statusCode = request.responseCode;
        var isSuccess = request.result == UnityWebRequest.Result.Success && statusCode >= 200 && statusCode < 300;

        if (!isSuccess)
        {
            var message = BuildTransportErrorMessage(statusCode, request.error, rawBody);
            if (_logFailures)
                Debug.LogWarning($"[Backend] Request failed {method} {url}: {message}");
            return CreateFailure<TResponse>(statusCode, message, rawBody);
        }

        if (_logSuccessResponses)
            Debug.Log($"[BackendApi] {method} {path} -> {statusCode}", this);

        TResponse responseData = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                responseData = JsonUtility.FromJson<TResponse>(rawBody);
            }
            catch (Exception exception)
            {
                var parseError = $"Failed to parse JSON response: {exception.Message}";
                if (_logFailures)
                    Debug.LogWarning($"[Backend] {parseError} URL: {url}");
                return CreateFailure<TResponse>(statusCode, parseError, rawBody);
            }
        }

        return new ApiResult<TResponse>
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Data = responseData,
            RawBody = rawBody,
            ErrorMessage = string.Empty
        };
    }

    private bool TryBuildAuthorizedHeaders(
        string attemptToken,
        bool includeIdempotency,
        out Dictionary<string, string> headers)
    {
        headers = new Dictionary<string, string>();

        var accessToken = ResolveAccessToken();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            headers["Authorization"] = $"Bearer {accessToken}";
        }
        else if (_allowAnonymousRequestsWhenNoToken)
        {
            if (_logFailures && !_anonymousModeLogged)
            {
                Debug.LogWarning("[BackendApi] Access token is missing/expired. Sending requests without Authorization (anonymous mode).", this);
                _anonymousModeLogged = true;
            }
        }
        else
        {
            if (_logFailures)
                Debug.LogWarning("[BackendApi] Missing access token for authorized request.", this);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(attemptToken))
            headers["X-Attempt-Token"] = attemptToken;

        if (includeIdempotency)
            headers["Idempotency-Key"] = Guid.NewGuid().ToString("N");

        return true;
    }

    private string ResolveAccessToken()
    {
        if (TryGetUsableToken(_accessToken, out var runtimeToken))
            return runtimeToken;

        var playerPrefsKey = ConfiguredAccessTokenPlayerPrefsKey;
        if (!string.IsNullOrWhiteSpace(playerPrefsKey) && PlayerPrefs.HasKey(playerPrefsKey))
        {
            var persistedToken = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (TryGetUsableToken(persistedToken, out var persistedTokenValue))
                return persistedTokenValue;

            if (_logFailures && !string.IsNullOrWhiteSpace(persistedToken))
                Debug.LogWarning("[BackendApi] Stored access token is invalid or expired and will be removed.", this);

            PlayerPrefs.DeleteKey(playerPrefsKey);
            PlayerPrefs.Save();
        }

        return string.Empty;
    }

    private bool TryGetUsableToken(string rawToken, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(rawToken))
            return false;

        var normalized = rawToken.Trim();
        var minRemaining = Math.Max(0, ConfiguredMinTokenRemainingSeconds);
        if (!IsAccessTokenUsable(normalized, minRemaining))
            return false;

        token = normalized;
        return true;
    }

    public static bool IsAccessTokenUsable(string accessToken, int minRemainingSeconds = 15)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        if (!TryGetTokenExpiryUnixSeconds(accessToken, out var expUnixSeconds))
            return false;

        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return expUnixSeconds - nowUnixSeconds > Math.Max(0, minRemainingSeconds);
    }

    private static bool TryGetTokenExpiryUnixSeconds(string accessToken, out long expUnixSeconds)
    {
        expUnixSeconds = 0;
        var token = (accessToken ?? string.Empty).Trim();
        if (token.Length == 0)
            return false;

        var parts = token.Split('.');
        if (parts.Length < 2)
            return false;

        if (!TryDecodeBase64Url(parts[1], out var payloadJson))
            return false;

        AccessTokenPayloadDto payload;
        try
        {
            payload = JsonUtility.FromJson<AccessTokenPayloadDto>(payloadJson);
        }
        catch
        {
            return false;
        }

        if (payload == null || payload.exp <= 0)
            return false;

        expUnixSeconds = payload.exp;
        return true;
    }

    private static bool TryDecodeBase64Url(string encoded, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        var remainder = base64.Length % 4;
        if (remainder != 0)
            base64 = base64.PadRight(base64.Length + (4 - remainder), '=');

        try
        {
            var bytes = Convert.FromBase64String(base64);
            decoded = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildUrl(string path)
    {
        var baseUrl = ConfiguredBaseUrl.Trim();
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith("/") ? path : "/" + path;
        return normalizedBaseUrl + normalizedPath;
    }

    private static string BuildTransportErrorMessage(long statusCode, string requestError, string rawBody)
    {
        if (!string.IsNullOrWhiteSpace(requestError))
            return $"HTTP {statusCode}: {requestError}";
        if (!string.IsNullOrWhiteSpace(rawBody))
            return $"HTTP {statusCode}: {rawBody}";
        return $"HTTP {statusCode}: request failed";
    }

    private static ApiResult<TData> CreateFailure<TData>(long statusCode, string message, string rawBody)
    {
        return new ApiResult<TData>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = message,
            RawBody = rawBody,
            Data = default
        };
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "<empty>";

        var trimmed = token.Trim();
        return trimmed.Length <= 8 ? trimmed : trimmed.Substring(0, 8) + "...";
    }

    private static void AppendTelegramMiniAppHeaders(UnityWebRequest request)
    {
        if (!TelegramMiniAppSession.IsMiniApp)
            return;

        request.SetRequestHeader("X-Client-Source", "telegram-miniapp");
        request.SetRequestHeader("X-Telegram-MiniApp", "1");

        if (TelegramMiniAppSession.UserId > 0)
            request.SetRequestHeader("X-Telegram-User-Id", TelegramMiniAppSession.UserId.ToString());

        SetOptionalHeader(request, "X-Telegram-Display-Name", TelegramMiniAppSession.DisplayName);
        SetOptionalHeader(request, "X-Telegram-Username", TelegramMiniAppSession.Username);
        SetOptionalHeader(request, "X-Telegram-Language", TelegramMiniAppSession.LanguageCode);
        SetOptionalHeader(request, "X-Telegram-Chat-Type", TelegramMiniAppSession.ChatType);
        SetOptionalHeader(request, "X-Telegram-Start-Param", TelegramMiniAppSession.StartParam);
        SetOptionalHeader(request, "X-Telegram-Platform", TelegramMiniAppSession.Platform);
        SetOptionalHeader(request, "X-Telegram-Init-Source", TelegramMiniAppSession.InitDataSource);
    }

    private static void SetOptionalHeader(UnityWebRequest request, string key, string value)
    {
        var headerValue = SanitizeHeaderValue(value);
        if (string.IsNullOrWhiteSpace(headerValue))
            return;

        request.SetRequestHeader(key, headerValue);
    }

    private static string SanitizeHeaderValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return sanitized.Length <= 128 ? sanitized : sanitized.Substring(0, 128);
    }
}
