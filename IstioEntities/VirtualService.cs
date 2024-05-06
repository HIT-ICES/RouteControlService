using System.Text.Json.Serialization;

// Refer to https://istio.io/latest/docs/reference/config/networking/virtual-service/
namespace RouteControlService.IstioEntities;

[Serializable]
public class VirtualService() : CustomResource<VirtualServiceSpec>
    ("VirtualService", $"{GROUP}/{VERSION}", new VirtualServiceSpec())
{
    public static readonly string PLURAL = "virtualservices";
    public static readonly string GROUP = "networking.istio.io";
    public static readonly string VERSION = "v1alpha3";
}

[Serializable]
public class VirtualServiceSpec
{
    [JsonPropertyName("hosts")] public List<string>? Hosts { get; set; }

    [JsonPropertyName("gateways")] public List<string>? Gateways { get; set; }

    [JsonPropertyName("exportTo")] public List<string>? ExportTo { get; set; }

    [JsonPropertyName("http")] public List<HttpRoute>? Http { get; set; }

    [JsonPropertyName("tls")] public List<HttpRoute>? Tls { get; set; }

    [JsonPropertyName("tcp")] public List<HttpRoute>? Tcp { get; set; }
}

[Serializable]
public class Percent
{
    [JsonPropertyName("value")] public double Value { get; set; }
}

[Serializable]
public class PortSelector
{
    [JsonPropertyName("number")] public uint Number { get; set; }
}

/// <summary>
///     Describes match conditions and actions for routing HTTP/1.1, HTTP2, and gRPC traffic.
///     See VirtualService for usage examples.
/// </summary>
[Serializable]
public class HttpRoute
{
    // TODO: Partial
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("match")] public List<HTTPMatchRequest>? Match { get; set; }

    [JsonPropertyName("route")] public List<HttpRouteDestination>? Route { get; set; }
}

[Serializable]
public class TlsRoute
{
    // TODO
}

[Serializable]
public class TcpRoute
{
    // TODO
}