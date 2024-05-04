using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace RouteControlService;
[Serializable]
public abstract class CustomResource : KubernetesObject, IMetadata<V1ObjectMeta>
{
    /// <inheritdoc />
    protected CustomResource(string kind, string apiVersion)
    {
        Kind = kind;
        ApiVersion = apiVersion;
    }
    [JsonPropertyName("kind")] public string Kind { get; }
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; }
    [JsonPropertyName("metadata")] public V1ObjectMeta Metadata { get; set; } = new();
}
[Serializable]
public abstract class CustomResource<TSpec> : CustomResource
{
    /// <inheritdoc />
    protected CustomResource(string kind, string apiVersion, TSpec spec) : base(kind, apiVersion) { Spec = spec; }
    [JsonPropertyName("spec")] public TSpec Spec { get; set; }
}
[Serializable]
public class CustomResourceList<T> : KubernetesObject
    where T : CustomResource
{
    public V1ListMeta Metadata { get; set; }
    public List<T> Items { get; set; }
}

public class CResource
{
}