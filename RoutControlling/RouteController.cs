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

    private static string SubsetNameOfLabel(K8SResourceLabel label) => $"Subset-{label.Key}-{label.Value}";

    public static V1Patch AddSubsetPatch(DestinationRule destinationRule, K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<DestinationRule>();
        var subsetList = destinationRule.Spec.Subsets ?? new();
        subsetList.Insert
        (
            0,
            new Subset
            {
                Labels = new()
                         {
                             { label.Key, label.Value }
                         },
                Name = SubsetNameOfLabel(label)
            }
        );
        patch.Replace(d => d.Spec.Subsets, subsetList);
        return patch.AsV1Patch();
    }

    public static V1Patch RemoveSubsetPatch(DestinationRule destinationRule, K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<DestinationRule>();
        var subsetName = SubsetNameOfLabel(label);
        var target = destinationRule.Spec.Subsets?.FindIndex(s => s.Name == subsetName);
        if (target is { } targetIndex)
            patch.Remove(p => p.Spec.Subsets![targetIndex]);
        return patch.AsV1Patch();
    }

    public static V1Patch AddVServicePatch
    (
        string routeName,
        RouteMatch? routeMatch,
        VirtualService virtualService,
        HostAddress host,
        K8SResourceLabel label
    )
    {
        var patch = new JsonPatchDocument<VirtualService>();
        var subsetList = virtualService.Spec.Http ?? new();
        subsetList.Insert
        (
            0,
            new HttpRoute
            {
                Match = new()
                        {
                            new()
                            {
                                SourceLabels = new()
                                               {
                                                   { label.Key, label.Value }
                                               },
                                Uri = routeMatch?.ToStringMatch()
                            }
                        },
                Route = new()
                        {
                            new()
                            {
                                Destination = new()
                                              {
                                                  Host = host.Hostname,
                                                  Port = new PortSelector() { Number = host.Port },
                                                  Subset = SubsetNameOfLabel(label)
                                              }
                            }
                        },
                Name = routeName
            }
        );
        patch.Replace(d => d.Spec.Http, subsetList);
        return patch.AsV1Patch();
    }

    public static V1Patch RemoveVServicePatch(string routeName, VirtualService virtualService)
    {
        var patch = new JsonPatchDocument<VirtualService>();
        var target = virtualService.Spec.Http?.FindIndex(vs => vs.Name == routeName);
        if (target is { } targetIndex)
            patch.Remove(p => p.Spec.Http![targetIndex]);
        return patch.AsV1Patch();
    }

    public static V1Patch RemoveLabelPatch(K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<V1Pod>();
        patch.Remove(p => p.Metadata.Labels[label.Key]);
        return patch.AsV1Patch();
    }

    public static V1Patch AddLabelPatch(K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<V1Pod>();
        patch.Add(p => p.Metadata.Labels[label.Key], label.Value);
        return patch.AsV1Patch();
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
            await K8S.PatchNamespacedPodAsync(RouteCtl.AddLabelPatch(label), podRef.Name, podRef.Namespace);
        }
        catch (HttpOperationException ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RouteRule[]?> GetAllAsync(string @namespace, string serviceName)
    {
        var pods = await K8S.ListPodForAllNamespacesAsync();
        var srcLabel = $"route-ctl/out--{@namespace}--{serviceName}";
        var desLabel = $"route-ctl/in--{@namespace}--{serviceName}";
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
                r => r.SrcPods.Select(p => TagInstance(p, r.AsLabel(false)))
                      .Concat
                       (
                           r.DesPods.Select(p => TagInstance(p, r.AsLabel(true)))
                       )
            )
        );
    }
}