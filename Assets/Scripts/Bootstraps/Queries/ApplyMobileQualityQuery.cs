using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class ApplyMobileQualityQuery : BootstrapLoadQuery
{
    [SerializeField] private bool onlyMobilePlatform = true;
    [SerializeField] private int targetFrameRate = 60;
    [SerializeField] private int vSyncCount = 0;

    public override Task ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        bool shouldApply = !onlyMobilePlatform || Application.isMobilePlatform;
        if (!shouldApply)
        {
            return Task.CompletedTask;
        }

        QualitySettings.vSyncCount = vSyncCount;
        Application.targetFrameRate = targetFrameRate;
        return Task.CompletedTask;
    }
}
