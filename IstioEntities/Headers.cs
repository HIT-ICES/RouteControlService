using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;

/// <summary>
///     Message headers can be manipulated when Envoy forwards requests to, or responses from, a destination service.
///     Header manipulation rules can be specified for a specific route destination or for all destinations.
///     The following VirtualService adds a test header with the value true to requests that are routed to any reviews
///     service destination.
///     It also removes the foo response header, but only from responses coming from the v1 subset (version) of the reviews
///     service.
/// </summary>
[Serializable]
public class Headers
{
    [JsonPropertyName("request")] public HeaderOperations? Request { get; set; }

    [JsonPropertyName("response")] public HeaderOperations? Response { get; set; }
    [Serializable]
    public class HeaderOperations
    {
        [JsonPropertyName("add")] public Dictionary<string, string>? Add { get; set; }

        [JsonPropertyName("set")] public Dictionary<string, string>? Set { get; set; }

        [JsonPropertyName("remove")] public List<string>? Remove { get; set; }
    }
}