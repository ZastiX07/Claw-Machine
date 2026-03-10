using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public abstract class BootstrapLoadQuery : MonoBehaviour
{
    [SerializeField] private int order;
    [SerializeField] private string queryName = "New Query";

    public int Order => order;

    public virtual string QueryName =>
        string.IsNullOrWhiteSpace(queryName) ? GetType().Name : queryName;

    public abstract Task ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken);
}
