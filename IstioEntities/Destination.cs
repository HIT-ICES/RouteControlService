using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;

[Serializable]
public class Destination
{
    [JsonPropertyName("host")] public string Host { get; set; }

    [JsonPropertyName("subset")] public string? Subset { get; set; }

    [JsonPropertyName("port")] public PortSelector? Port { get; set; }
}