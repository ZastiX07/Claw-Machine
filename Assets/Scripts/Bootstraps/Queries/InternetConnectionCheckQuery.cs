using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class InternetConnectionCheckQuery : BootstrapLoadQuery
{
    [SerializeField] private string checkUrl = "https://www.gstatic.com/generate_204";
    [SerializeField] [Min(1)] private int timeoutSeconds = 5;
    [SerializeField] private bool failIfNoConnection = true;

    public override async Task ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        using var request = UnityWebRequest.Get(checkUrl);
        request.timeout = timeoutSeconds;

        using CancellationTokenRegistration registration = cancellationToken.Register(request.Abort);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        if (request.result == UnityWebRequest.Result.Success)
        {
            return;
        }

        string error = string.IsNullOrWhiteSpace(request.error) ? "Unknown connection error" : request.error;
        string message = $"Internet check failed ({error}).";

        if (failIfNoConnection)
        {
            throw new InvalidOperationException(message);
        }

        context.ReportStatus(message);
    }
}
