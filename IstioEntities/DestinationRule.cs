using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;
/// <summary>
/// DestinationRule defines policies that apply to traffic intended for a service after routing has occurred.
/// </summary>
[Serializable]
public class DestinationRule() : CustomResource<DestinationRuleSpec>("DestinationRule", $"{GROUP}/{VERSION}",new())
{
    public static readonly string PLURAL = "destinationrules";
    public static readonly string GROUP = "networking.istio.io";
    public static readonly string VERSION = "v1alpha3";
}
[Serializable]
public class DestinationRuleSpec
{
    // Partial TODO
    /// <summary>
    /// The name of a service from the service registry.
    /// Service names are looked up from the platform’s service registry (e.g., Kubernetes services, Consul services, etc.) and from the hosts declared by ServiceEntries.
    /// Rules defined for services that do not exist in the service registry will be ignored.
    ///
    /// Note for Kubernetes users:
    /// When short names are used (e.g. “reviews” instead of “reviews.default.svc.cluster.local”),
    /// Istio will interpret the short name based on the namespace of the rule, not the service.
    /// A rule in the “default” namespace containing a host “reviews” will be interpreted as “reviews.default.svc.cluster.local”,
    /// irrespective of the actual namespace associated with the reviews service.
    /// To avoid potential misconfigurations, it is recommended to always use fully qualified domain names over short names.
    ///
    /// Note that the host field applies to both HTTP and TCP services.
    /// </summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = "*";
    /// <summary>
    /// One or more named sets that represent individual versions of a service.Traffic policies can be overridden at subset level.
    /// </summary>
    [JsonPropertyName("subsets")] public List<Subset>? Subsets { get; set; }
    /// <summary>
    /// Criteria used to select the specific set of pods/VMs on which this DestinationRule configuration should be applied.
    /// If specified, the DestinationRule configuration will be applied only to the workload instances matching the workload selector label in the same namespace.
    /// Workload selectors do not apply across namespace boundaries.
    /// If omitted, the DestinationRule falls back to its default behavior.
    /// For example, if specific sidecars need to have egress TLS settings for services outside of the mesh,
    /// instead of every sidecar in the mesh needing to have the configuration (which is the default behaviour),
    /// a workload selector can be specified.
    /// </summary>
    [JsonPropertyName("workloadSelector")] public WorkloadSelector? WorkloadSelector { get; set; }

    /// <summary>
    /// A list of namespaces to which this destination rule is exported.
    /// The resolution of a destination rule to apply to a service occurs in the context of a hierarchy of namespaces.
    /// Exporting a destination rule allows it to be included in the resolution hierarchy for services in other namespaces.
    /// This feature provides a mechanism for service owners and mesh administrators to control the visibility of destination rules across namespace boundaries.
    /// </summary>
    [JsonPropertyName("exportTo")] public List<string>? ExportTo { get; set; }
}
[Serializable]
public class WorkloadSelector
{
    //TODO
}
/// <summary>
/// A subset of endpoints of a service. Subsets can be used for scenarios like A/B testing, or routing to a specific version of a service.
/// Refer to VirtualService documentation for examples of using subsets in these scenarios.
/// In addition, traffic policies defined at the service-level can be overridden at a subset-level.
/// The following rule uses a round robin load balancing policy for all traffic going to a subset named testversion that is composed of endpoints (e.g., pods) with labels (version:v3).
/// </summary>
[Serializable]
public class Subset
{
    // Partial TODO
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("labels")] public Dictionary<string, string> Labels { get; set; }
}