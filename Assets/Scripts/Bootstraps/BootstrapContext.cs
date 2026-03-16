using System;
using UnityEngine;

public sealed class BootstrapContext
{
    private readonly Action<string> _statusReporter;

    public BootstrapContext(MonoBehaviour owner, Action<string> statusReporter)
    {
        Owner = owner;
        _statusReporter = statusReporter;
    }

    public MonoBehaviour Owner { get; }

    public void ReportStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }


        _statusReporter?.Invoke(message);
    }
}
