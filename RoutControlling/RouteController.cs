using System.Collections.Concurrent;
using k8s;
using k8s.Autorest;
using k8s.Models;
using RouteControlService.IstioEntities;

namespace RouteControlService.RoutControlling;

public class RouteController(Kubernetes kubernetes, ILogger<RouteController> logger) : IRouteController
{
    private const string kLabelName = "routectl-name";
    private const string kLabelNs = "routectl-ns";

    private readonly ConcurrentDictionary<KubernetesResourceId, byte> _locks = new();

    /// <inheritdoc />
    public async Task<RouteRule[]?> GetAllAsync(KubernetesResourceId serviceRef)
    {
        var rules = new List<RouteRule>();
        var (@namespace, serviceName) = serviceRef;
        var resName = ResName(serviceName);
        var availablePods = await GetAvailablePods();
        AcquireLock(serviceRef);
        try
        {
            DestinationRule destinationRule;
            VirtualService virtualService;
            try
            {
                if (await kubernetes.GetNamespacedCustomObjectAsync
                    (
                        DestinationRule.GROUP,
                        DestinationRule.VERSION,
                        @namespace,
                        DestinationRule.PLURAL,
                        resName
                    ) is not DestinationRule destinationRuleGot
                 || destinationRuleGot.Spec.Subsets is null
                 || destinationRuleGot.Spec.Host != serviceName
                 || await kubernetes.GetNamespacedCustomObjectAsync
                    (
                        VirtualService.GROUP,
                        VirtualService.VERSION,
                        @namespace,
                        VirtualService.PLURAL,
                        resName
                    ) is not VirtualService virtualServiceGot
                 || virtualServiceGot.Spec.Http is null
                 || virtualServiceGot.Spec.Hosts?.Contains(serviceName) is not true)
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
            }
            catch (Exception e)
            {
                logger.LogWarning("Caught HTTP operation exception, assuming no rule exists: {ex}", e);
                return null;
            }


            foreach (var subset in destinationRule.Spec.Subsets)
                if (subset.Labels.Count != 2
                 || !subset.Labels.TryGetValue(kLabelName, out var name)
                 || name != subset.Name
                 || !subset.Labels.TryGetValue(kLabelNs, out var ns)
                 || @namespace != ns
                 || !availablePods.Contains(new KubernetesResourceId(@namespace, name)))
                    logger.LogWarning
                    (
                        "Found unmatched subset: {ssname} != {{{label}}} in {ns}",
                        subset.Name,
                        string.Join(",", subset.Labels.Select(kv => $"{kv.Key}={kv.Value}")),
                        @namespace
                    );

            foreach (var route in virtualService.Spec.Http)
            {
                var ruleSrc = new List<KubernetesResourceId>();
                var ruleDes = new List<KubernetesResourceId>();
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

                    ruleDes.Add(new KubernetesResourceId(@namespace, ssname));
                    if (destination.Destination.Port is { } port)
                        extraInfo = extraInfo with { PortNumber = (ushort)port.Number };
                }

                foreach (var match in httpMatch)
                {
                    if (match.SourceLabels?.Count != 2
                     || !match.SourceLabels.TryGetValue(kLabelName, out var name)
                     || !match.SourceLabels.TryGetValue(kLabelNs, out var ns))
                    {
                        logger.LogWarning
                        (
                            "Found unsupported route match: {route}",
                            string.Join(",", match.SourceLabels?.Select(kv => $"{kv.Key}={kv.Value}") ?? [])
                        );
                        continue;
                    }

                    ruleSrc.Add(new KubernetesResourceId(ns, name));
                    if (match.Uri is { } uriMatch)
                        epCtl.Add
                        (
                            new EndpointControl
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

                rules.Add
                (
                    new RouteRule
                    (
                        @namespace,
                        serviceName,
                        ruleName,
                        ruleSrc.ToArray(),
                        ruleDes.ToArray(),
                        epCtl.ToArray(),
                        extraInfo
                    )
                );
            }


            return rules.ToArray();
        }
        finally
        {
            ReleaseLock(serviceRef);
        }
    }

    /// <inheritdoc />
    public async Task UpdateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules)
    {
        var (@namespace, serviceName) = serviceRef;
        var resName = ResName(serviceName);
        if (newRules is [])
        {
            try
            {
                await kubernetes.DeleteNamespacedCustomObjectAsync
                (
                    DestinationRule.GROUP,
                    DestinationRule.VERSION,
                    @namespace,
                    DestinationRule.PLURAL,
                    resName
                );
                await kubernetes.DeleteNamespacedCustomObjectAsync
                (
                    VirtualService.GROUP,
                    VirtualService.VERSION,
                    @namespace,
                    VirtualService.PLURAL,
                    resName
                );
            }
            catch (HttpOperationException ex)
            {
                throw new RouteControllingException("Failed to delete resources", ex);
            }

            return;
        }

        await CheckRules(newRules);

        var (destRule, vService) = RulesToResources(serviceName, newRules);
        try
        {
            await kubernetes.ReplaceNamespacedCustomObjectAsync
            (
                destRule,
                DestinationRule.GROUP,
                DestinationRule.VERSION,
                @namespace,
                DestinationRule.PLURAL,
                resName
            );
            await kubernetes.ReplaceNamespacedCustomObjectAsync
            (
                vService,
                VirtualService.GROUP,
                VirtualService.VERSION,
                @namespace,
                VirtualService.PLURAL,
                resName
            );
        }
        catch (HttpOperationException ex)
        {
            throw new RouteControllingException("Failed to update resources", ex);
        }
    }

