using k8s.Autorest;

namespace RouteControlService.RoutControlling;

public enum RouteControllingExceptionType
{
    Undefined,
    ConcurrencyConflict,
    UnmanagedPods,
    BadPodLabels,
    BadResource,
    ResourceNotFound,
    BadUpstream
}

public class RouteControllingException : Exception
{
    public RouteControllingException(string message, RouteControllingExceptionType type) : base(message)
    {
        Type = type;
    }

    public RouteControllingException(string message, HttpOperationException exception) : base
        (message, exception)
    {
        Type = RouteControllingExceptionType.BadUpstream;
    }

    public RouteControllingExceptionType Type { get; }
}

public interface IRouteController
{
    Task<RouteRule[]?> GetAllAsync(KubernetesResourceId serviceRef);
    Task UpdateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules);
    Task CreateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules);
}

public class FakeRouteController : IRouteController
{
    private readonly Dictionary<KubernetesResourceId, RouteRule[]> _memoryStorage = new();

    public Task<RouteRule[]?> GetAllAsync(KubernetesResourceId serviceRef)
    {
        return Task.FromResult(_memoryStorage.GetValueOrDefault(serviceRef));
    }

    public Task UpdateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules)
    {
        _memoryStorage[serviceRef] = newRules;
        return Task.CompletedTask;
    }

    public Task CreateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules)
    {
        _memoryStorage[serviceRef] = newRules;
        return Task.CompletedTask;
    }
}