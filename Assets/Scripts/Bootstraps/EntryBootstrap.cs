using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public sealed class EntryBootstrap : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private bool autoFindTextInChildren = true;
    [SerializeField] private string completedText = "Loading completed";
    [SerializeField] private string failedPrefix = "Loading failed: ";

    private CancellationTokenSource _cancellationTokenSource;

    private void Awake()
    {
        if (loadingText == null && autoFindTextInChildren)
        {
            loadingText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private async void Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        await RunQueriesAsync(_cancellationTokenSource.Token);
    }

    private void OnDestroy()
    {
        if (_cancellationTokenSource == null)
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task RunQueriesAsync(CancellationToken cancellationToken)
    {
        BootstrapLoadQuery[] queries = GetComponents<BootstrapLoadQuery>()
            .Where(query => query.enabled)
            .OrderBy(query => query.Order)
            .ToArray();

        if (queries.Length == 0)
        {
            SetStatus("No bootstrap queries configured.");
            return;
        }

        var context = new BootstrapContext(this, SetStatus);

        try
        {
            for (int index = 0; index < queries.Length; index++)
            {
                BootstrapLoadQuery query = queries[index];
                SetStatus($"[{index + 1}/{queries.Length}] {query.QueryName}");
                await query.ExecuteAsync(context, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(completedText))
            {
                SetStatus(completedText);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Loading canceled.");
        }
        catch (Exception exception)
        {
            SetStatus(failedPrefix + exception.Message);
            Debug.LogException(exception, this);
        }
    }

    private void SetStatus(string message)
    {
        if (loadingText != null)
        {
            loadingText.text = message;
        }

        Debug.Log($"[Bootstrap] {message}", this);
    }
}
