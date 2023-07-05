using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace RouteControlService;

public abstract class CustomResource : KubernetesObject, IMetadata<V1ObjectMeta>
{
    [JsonPropertyName("metadata")] public V1ObjectMeta Metadata { get; set; } = new();
}

public abstract class CustomResource<TSpec> : CustomResource
{
    [JsonPropertyName("spec")] public TSpec Spec { get; set; }
}

public class CustomResourceList<T> : KubernetesObject
    where T : CustomResource
{
    public V1ListMeta Metadata { get; set; }
    public List<T> Items { get; set; }
}

public class CResource
{
}