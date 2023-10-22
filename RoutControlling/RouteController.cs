using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;
using RouteControlService.IstioEntities;
using System.Xml.Linq;

namespace RouteControlService.RoutControlling;

public record struct K8SResourceAddress(string Namespace, string Name);

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
        subsetList.Insert(0, new Subset
        {
            Labels = new()
            {
                { label.Key, label.Value }
            },
            Name = SubsetNameOfLabel(label)
        });
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
    public static V1Patch AddVServicePatch(
        string routeName,
        RouteMatch? routeMatch,
        VirtualService virtualService,
        HostAddress host,
        K8SResourceLabel label)
    {
        var patch = new JsonPatchDocument<VirtualService>();
        var subsetList = virtualService.Spec.Http ?? new();
        subsetList.Insert(0, new HttpRoute
        {
            Match = new()
            {
                new()
                {
                    SourceLabels = new()
                    {
                        {label.Key, label.Value }
                    },
                    Uri=routeMatch?.ToStringMatch()
                }
            },
            Route = new()
            {
                new(){Destination=new()
                {
                    Host=host.Hostname,
                    Port=new PortSelector(){Number=host.Port},
                    Subset=SubsetNameOfLabel(label)
                }}
            },
            Name = routeName
        });
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

public class RouteController
{
    public RouteController(Kubernetes k8s)
    {
        K8S = k8s;
    }


    private Kubernetes K8S { get; }


    /// <summary>
    ///     To label certain pod (service instance).
    ///     As the label finally working with ISTIO route controlling is the label of pod, we don't need to label deployments.
    /// </summary>
    /// <param name="podName"></param>
    /// <param name="key"></param>
    /// <param name="value"></param>
    private void TagInstance(string podName, string key, string value)
    {
    }
}