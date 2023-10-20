namespace RouteControlService;

[Serializable]
public record K8sResourceId(string Namespace, string Name);
[Serializable]
public record EndpointControl(string Uri, bool? UseRegex);
[Serializable]
public record RouteRuleExtraInfo(ushort PortNumber, string[] Hosts)
{
    public static RouteRuleExtraInfo Default { get; } = new(80, new[] { "*" });
}
[Serializable]
public record RouteRule(string Namespace, string DesService, string Name,
    K8sResourceId[] SrcPods, K8sResourceId[] DesPods, EndpointControl[] EndpointControls,
    RouteRuleExtraInfo? ExtraInfo);

[Serializable]
public record RouteRuleId(string Namespace, string DesService, string? Name);