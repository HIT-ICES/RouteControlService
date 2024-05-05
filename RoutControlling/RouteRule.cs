namespace RouteControlService.RoutControlling;

[Serializable] public record K8sResourceId(string Namespace, string Name);

[Serializable] public record EndpointControl(string Uri, bool? UseRegex);

[Serializable]
public record RouteRuleExtraInfo(ushort PortNumber)//, string[] Hosts)
{
    public static RouteRuleExtraInfo Default { get; } = new(80); //, new[] { "*" });
}

public enum TrafficDirection
{
    In,Out
}
[Serializable]
public record RouteRule
(
    string Namespace,
    string DesService,
    string Name,
    K8sResourceId[] SrcPods,
    K8sResourceId[] DesPods,
    EndpointControl[] EndpointControls,
    RouteRuleExtraInfo? ExtraInfo
)
{
    public K8SResourceLabel AsLabel(TrafficDirection direction)
    {
        return new($"route-ctl-{Enum.GetName(direction).ToLower()}--{Namespace}--{DesService}", Name);
    }

    public K8SResourceLabel AsLabel(bool isInbound) => AsLabel(isInbound ? TrafficDirection.In : TrafficDirection.Out);
}

[Serializable] public record RouteRuleId(string Namespace, string DesService, string? Name);