using k8s;
using k8s.Models;
using Microsoft.AspNetCore.JsonPatch;

namespace RouteControlService;

public record struct K8SResourceAddress(string Namespace, string Name);

public record struct K8SResourceLabel(string Key, string Value);

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