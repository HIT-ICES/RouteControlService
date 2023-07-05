using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;

/// <summary>
///     L4 routing rule weighted destination.
/// </summary>
[Serializable]
public class RouteDestination
{
    /// <summary>
    ///     Destination uniquely identifies the instances of a service to which the request/connection should be forwarded to.
    /// </summary>
    [JsonPropertyName("destination")]
    public Destination Destination { get; set; }

    /// <summary>
    ///     Weight specifies the relative proportion of traffic to be forwarded to the destination.
    ///     A destination will receive weight/(sum of all weights) requests.
    ///     If there is only one destination in a rule, it will receive all traffic.
    ///     Otherwise, if weight is 0, the destination will not receive any traffic.
    /// </summary>
    [JsonPropertyName("weight")]
    public int? Weight { get; set; }
}

/// <summary>
///     Each routing rule is associated with one or more service versions (see glossary in beginning of document).
///     Weights associated with the version determine the proportion of traffic it receives.
///     For example, the following rule will route 25% of traffic for the “reviews” service to instances with the “v2” tag
///     and the remaining traffic (i.e., 75%) to “v1”.
/// </summary>
[Serializable]
public class HttpRouteDestination : RouteDestination
{
    [JsonPropertyName("headers")] public Headers? Headers { get; set; }
}