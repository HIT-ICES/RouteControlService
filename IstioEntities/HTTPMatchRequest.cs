using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;

[Serializable]
public class HTTPMatchRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("uri")] public StringMatch? Uri { get; set; }

    [JsonPropertyName("scheme")] public StringMatch? Scheme { get; set; }

    [JsonPropertyName("method")] public StringMatch? Method { get; set; }

    [JsonPropertyName("authority")] public StringMatch? Authority { get; set; }

    [JsonPropertyName("headers")] public Dictionary<string, StringMatch>? Headers { get; set; }

    [JsonPropertyName("port")] public uint Port { get; set; }

    [JsonPropertyName("sourceLabels")] public Dictionary<string, string>? SourceLabels { get; set; }

    [JsonPropertyName("gateways")] public List<string>? Gateways { get; set; }

    [JsonPropertyName("queryParams")] public Dictionary<string, StringMatch>? QueryParams { get; set; }

    [JsonPropertyName("ignoreUriCase")] public bool? IgnoreUriCase { get; set; }

    [JsonPropertyName("withoutHeaders")] public Dictionary<string, StringMatch>? WithoutHeaders { get; set; }

    [JsonPropertyName("sourceNamespace")] public string? SourceNamespace { get; set; }

    [JsonPropertyName("statPrefix")] public string? StatPrefix { get; set; }
}