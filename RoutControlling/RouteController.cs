using System.Text.Json;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using RouteControlService.IstioEntities;

namespace RouteControlService.RoutControlling;

public record struct K8SResourceLabel(string Key, string Value);

public record struct HostAddress(string Hostname, uint Port);

public record struct RouteMatch(StringMatchType Type, string Value)
{
    public StringMatch ToStringMatch() => new() { { Enum.GetName(Type)?.ToLower() ?? "", Value } };
}

internal static class RouteCtl { }

public class RouteController(Kubernetes K8S, ILogger<RouteController> logger) : IRouteController
{
    private const string LABEL_NAME = "routectl-name";
    private const string LABEL_NS = "routectl-ns";

    //private string DestinationRuleName(string serviceName) => $"routectl-{serviceName}";
    private string ResName(string serviceName) => $"routectl-{serviceName}";

    public static V1Patch HandleLabelPatch(K8sResourceId id)
    {
        var patchJson =
            $$"""
              [
                  {
                    "op": "add",
                    "path": "/metadata/labels/{{LABEL_NS}}",
                    "value": "{{id.Namespace}}"
                  },
                  {
                    "op": "replace",
                    "path": "/metadata/labels/{{LABEL_NAME}}",
                    "value": "{{id.Name}}"
                  }
              ]
              """;
        var r = new V1Patch(patchJson, V1Patch.PatchType.JsonPatch);
        return r;
    }

    /// <summary>
    ///     To label certain pod (service instance).
    ///     As the label finally working with ISTIO route controlling is the label of pod, we don't need to label deployments.
    /// </summary>
    /// <param name="podRef"></param>
    private async Task TagInstance(K8sResourceId podRef)
    {
        try
        {
            var patch = HandleLabelPatch(podRef);
            await K8S.PatchNamespacedPodAsync(patch, podRef.Name, podRef.Namespace);
            logger.LogInformation("Updated handle label for pod {pod}", podRef);
        }
        catch (HttpOperationException ex)
        {
            logger.LogWarning("Failed to update handle label for pod {pod}", podRef);
        }
    }