    /// <inheritdoc />
    public async Task CreateAllAsync(KubernetesResourceId serviceRef, RouteRule[] newRules)
    {
        var (@namespace, serviceName) = serviceRef;
        await CheckRules(newRules);
        var (destRule, vService) = RulesToResources(serviceName, newRules);
        try
        {
            await kubernetes.CreateNamespacedCustomObjectAsync
            (
                destRule,
                DestinationRule.GROUP,
                DestinationRule.VERSION,
                @namespace,
                DestinationRule.PLURAL
            );
            await kubernetes.CreateNamespacedCustomObjectAsync
            (
                vService,
                VirtualService.GROUP,
                VirtualService.VERSION,
                @namespace,
                VirtualService.PLURAL
            );
        }
        catch (HttpOperationException ex)
        {
            throw new RouteControllingException("Failed to create resources", ex);
        }
    }

    //private string DestinationRuleName(string serviceName) => $"routectl-{serviceName}";
    private string ResName(string serviceName) { return $"routectl-{serviceName}"; }

    public static V1Patch HandleLabelPatch(KubernetesResourceId id)
    {
        var patchJson =
            $$"""
              [
                  {
                    "op": "replace",
                    "path": "/metadata/labels/{{kLabelNs}}",
                    "value": "{{id.Namespace}}"
                  },
                  {
                    "op": "replace",
                    "path": "/metadata/labels/{{kLabelName}}",
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
    private async Task<bool> AutoFixLabels(KubernetesResourceId podRef)
    {
        try
        {
            var patch = HandleLabelPatch(podRef);
            await kubernetes.PatchNamespacedPodAsync(patch, podRef.Name, podRef.Namespace);
            logger.LogInformation("Fixed handle label for pod {pod}", podRef);
            return true;
        }
        catch (HttpOperationException)
        {
            logger.LogWarning("Failed to fix handle label for pod {pod}", podRef);
            return false;
        }
    }

    protected void ReleaseLock(KubernetesResourceId serviceRef) { _locks.TryRemove(serviceRef, out _); }

    protected void AcquireLock(KubernetesResourceId serviceRef)
    {
        if (!_locks.TryAdd(serviceRef, new byte()))
            throw new RouteControllingException
                ("Resource is under processing", RouteControllingExceptionType.ConcurrencyConflict);
    }

    protected async Task<HashSet<KubernetesResourceId>> GetAvailablePods(bool runFix = false)
    {
        HashSet<KubernetesResourceId> availablePods = new();
        try
        {
            string? ns = null, name = null;
            if (await kubernetes.ListPodForAllNamespacesAsync() is not { } pods)
                throw new RouteControllingException
                    ("Failed to get available pods", RouteControllingExceptionType.BadPodLabels);

            foreach (var item in pods.Items)
            {
                if (!item.Metadata.Labels.TryGetValue(kLabelName, out name)
                 || !item.Metadata.Labels.TryGetValue(kLabelNs, out ns)) continue;
                var podRef = new KubernetesResourceId(item.Namespace(), item.Name());
                if (item.Name() != name || item.Namespace() != ns)
                {
                    if (runFix)
                    {
                        logger.LogWarning
                        (
                            "Found Pod without/withbad bad label: {pod}@{podns} != {label}@{label_ns}, fixing ...",
                            item.Name(),
                            item.Namespace(),
                            name,
                            ns
                        );
                        if (!await AutoFixLabels(podRef)) continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                availablePods.Add(podRef);
            }
        }
        catch (HttpOperationException e)
        {
            throw new RouteControllingException("Caught HTTP operation exception when listing all pods.", e);
        }

        return availablePods;
    }

    private async Task CheckRules(RouteRule[] newRules, HashSet<KubernetesResourceId>? availablePods = null)
    {
        availablePods ??= await GetAvailablePods(true);
        foreach (var pod in newRules.SelectMany(r => r.SrcPods.Concat(r.DesPods)))
        {
            if (availablePods.Contains(pod)) continue;
            logger.LogError
            (
                "Pod referred in rules not available for route controlling: {pod}@{podns}.",
                pod.Name,
                pod.Namespace
            );
            throw new RouteControllingException
            (
                "Pod referred in rules not available for route controlling",
                RouteControllingExceptionType.UnmanagedPods
            );
        }
    }

    private (DestinationRule destRule, VirtualService vService) RulesToResources
        (string serviceName, RouteRule[] newRules)
    {
        var resName = ResName(serviceName);

        // Step 1: Create DesRule
        var destRule =
            new DestinationRule
            {
                Metadata =
                {
                    Name = resName
                },
                Spec =
                {
                    Host = serviceName,
                    Subsets =
                    [
                        ..
                        newRules.SelectMany
                        (
                            r =>
                                r.DesPods.Select
                                (
                                    des => new Subset
                                           {
                                               Name = des.Name,
                                               Labels = new Dictionary<string, string>
                                                        {
                                                            { kLabelName, des.Name },
                                                            { kLabelNs, des.Namespace }
                                                        }
                                           }
                                )
                        )
                    ]
                }
            };


        // Step 2: Create VService
        var vService = new VirtualService
                       {
                           Metadata =
                           {
                               Name = resName
                           },
                           Spec =
                           {
                               Hosts = [serviceName],
                               Http =
                               [
                                   ..
                                   newRules.SelectMany
                                   (
                                       r => r.SrcPods.Select
                                       (
                                           src => new HttpRoute
                                                  {
                                                      Name = r.Name,
                                                      Route =
                                                      [
                                                          ..
                                                          r.DesPods.Select
                                                          (
                                                              des =>
                                                                  new HttpRouteDestination
                                                                  {
                                                                      Destination =
                                                                          new Destination
                                                                          {
                                                                              Host = serviceName,
                                                                              Port = new PortSelector
                                                                                  {
                                                                                      Number =
                                                                                          r.ExtraInfo
                                                                                            ?.PortNumber
                                                                                       ?? 80
                                                                                  },
                                                                              Subset = des.Name
                                                                          }
                                                                  }
                                                          )
                                                      ],
                                                      Match =
                                                      [
                                                          .. (r.EndpointControls is null or []
                                                                  ? [null]
                                                                  : r.EndpointControls.Cast<EndpointControl?>())
                                                         .SelectMany
                                                          (
                                                              epc => r.SrcPods.Select
                                                              (
                                                                  src => new HTTPMatchRequest
                                                                         {
                                                                             Name = r.Name,
                                                                             SourceLabels =
                                                                                 new Dictionary<string, string>
                                                                                 {
                                                                                     { kLabelName, src.Name },
                                                                                     { kLabelNs, src.Namespace }
                                                                                 },
                                                                             Uri = epc is null
                                                                                 ? null
                                                                                 : new StringMatch
                                                                                     {
                                                                                         Value = epc.Uri,
                                                                                         Type = epc.UseRegex switch
                                                                                             {
                                                                                                 true =>
                                                                                                     StringMatchType
                                                                                                        .Regex,
                                                                                                 false =>
                                                                                                     StringMatchType
                                                                                                        .Exact,
                                                                                                 null =>
                                                                                                     StringMatchType
                                                                                                        .Prefix
                                                                                             }
                                                                                     }
                                                                         }
                                                              )
                                                          )
                                                      ]
                                                  }
                                       )
                                   )
                               ]
                           }
                       };
        logger.LogInformation("Successfully Created Resources: {desrule}, {vsvc}", destRule, vService);
        return (destRule, vService);
    }
}