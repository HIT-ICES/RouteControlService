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

internal static class RouteCtl
{
    private static V1Patch AsV1Patch<T>(this JsonPatchDocument<T> patch) where T : class
    {
        return new(patch, V1Patch.PatchType.JsonPatch);
    }

    public static V1Patch RemoveLabelPatch(K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<V1Pod>();
        patch.Remove(p => p.Metadata.Labels[label.Key]);
        var r = patch.AsV1Patch();
        return r;
    }

    public static V1Patch AddLabelPatch(K8SResourceLabel label)
    {
        // var patch = new JsonPatchDocument();
        // patch.Add($"/metadata/labels/{label.Key}", label.Value);
        var patchJson =
            $$"""
              [{
                "op": "add",
                "path": "/metadata/labels/{{label.Key}}",
                "value": "{{label.Value}}"
              }]
              """;
        var r = new V1Patch(patchJson, V1Patch.PatchType.JsonPatch);
        return r;
    }
}

public class RouteController(Kubernetes K8S, ILogger<RouteController> logger) : IRouteController
{
    /// <summary>
    ///     To label certain pod (service instance).
    ///     As the label finally working with ISTIO route controlling is the label of pod, we don't need to label deployments.
    /// </summary>
    /// <param name="podRef"></param>
    /// <param name="label"></param>
    private async Task TagInstance(K8sResourceId podRef, K8SResourceLabel label)
    {
        try
        {
            var patch = RouteCtl.AddLabelPatch(label);
            await K8S.PatchNamespacedPodAsync(patch, podRef.Name, podRef.Namespace);
            logger.LogInformation("Add label {label} to pod {pod}", label, podRef);
        }
        catch (HttpOperationException ex)
        {
            var serialized = JsonSerializer.Serialize(RouteCtl.AddLabelPatch(label));
            ;
            Console.WriteLine(serialized);
            Console.WriteLine(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RouteRule[]?> GetAllAsync(string @namespace, string serviceName)
    {
        var pods = await K8S.ListPodForAllNamespacesAsync();
        var srcLabel = $"route-ctl-out--{@namespace}--{serviceName}";
        var desLabel = $"route-ctl-in--{@namespace}--{serviceName}";
        var rules = new Dictionary<string, (List<K8sResourceId>, List<K8sResourceId>, List<EndpointControl>)>();

        foreach (var pod in pods)
        {
            var podId = new K8sResourceId(pod.Namespace(), pod.Name());
            if (pod.GetLabel(srcLabel) is { } outDef)
            {
                if (rules.TryGetValue(outDef, out var rule))
                {
                    rule.Item1.Add(podId);
                }
                else
                {
                    rules[outDef] = ([podId], [], []);
                }
            }

            if (pod.GetLabel(desLabel) is { } inDef)
            {
                if (rules.ContainsKey(inDef))
                {
                    rules[inDef].Item2.Add(podId);
                }
                else
                {
                    rules[inDef] = ([], [podId], []);
                }
            }
        }

        try
        {
            if (await K8S.GetNamespacedCustomObjectAsync
                (
                    VirtualService.GROUP,
                    VirtualService.VERSION,
                    @namespace,
                    VirtualService.PLURAL,
                    serviceName
                ) is not VirtualService vservice
             || vservice.Spec.Http is null)
            {
                logger.LogWarning
                (
                    "Failed to get Virtual Service Http routes for {ns}.{sn}, assuming not exist.",
                    @namespace,
                    serviceName
                );
                return [];
            }

            foreach (var route in vservice.Spec.Http)
            {
                if (route.Match is not { } httpMatch
                 || rules.TryGetValue(route.Name ?? "", out var rule)) continue;
                foreach (var match in httpMatch)
                {
                    if (match.Uri is not { } uriMatch) continue;
                    rule.Item3.Add
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
        }
        catch (HttpOperationException httpExp)
        {
            logger.LogWarning("Caught HTTP operation exception, assuming no rule exists: {ex}", httpExp);
            return null;
        }


        return rules.Select
                     (
                         kv => new RouteRule
                         (
                             @namespace,
                             serviceName,
                             kv.Key,
                             kv.Value.Item1.ToArray(),
                             kv.Value.Item2.ToArray(),
                             kv.Value.Item3.ToArray(),
                             RouteRuleExtraInfo.Default
                         )
                     )
                    .ToArray();
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