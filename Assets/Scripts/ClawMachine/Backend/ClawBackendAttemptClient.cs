using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class ClawBackendAttemptClient : MonoBehaviour
{
    [Serializable]
    public sealed class AttemptResolvedEventArgs
    {
        public string attemptId;
        public string result;
        public string rewardCode;
        public string spawnOnWinToyId;
        public float rewardRarity;
        public int riskScore;
        public string outcomeReason;
        public float chance;
        public float rewardRoll;
        public string selectedRewardCode;
        public float keepChance;
        public float dropRoll;
        public bool dropTriggered;
        public bool localGrabObserved;
        public bool serverValidatedGrab;
        public float dropAlignment;
        public float stability;
        public float timingQuality;
        public bool lockedPhaseMovement;
        public float skillScore;
        public string errorMessage;
    }

    [Serializable]
    public sealed class GrabOutcomePreviewEventArgs
    {
        public string attemptId;
        public string predictedResultIfGrabbed;
        public bool shouldDropOnGrab;
        public string outcomeReason;
        public float chance;
        public float rewardRoll;
        public string selectedRewardCode;
        public float keepChance;
        public float dropRoll;
        public bool dropTriggered;
        public bool localGrabObserved;
        public bool serverValidatedGrab;
        public float dropAlignment;
        public float stability;
        public float timingQuality;
        public bool lockedPhaseMovement;
        public float skillScore;
    }

    [Serializable]
    private sealed class ResultProbeRequestDto
    {
        public string attemptId;
        public string result;
        public bool won;
        public int riskScore;
        public long clientTimeMs;
    }

    [Header("References")]
    [SerializeField] private ClawBackendApiClient _apiClient;

    [Header("Attempt Config")]
    [SerializeField] private string _machineId = "main";
    [SerializeField] private string _configVersion = "v1-default";
    [SerializeField, Min(0.02f)] private float _inputSampleIntervalSeconds = 0.04f;
    [SerializeField, Min(0.03f)] private float _inputFlushIntervalSeconds = 0.12f;
    [SerializeField, Min(1)] private int _minPacketsPerFlush = 4;
    [SerializeField, Min(1)] private int _maxPacketsPerFlush = 12;
    [SerializeField, Min(8)] private int _maxBufferedPackets = 256;

    [Header("Behavior")]
    [SerializeField] private bool _allowOfflineFallback = true;
    [SerializeField] private bool _autoClaimReward = true;
    [SerializeField] private bool _verboseLogs = true;

    [Header("Debug Probe")]
    [SerializeField] private bool _sendResultToPlaceholderEndpoint = true;
    [SerializeField] private string _resultProbePath = "/v1/debug/nonexistent/attempt-result";
    [SerializeField, Min(100)] private int _resultProbeTimeoutMs = 1200;
    [SerializeField] private bool _resultProbeIncludeAuthHeader = true;

    public event Action<AttemptResolvedEventArgs> AttemptResolved;
    public event Action<GrabOutcomePreviewEventArgs> GrabOutcomePreviewReady;

    public bool IsBackendEnabled => _apiClient != null && _apiClient.IsConfigured;
    public bool IsAttemptActive => _hasServerAttempt;

    private readonly List<ClawBackendApiClient.InputPacketDto> _inputBuffer = new();
    private CancellationTokenSource _destroyCancellationTokenSource;
    private string _attemptId;
    private string _attemptToken;
    private bool _hasServerAttempt;
    private bool _startInFlight;
    private bool _flushInFlight;
    private bool _resolveInFlight;
    private bool _previewInFlight;
    private bool _acceptInput;
    private bool _localGrabObserved;
    private string _grabbedToyHintId;
    private int _grabbedToyHintFingers;
    private GrabOutcomePreviewEventArgs _grabOutcomePreview;
    private int _nextSeq;
    private long _pressTimeMs;
    private long _closeStartTimeMs;
    private float _sampleTimer;
    private float _flushTimer;
    private Vector2 _latestMove;

    void Awake()
    {
        if (_apiClient == null)
            _apiClient = GetComponent<ClawBackendApiClient>();
        if (_apiClient == null)
            _apiClient = FindFirstObjectByType<ClawBackendApiClient>();

        Log(_apiClient == null
            ? "No ClawBackendApiClient found. Backend calls will fallback if allowed."
            : "ClawBackendApiClient found.");

        _destroyCancellationTokenSource = new CancellationTokenSource();
    }

    void OnDestroy()
    {
        CancelActiveAttempt();

        if (_destroyCancellationTokenSource == null)
            return;

        _destroyCancellationTokenSource.Cancel();
        _destroyCancellationTokenSource.Dispose();
        _destroyCancellationTokenSource = null;
    }

    void Update()
    {
        if (!_acceptInput || !_hasServerAttempt)
            return;

        var dt = Time.unscaledDeltaTime;
        if (dt <= 0f)
            return;

        _sampleTimer += dt;
        _flushTimer += dt;

        var sampleInterval = Mathf.Max(0.02f, _inputSampleIntervalSeconds);
        while (_sampleTimer >= sampleInterval)
        {
            _sampleTimer -= sampleInterval;
            CaptureMovementSample();
        }

        if (_flushInFlight || _inputBuffer.Count == 0)
            return;

        var minBatchSize = Mathf.Max(1, _minPacketsPerFlush);
        var hasEnoughPackets = _inputBuffer.Count >= minBatchSize;
        var flushInterval = Mathf.Max(0.03f, _inputFlushIntervalSeconds);
        var reachedFlushDeadline = _flushTimer >= flushInterval;
        if (!hasEnoughPackets && !reachedFlushDeadline)
            return;

        _flushTimer = 0f;
        _ = FlushBufferedInputsAsync(flushAll: false);
    }

    public async Task<bool> TryStartAttemptAsync()
    {
        if (_startInFlight)
        {
            Log("TryStartAttemptAsync ignored: start already in flight.");
            return false;
        }
        if (_hasServerAttempt)
        {
            Log("TryStartAttemptAsync ignored: attempt already active.");
            return false;
        }
        if (!IsBackendEnabled)
        {
            Log($"Backend disabled or unconfigured. Offline fallback={_allowOfflineFallback}.");
            return _allowOfflineFallback;
        }

        _startInFlight = true;
        try
        {
            Log("Starting backend attempt...");
            var normalizedConfigVersion = NormalizeConfigVersion(_configVersion);
            if (_verboseLogs && !string.Equals(normalizedConfigVersion, (_configVersion ?? string.Empty).Trim(), StringComparison.Ordinal))
                Log($"Normalized config version '{_configVersion}' -> '{normalizedConfigVersion}'.");

            var request = new ClawBackendApiClient.StartAttemptRequestDto
            {
                machineId = _machineId,
                clientBuild = Application.version,
                configVersion = normalizedConfigVersion
            };

            var result = await _apiClient.StartAttemptAsync(request, DestroyToken);
            if (!result.IsSuccess || result.Data == null || string.IsNullOrWhiteSpace(result.Data.attemptId) || string.IsNullOrWhiteSpace(result.Data.attemptToken))
            {
                if (_verboseLogs)
                {
                    var error = result == null ? "Unknown start attempt error." : result.ErrorMessage;
                    Debug.LogWarning($"[BackendAttempt] Start failed. {error}");
                }

                return _allowOfflineFallback;
            }

            _attemptId = result.Data.attemptId;
            _attemptToken = result.Data.attemptToken;
            _hasServerAttempt = true;
            _acceptInput = true;
            _localGrabObserved = false;
            _nextSeq = 1;
            _sampleTimer = 0f;
            _flushTimer = 0f;
            _latestMove = Vector2.zero;
            _pressTimeMs = GetUnixTimeMilliseconds();
            _closeStartTimeMs = 0;
            _grabbedToyHintId = string.Empty;
            _grabbedToyHintFingers = 0;
            _grabOutcomePreview = null;
            _inputBuffer.Clear();
            Log($"Attempt started. id={ShortId(_attemptId)}, token={ShortId(_attemptToken)}");

            return true;
        }
        catch (OperationCanceledException)
        {
            Log("Start attempt cancelled.");
            return _allowOfflineFallback;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return _allowOfflineFallback;
        }
        finally
        {
            _startInFlight = false;
        }
    }

    public void OnMovementChanged(Vector2 move)
    {
        _latestMove = Vector2.ClampMagnitude(move, 1f);
    }

    public void MarkCloseStarted()
    {
        if (!_hasServerAttempt || _closeStartTimeMs > 0)
            return;

        _closeStartTimeMs = GetUnixTimeMilliseconds();
        _latestMove = Vector2.zero;
        _acceptInput = false;
        CaptureMovementSample();
        Log($"Close phase started for attempt={ShortId(_attemptId)} at {_closeStartTimeMs}.");
    }

    public void MarkLocalGrabObserved(string toyHintId, int fingerCount)
    {
        _localGrabObserved = true;
        _grabbedToyHintId = NormalizeToyHintId(toyHintId);
        _grabbedToyHintFingers = Mathf.Max(0, fingerCount);

        if (!_hasServerAttempt || _closeStartTimeMs <= 0 || _resolveInFlight || _previewInFlight)
            return;

        _ = PreviewAttemptIfGrabbedAsync();
    }

    public bool TryGetGrabOutcomePreview(out GrabOutcomePreviewEventArgs preview)
    {
        preview = _grabOutcomePreview;
        return preview != null;
    }

    public void CompleteAttempt(bool localGrabObserved)
    {
        if (!_hasServerAttempt || _resolveInFlight)
            return;

        _localGrabObserved = _localGrabObserved || localGrabObserved;
        _acceptInput = false;
        Log($"CompleteAttempt called. localGrabObserved={_localGrabObserved}. Resolving...");
        _ = ResolveAttemptAsync();
    }

    public void CancelActiveAttempt()
    {
        ResetAttemptState();
    }

    private async Task ResolveAttemptAsync()
    {
        _resolveInFlight = true;
        var attemptId = _attemptId;
        var attemptToken = _attemptToken;
        var localGrabObserved = _localGrabObserved;

        try
        {
            if (_acceptInput)
                CaptureMovementSample();
            await WaitUntilNoFlushInFlightAsync();
            await FlushBufferedInputsAsync(flushAll: true);

            var resolveRequest = new ClawBackendApiClient.ResolveAttemptRequestDto
            {
                clientSummary = new ClawBackendApiClient.ResolveClientSummaryDto
                {
                    pressTimeMs = _pressTimeMs,
                    closeStartMs = _closeStartTimeMs,
                    localGrabObserved = localGrabObserved,
                    contactHints = BuildContactHints()
                }
            };

            var resolveResult = await _apiClient.ResolveAttemptAsync(attemptId, attemptToken, resolveRequest, DestroyToken);
            if (!resolveResult.IsSuccess || resolveResult.Data == null)
            {
                Log($"Resolve failed. id={ShortId(attemptId)} error={resolveResult?.ErrorMessage}");
                EmitResolutionEvent(new AttemptResolvedEventArgs
                {
                    attemptId = attemptId,
                    result = "error",
                    errorMessage = resolveResult == null ? "Resolve failed." : resolveResult.ErrorMessage
                });
                return;
            }

            var response = resolveResult.Data;
            if (_autoClaimReward && string.Equals(response.result, "win", StringComparison.OrdinalIgnoreCase))
            {
                var claimRequest = new ClawBackendApiClient.ClaimRewardRequestDto
                {
                    attemptId = attemptId
                };

                var claimResult = await _apiClient.ClaimRewardAsync(claimRequest, DestroyToken);
                if (!claimResult.IsSuccess && _verboseLogs)
                {
                    Debug.LogWarning($"[BackendAttempt] Claim failed for attempt {attemptId}. {claimResult.ErrorMessage}");
                }
            }
            var debug = response.debug;
            Log(
                $"Resolve completed. id={ShortId(response.attemptId)} result={response.result} reason={debug?.outcomeReason ?? "n/a"} risk={response.riskScore} " +
                $"localGrab={debug?.localGrabObserved ?? false} serverValidated={debug?.serverValidatedGrab ?? false} " +
                $"alignment={debug?.replay?.dropAlignment ?? 0f:0.###} skill={debug?.replay?.skillScore ?? 0f:0.###} " +
                $"chance={debug?.chance ?? 0f:0.###} rewardRoll={debug?.rewardRoll ?? 0f:0.###} reward={debug?.selectedRewardCode ?? "<none>"} " +
                $"dropTriggered={debug?.dropTriggered ?? false} keepChance={debug?.keepChance ?? 0f:0.###} dropRoll={debug?.dropRoll ?? 0f:0.###}");
            _ = SendResultProbeAsync(response.attemptId, response.result, response.riskScore);

            EmitResolutionEvent(new AttemptResolvedEventArgs
            {
                attemptId = response.attemptId,
                result = response.result,
                rewardCode = response.reward != null ? response.reward.code : string.Empty,
                spawnOnWinToyId = response.spawnOnWinToyId ?? string.Empty,
                rewardRarity = response.reward != null ? response.reward.rarity : 0f,
                riskScore = response.riskScore,
                outcomeReason = debug != null ? debug.outcomeReason ?? string.Empty : string.Empty,
                chance = debug != null ? debug.chance : 0f,
                rewardRoll = debug != null ? debug.rewardRoll : 0f,
                selectedRewardCode = debug != null ? debug.selectedRewardCode ?? string.Empty : string.Empty,
                keepChance = debug != null ? debug.keepChance : 0f,
                dropRoll = debug != null ? debug.dropRoll : 0f,
                dropTriggered = debug != null && debug.dropTriggered,
                localGrabObserved = debug != null && debug.localGrabObserved,
                serverValidatedGrab = debug != null && debug.serverValidatedGrab,
                dropAlignment = debug != null && debug.replay != null ? debug.replay.dropAlignment : 0f,
                stability = debug != null && debug.replay != null ? debug.replay.stability : 0f,
                timingQuality = debug != null && debug.replay != null ? debug.replay.timingQuality : 0f,
                lockedPhaseMovement = debug != null && debug.replay != null && debug.replay.lockedPhaseMovement,
                skillScore = debug != null && debug.replay != null ? debug.replay.skillScore : 0f
            });
        }
        catch (OperationCanceledException)
        {
            Log($"Resolve cancelled. id={ShortId(attemptId)}");
            EmitResolutionEvent(new AttemptResolvedEventArgs
            {
                attemptId = attemptId,
                result = "cancelled",
                errorMessage = "Attempt resolve cancelled."
            });
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            EmitResolutionEvent(new AttemptResolvedEventArgs
            {
                attemptId = attemptId,
                result = "error",
                errorMessage = exception.Message
            });
        }
        finally
        {
            Log($"Attempt finalized. Resetting local state for id={ShortId(attemptId)}");
            ResetAttemptState();
            _resolveInFlight = false;
        }
    }

    private async Task PreviewAttemptIfGrabbedAsync()
    {
        if (_previewInFlight || !_hasServerAttempt || _closeStartTimeMs <= 0)
            return;

        _previewInFlight = true;
        var attemptId = _attemptId;
        var attemptToken = _attemptToken;

        try
        {
            await WaitUntilNoFlushInFlightAsync();
            await FlushBufferedInputsAsync(flushAll: true);

            var previewRequest = new ClawBackendApiClient.PreviewAttemptIfGrabbedRequestDto
            {
                clientSummary = new ClawBackendApiClient.ResolveClientSummaryDto
                {
                    pressTimeMs = _pressTimeMs,
                    closeStartMs = _closeStartTimeMs,
                    localGrabObserved = true,
                    contactHints = BuildContactHints()
                }
            };

            var previewResult = await _apiClient.PreviewAttemptIfGrabbedAsync(
                attemptId,
                attemptToken,
                previewRequest,
                DestroyToken);
            if (!previewResult.IsSuccess || previewResult.Data == null)
            {
                Log($"Preview failed. id={ShortId(attemptId)} error={previewResult?.ErrorMessage}");
                return;
            }

            var response = previewResult.Data;
            var debug = response.debug;
            _grabOutcomePreview = new GrabOutcomePreviewEventArgs
            {
                attemptId = response.attemptId,
                predictedResultIfGrabbed = response.predictedResultIfGrabbed,
                shouldDropOnGrab = response.shouldDropOnGrab,
                outcomeReason = debug != null ? debug.outcomeReason ?? string.Empty : string.Empty,
                chance = debug != null ? debug.chance : 0f,
                rewardRoll = debug != null ? debug.rewardRoll : 0f,
                selectedRewardCode = debug != null ? debug.selectedRewardCode ?? string.Empty : string.Empty,
                keepChance = debug != null ? debug.keepChance : 0f,
                dropRoll = debug != null ? debug.dropRoll : 0f,
                dropTriggered = debug != null && debug.dropTriggered,
                localGrabObserved = debug != null && debug.localGrabObserved,
                serverValidatedGrab = debug != null && debug.serverValidatedGrab,
                dropAlignment = debug != null && debug.replay != null ? debug.replay.dropAlignment : 0f,
                stability = debug != null && debug.replay != null ? debug.replay.stability : 0f,
                timingQuality = debug != null && debug.replay != null ? debug.replay.timingQuality : 0f,
                lockedPhaseMovement = debug != null && debug.replay != null && debug.replay.lockedPhaseMovement,
                skillScore = debug != null && debug.replay != null ? debug.replay.skillScore : 0f
            };

            Log(
                $"Preview ready. id={ShortId(response.attemptId)} predicted={response.predictedResultIfGrabbed} shouldDrop={response.shouldDropOnGrab} " +
                $"reason={_grabOutcomePreview.outcomeReason} alignment={_grabOutcomePreview.dropAlignment:0.###} skill={_grabOutcomePreview.skillScore:0.###} " +
                $"chance={_grabOutcomePreview.chance:0.###} rewardRoll={_grabOutcomePreview.rewardRoll:0.###} reward={_grabOutcomePreview.selectedRewardCode} " +
                $"keepChance={_grabOutcomePreview.keepChance:0.###} dropRoll={_grabOutcomePreview.dropRoll:0.###}");
            GrabOutcomePreviewReady?.Invoke(_grabOutcomePreview);
        }
        catch (OperationCanceledException)
        {
            Log($"Preview cancelled. id={ShortId(attemptId)}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            _previewInFlight = false;
        }
    }

    private async Task FlushBufferedInputsAsync(bool flushAll)
    {
        if (_flushInFlight || !_hasServerAttempt || _inputBuffer.Count == 0)
            return;

        _flushInFlight = true;
        try
        {
            do
            {
                var takeCount = Mathf.Min(_inputBuffer.Count, Mathf.Max(1, _maxPacketsPerFlush));
                var packets = _inputBuffer.GetRange(0, takeCount).ToArray();
                Log($"Flushing input batch. attempt={ShortId(_attemptId)} count={packets.Length}");
                var request = new ClawBackendApiClient.InputBatchRequestDto { packets = packets };
                var result = await _apiClient.SendInputsAsync(_attemptId, _attemptToken, request, DestroyToken);
                if (!result.IsSuccess)
                {
                    if (_verboseLogs)
                        Debug.LogWarning($"[BackendAttempt] Input flush failed. {result.ErrorMessage}");
                    break;
                }

                _inputBuffer.RemoveRange(0, takeCount);
            }
            while (flushAll && _inputBuffer.Count > 0);
        }
        finally
        {
            _flushInFlight = false;
        }
    }

    private async Task WaitUntilNoFlushInFlightAsync()
    {
        while (_flushInFlight)
        {
            DestroyToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private async Task SendResultProbeAsync(string attemptId, string result, int riskScore)
    {
        if (!_sendResultToPlaceholderEndpoint || _apiClient == null || !IsBackendEnabled)
            return;

        var probeUrl = BuildResultProbeUrl(_apiClient.BaseUrl, _resultProbePath);
        if (string.IsNullOrWhiteSpace(probeUrl))
            return;

        var payload = new ResultProbeRequestDto
        {
            attemptId = attemptId,
            result = result,
            won = string.Equals(result, "win", StringComparison.OrdinalIgnoreCase),
            riskScore = riskScore,
            clientTimeMs = GetUnixTimeMilliseconds()
        };

        var token = DestroyToken;
        try
        {
            var json = JsonUtility.ToJson(payload);
            using var request = new UnityWebRequest(probeUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(100, _resultProbeTimeoutMs) / 1000f));

            if (_resultProbeIncludeAuthHeader && _apiClient.TryGetAccessToken(out var accessToken))
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    token.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                Log($"Result probe sent. status={request.responseCode} endpoint={probeUrl}");
                return;
            }

            if (_verboseLogs)
            {
                if (request.responseCode == 404)
                {
                    Log($"Result probe got 404 (expected for placeholder endpoint). result={result} attempt={ShortId(attemptId)}");
                }
                else
                {
                    Log($"Result probe failed (non-blocking). status={request.responseCode} error={request.error}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("Result probe cancelled.");
        }
        catch (Exception exception)
        {
            if (_verboseLogs)
                Debug.LogWarning($"[BackendAttempt] Result probe exception: {exception.Message}", this);
        }
    }

    private void CaptureMovementSample()
    {
        if (!_hasServerAttempt)
            return;

        var packet = new ClawBackendApiClient.InputPacketDto
        {
            seq = _nextSeq++,
            clientTimeMs = GetUnixTimeMilliseconds(),
            moveX = Mathf.Clamp(_latestMove.x, -1f, 1f),
            moveY = Mathf.Clamp(_latestMove.y, -1f, 1f)
        };

        _inputBuffer.Add(packet);
        if (_inputBuffer.Count <= Mathf.Max(8, _maxBufferedPackets))
            return;

        var overflow = _inputBuffer.Count - Mathf.Max(8, _maxBufferedPackets);
        _inputBuffer.RemoveRange(0, overflow);
    }

    private void ResetAttemptState()
    {
        _attemptId = string.Empty;
        _attemptToken = string.Empty;
        _hasServerAttempt = false;
        _acceptInput = false;
        _localGrabObserved = false;
        _grabbedToyHintId = string.Empty;
        _grabbedToyHintFingers = 0;
        _nextSeq = 1;
        _pressTimeMs = 0;
        _closeStartTimeMs = 0;
        _sampleTimer = 0f;
        _flushTimer = 0f;
        _latestMove = Vector2.zero;
        _inputBuffer.Clear();
        _flushInFlight = false;
        _startInFlight = false;
        _previewInFlight = false;
        _grabOutcomePreview = null;
    }

    private static long GetUnixTimeMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private CancellationToken DestroyToken =>
        _destroyCancellationTokenSource == null
            ? CancellationToken.None
            : _destroyCancellationTokenSource.Token;

    private void EmitResolutionEvent(AttemptResolvedEventArgs eventArgs)
    {
        Log($"EmitResolutionEvent: result={eventArgs.result}, attempt={ShortId(eventArgs.attemptId)}");
        AttemptResolved?.Invoke(eventArgs);
    }

    private void Log(string message)
    {
        if (!_verboseLogs)
            return;

        Debug.Log($"[BackendAttempt] {message}", this);
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<empty>";

        var trimmed = value.Trim();
        return trimmed.Length <= 8 ? trimmed : trimmed.Substring(0, 8);
    }

    private static string NormalizeConfigVersion(string configVersion)
    {
        var normalized = (configVersion ?? string.Empty).Trim();
        if (string.Equals(normalized, "v1", StringComparison.OrdinalIgnoreCase))
            return "v1-default";

        return normalized;
    }

    private ClawBackendApiClient.ContactHintDto[] BuildContactHints()
    {
        if (string.IsNullOrWhiteSpace(_grabbedToyHintId))
            return Array.Empty<ClawBackendApiClient.ContactHintDto>();

        return new[]
        {
            new ClawBackendApiClient.ContactHintDto
            {
                toyHintId = _grabbedToyHintId,
                fingers = Mathf.Max(0, _grabbedToyHintFingers)
            }
        };
    }

    private static string NormalizeToyHintId(string toyHintId)
    {
        return string.IsNullOrWhiteSpace(toyHintId) ? string.Empty : toyHintId.Trim();
    }

    private static string BuildResultProbeUrl(string baseUrl, string probePath)
    {
        var normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
            return string.Empty;

        var normalizedProbePath = (probePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedProbePath))
            normalizedProbePath = "/v1/debug/nonexistent/attempt-result";

        if (Uri.TryCreate(normalizedProbePath, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        if (!normalizedProbePath.StartsWith("/", StringComparison.Ordinal))
            normalizedProbePath = "/" + normalizedProbePath;

        return normalizedBaseUrl + normalizedProbePath;
    }
}
