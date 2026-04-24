using ES.Kubernetes.Reflector.Mirroring;
using ES.Kubernetes.Reflector.Mirroring.Core;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Tests;

public class NamespaceLabelsEqualTests
{
    private static V1Namespace Ns(Dictionary<string, string>? labels) =>
        new()
        {
            Metadata = new V1ObjectMeta { Name = "ns", Labels = labels }
        };

    [Fact]
    public void NullAndEmpty_AreEqual()
    {
        Assert.True(ResourceMirror<V1Secret>.NamespaceLabelsEqual(Ns(null), Ns(new())));
        Assert.True(ResourceMirror<V1Secret>.NamespaceLabelsEqual(Ns(new()), Ns(null)));
        Assert.True(ResourceMirror<V1Secret>.NamespaceLabelsEqual(Ns(null), Ns(null)));
    }

    [Fact]
    public void SameLabels_AreEqual()
    {
        var a = Ns(new() { ["env"] = "prod", ["tier"] = "frontend" });
        var b = Ns(new() { ["env"] = "prod", ["tier"] = "frontend" });
        Assert.True(ResourceMirror<V1Secret>.NamespaceLabelsEqual(a, b));
    }

    [Fact]
    public void ChangedValue_IsNotEqual()
    {
        var a = Ns(new() { ["env"] = "prod" });
        var b = Ns(new() { ["env"] = "staging" });
        Assert.False(ResourceMirror<V1Secret>.NamespaceLabelsEqual(a, b));
    }

    [Fact]
    public void AddedLabel_IsNotEqual()
    {
        var a = Ns(new() { ["env"] = "prod" });
        var b = Ns(new() { ["env"] = "prod", ["tier"] = "frontend" });
        Assert.False(ResourceMirror<V1Secret>.NamespaceLabelsEqual(a, b));
    }

    [Fact]
    public void RemovedLabel_IsNotEqual()
    {
        var a = Ns(new() { ["env"] = "prod", ["tier"] = "frontend" });
        var b = Ns(new() { ["env"] = "prod" });
        Assert.False(ResourceMirror<V1Secret>.NamespaceLabelsEqual(a, b));
    }

    [Fact]
    public void RenamedKey_IsNotEqual()
    {
        var a = Ns(new() { ["env"] = "prod" });
        var b = Ns(new() { ["environment"] = "prod" });
        Assert.False(ResourceMirror<V1Secret>.NamespaceLabelsEqual(a, b));
    }
}
