using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LoadSceneQuery : BootstrapLoadQuery
{
    [SerializeField] private string sceneName = "Gameplay";
    [SerializeField] private LoadSceneMode loadSceneMode = LoadSceneMode.Single;
    [SerializeField] private bool showProgress = true;

    public override async Task ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("Scene name is empty.");
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
        if (operation == null)
        {
            throw new InvalidOperationException($"Failed to start loading scene '{sceneName}'.");
        }

        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (showProgress)
            {
                int progressPercent = Mathf.RoundToInt(operation.progress * 100f);
                context.ReportStatus($"{QueryName}: {progressPercent}%");
            }

            await Task.Yield();
        }
    }
}