    /// <inheritdoc />
    public async Task<RouteRule[]?> GetAllAsync(string @namespace, string serviceName)
    {
        var rules = new List<RouteRule>();

        var resName = ResName(serviceName);
        HashSet<string> availablePods = new();
        DestinationRule destinationRule;
        VirtualService virtualService;
        try
        {
            if (await K8S.GetNamespacedCustomObjectAsync
                (
                    DestinationRule.GROUP,
                    DestinationRule.VERSION,
                    @namespace,
                    DestinationRule.PLURAL,
                    resName
                ) is not DestinationRule destinationRuleGot
             || destinationRuleGot.Spec.Subsets is null
             || destinationRuleGot.Spec.Host != serviceName
             || await K8S.GetNamespacedCustomObjectAsync
                (
                    VirtualService.GROUP,
                    VirtualService.VERSION,
                    @namespace,
                    VirtualService.PLURAL,
                    serviceName
                ) is not VirtualService virtualServiceGot
             || virtualServiceGot.Spec.Http is null
             || virtualServiceGot.Spec.Hosts?.Contains(serviceName) is not true
             || await K8S.ListNamespacedPodAsync(@namespace) is not { } pods)
            {
                logger.LogWarning
                (
                    "Failed to get some of Routectl Resouces for {ns}.{sn}, assuming not exist.",
                    @namespace,
                    serviceName
                );
                return null;
            }

            virtualService = virtualServiceGot;
            destinationRule = destinationRuleGot;
            foreach (var item in pods.Items)
            {
                if (!item.Metadata.Labels.TryGetValue(LABEL_NAME, out var name)
                 || item.Name() != name
                 || !item.Metadata.Labels.TryGetValue(LABEL_NS, out var ns)
                 || @namespace != ns)
                {
                    logger.LogWarning
                    (
                        "Found Pod without/withbad label: {pod}, {label}@{label_ns} in {ns}",
                        item.Name(),
                        name,
                        ns,
                        @namespace
                    );
                }
                else
                {
                    availablePods.Add(name);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Caught HTTP operation exception, assuming no rule exists: {ex}", e);
            return null;
        }

        foreach (var subset in destinationRule.Spec.Subsets)
        {
            if (subset.Labels.Count != 2
             || !subset.Labels.TryGetValue(LABEL_NAME, out var name)
             || name != subset.Name
             || !subset.Labels.TryGetValue(LABEL_NS, out var ns)
             || @namespace != ns
             || !availablePods.Contains(name))
            {
                logger.LogWarning
                (
                    "Found unmatched subset: {ssname} != {{{label}}} in {ns}",
                    subset.Name,
                    string.Join(",", subset.Labels.Select(kv => $"{kv.Key}={kv.Value}")),
                    @namespace
                );
            }
        }

        foreach (var route in virtualService.Spec.Http)
        {
            var ruleSrc = new List<K8sResourceId>();
            var ruleDes = new List<K8sResourceId>();
            var epCtl = new List<EndpointControl>();
            var extraInfo = new RouteRuleExtraInfo(80);
            if (route.Match is not { } httpMatch
             || route.Route is not { } destinations
             || route.Name is not { } ruleName)
            {
                logger.LogWarning("Found unsupported route match: {route} in {ns}", route.Match, @namespace);
                continue;
            }

            foreach (var destination in destinations)
            {
                if (destination.Destination.Subset is not { } ssname || destination.Destination.Host != serviceName)
                {
                    logger.LogWarning
                    (
                        "Found unexcepted destination: {route}({ssname}) != {sn} in {ns}",
                        destination.Destination,
                        destination.Destination.Subset,
                        serviceName,
                        @namespace
                    );
                    continue;
                }

                ruleDes.Add(new(@namespace, ssname));
                if (destination.Destination.Port is { } port)
                    extraInfo = extraInfo with { PortNumber = (ushort)port.Number };
            }

            foreach (var match in httpMatch)
            {
                if (match.SourceLabels?.Count != 2
                 || !match.SourceLabels.TryGetValue(LABEL_NAME, out var name)
                 || !match.SourceLabels.TryGetValue(LABEL_NS, out var ns))
                {
                    logger.LogWarning
                    (
                        "Found unsupported route match: {route}",
                        string.Join(",", match.SourceLabels?.Select(kv => $"{kv.Key}={kv.Value}") ?? [])
                    );
                    continue;
                }

                ruleSrc.Add(new(ns, name));
                if (match.Uri is { } uriMatch)
                {
                    epCtl.Add
                    (
                        new
                        (
                            uriMatch.Value,
                            uriMatch.Type switch
                            {
                                StringMatchType.Exact => false,
                                StringMatchType.Prefix => null,
                                StringMatchType.Regex => true,
                                _ => throw new ArgumentOutOfRangeException()
                            }
                        )
                    );
                }
            }

            rules.Add
            (
                new(@namespace, serviceName, ruleName, ruleSrc.ToArray(), ruleDes.ToArray(), epCtl.ToArray(), extraInfo)
            );
        }


        return rules.ToArray();
    }

    /// <inheritdoc />
    public Task UpdateAllAsync(string @namespace, string serviceName, RouteRule[] newRules)
    {
        throw new NotImplementedException();
        //K8S.ReplaceClusterCustomObjectAsync();
    }

    /// <inheritdoc />
    public async Task CreateAllAsync(string @namespace, string serviceName, RouteRule[] newRules)
    {
        // Step 1: Create DesRule
        var destRule = new DestinationRule
                       {
                           Metadata =
                           {
                               Name = serviceName
                           }
                       };
        destRule.Spec.Host = serviceName;
        destRule.Spec.Subsets =
            newRules.Select
                     (
                         r => new Subset
                              {
                                  Name = r.Name,
                                  Labels = new() { { r.AsLabel(true).Key, r.Name } }
                              }
                     )
                    .ToList();
        try
        {
            await K8S.CreateNamespacedCustomObjectAsync
            (
                destRule,
                DestinationRule.GROUP,
                DestinationRule.VERSION,
                @namespace,
                DestinationRule.PLURAL
            );
        }
        catch (HttpOperationException ex)
        {
            Console.WriteLine(ex);
            throw;
        }

        // Step 2: Create VService
        var vService = new VirtualService();
        vService.Spec.Hosts = [serviceName];
        vService.Metadata.Name = serviceName;
        vService.Spec.Http =
            newRules.Select
                     (
                         r => new HttpRoute
                              {
                                  Name = r.Name,
                                  Route = r.ExtraInfo?.Hosts is not null or []
                                              ? r.ExtraInfo.Hosts.Select
                                                  (
                                                      h =>
                                                          new HttpRouteDestination()
                                                          {
                                                              Destination = new Destination()
                                                                            {
                                                                                Host = h,
                                                                                Port = new PortSelector()
                                                                                    {
                                                                                        Number = r.ExtraInfo?.PortNumber
                                                                                         ?? 80
                                                                                    },
                                                                                Subset = r.Name
                                                                            }
                                                          }
                                                  )
                                                 .ToList()
                                              :
                                              [
                                                  new HttpRouteDestination()
                                                  {
                                                      Destination = new Destination()
                                                                    {
                                                                        Host = serviceName,
                                                                        Port = new PortSelector()
                                                                               {
                                                                                   Number = r.ExtraInfo?.PortNumber
                                                                                    ?? 80
                                                                               },
                                                                        Subset = r.Name
                                                                    }
                                                  }
                                              ],
                                  Match = r.EndpointControls.Length != 0
                                              ?
                                              [
                                                  .. r.EndpointControls.Select
                                                  (
                                                      ep => new HTTPMatchRequest()
                                                            {
                                                                Name = r.Name,
                                                                SourceLabels =
                                                                    new() { { r.AsLabel(false).Key, r.Name } },
                                                                Uri = new()
                                                                      {
                                                                          Value = ep.Uri,
                                                                          Type = ep.UseRegex switch
                                                                              {
                                                                                  true => StringMatchType.Regex,
                                                                                  false => StringMatchType.Exact,
                                                                                  null => StringMatchType.Prefix
                                                                              }
                                                                      }
                                                            }
                                                  )
                                              ]
                                              :
                                              [
                                                  new HTTPMatchRequest
                                                  {
                                                      Name = r.Name,
                                                      SourceLabels = new() { { r.AsLabel(false).Key, r.Name } },
                                                  }
                                              ]
                              }
                     )
                    .ToList();
        try
        {
            await K8S.CreateNamespacedCustomObjectAsync
                (vService, VirtualService.GROUP, VirtualService.VERSION, @namespace, VirtualService.PLURAL);
        }
        catch (HttpOperationException e)
        {
            Console.WriteLine(e);
            throw;
        }

        // Step 3: Tag pods

        await Task.WhenAll
        (
            newRules.SelectMany
            (
                r => r.SrcPods.Select(p => TagInstance(p, r.AsLabel(TrafficDirection.Out)))
                      .Concat
                       (
                           r.DesPods.Select(p => TagInstance(p, r.AsLabel(TrafficDirection.In)))
                       )
            )
        );
    }
}