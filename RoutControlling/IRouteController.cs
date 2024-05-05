namespace RouteControlService.RoutControlling;

public interface IRouteController
{

    Task<RouteRule[]?> GetAllAsync(string @namespace, string serviceName);
    Task UpdateAllAsync(string @namespace, string serviceName, RouteRule[] newRules);
    Task CreateAllAsync(string @namespace, string serviceName, RouteRule[] newRules);
}

public class FakeRouteController : IRouteController
{
    private readonly Dictionary<(string, string), RouteRule[]> _memoryStorage = new();

    public Task<RouteRule[]?> GetAllAsync(string @namespace, string serviceName)
    {
        return Task.FromResult(_memoryStorage.GetValueOrDefault((@namespace, serviceName)));
    }

    public Task UpdateAllAsync(string @namespace, string serviceName, RouteRule[] newRules)
    {
        _memoryStorage[(@namespace, serviceName)] = newRules;
        return Task.CompletedTask;
    }

    public Task CreateAllAsync(string @namespace, string serviceName, RouteRule[] newRules)
    {
        _memoryStorage[(@namespace, serviceName)] = newRules;
        return Task.CompletedTask;
    }
}