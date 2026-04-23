using ES.Kubernetes.Reflector.Mirroring.Core;
using k8s.Models;

namespace ES.Kubernetes.Reflector.Tests;

public class LabelSelectorMatchTests
{
    private static V1Namespace CreateNamespace(string name, Dictionary<string, string>? labels = null) =>
        new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = labels ?? new Dictionary<string, string>()
            }
        };

    [Fact]
    public void EmptySelector_MatchesAll()
    {
        var ns = CreateNamespace("test");
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("", ns));
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("  ", ns));
    }

    [Fact]
    public void EqualitySelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env=production", ns));
    }

    [Fact]
    public void EqualitySelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "staging" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env=production", ns));
    }

    [Fact]
    public void DoubleEqualitySelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env==production", ns));
    }

    [Fact]
    public void InequalitySelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "staging" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env!=production", ns));
    }

    [Fact]
    public void InequalitySelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env!=production", ns));
    }

    [Fact]
    public void InequalitySelector_MissingLabel_Matches()
    {
        var ns = CreateNamespace("test");
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env!=production", ns));
    }

    [Fact]
    public void ExistsSelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env", ns));
    }

    [Fact]
    public void ExistsSelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test");
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env", ns));
    }

    [Fact]
    public void NotExistsSelector_Matches()
    {
        var ns = CreateNamespace("test");
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("!env", ns));
    }

    [Fact]
    public void NotExistsSelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("!env", ns));
    }

    [Fact]
    public void MultipleSelectors_AllMustMatch()
    {
        var ns = CreateNamespace("test",
            new Dictionary<string, string> { { "env", "production" }, { "tier", "frontend" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env=production,tier=frontend", ns));
    }

    [Fact]
    public void MultipleSelectors_OneFails()
    {
        var ns = CreateNamespace("test",
            new Dictionary<string, string> { { "env", "production" }, { "tier", "backend" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env=production,tier=frontend", ns));
    }

    [Fact]
    public void InSelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "staging" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env in (production,staging)", ns));
    }

    [Fact]
    public void InSelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "dev" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env in (production,staging)", ns));
    }

    [Fact]
    public void NotInSelector_Matches()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "dev" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch("env notin (production,staging)", ns));
    }

    [Fact]
    public void NotInSelector_DoesNotMatch()
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch("env notin (production,staging)", ns));
    }

    [Theory]
    [InlineData(",")]
    [InlineData(",,")]
    [InlineData(", ,")]
    public void InvalidSelector_CommasOnly_FailsClosed(string selector)
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch(selector, ns));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("!=value")]
    [InlineData("=value")]
    public void InvalidSelector_EmptyKey_FailsClosed(string selector)
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch(selector, ns));
    }

    [Theory]
    [InlineData("env in (prod")]
    [InlineData("env in prod)")]
    [InlineData("env in ()")]
    [InlineData("()")]
    [InlineData("=")]
    public void InvalidSelector_MalformedExpression_FailsClosed(string selector)
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "production" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch(selector, ns));
    }

    [Fact]
    public void MirroringProperties_AllowedNamespacesSelector_MatchesByLabel()
    {
        var ns = CreateNamespace("my-ns", new Dictionary<string, string> { { "env", "production" } });
        var props = new MirroringProperties
        {
            Allowed = true,
            AllowedNamespacesSelector = "env=production"
        };
        Assert.True(props.CanBeReflectedToNamespace(ns));
    }

    [Fact]
    public void MirroringProperties_AllowedNamespacesSelector_DoesNotMatchByLabel()
    {
        var ns = CreateNamespace("my-ns", new Dictionary<string, string> { { "env", "staging" } });
        var props = new MirroringProperties
        {
            Allowed = true,
            AllowedNamespaces = "other-ns",
            AllowedNamespacesSelector = "env=production"
        };
        Assert.False(props.CanBeReflectedToNamespace(ns));
    }

    [Fact]
    public void MirroringProperties_OrLogic_NameMatchWins()
    {
        var ns = CreateNamespace("my-ns", new Dictionary<string, string> { { "env", "staging" } });
        var props = new MirroringProperties
        {
            Allowed = true,
            AllowedNamespaces = "my-ns",
            AllowedNamespacesSelector = "env=production"
        };
        // Name matches even though label doesn't
        Assert.True(props.CanBeReflectedToNamespace(ns));
    }

    [Fact]
    public void MirroringProperties_OrLogic_LabelMatchWins()
    {
        var ns = CreateNamespace("my-ns", new Dictionary<string, string> { { "env", "production" } });
        var props = new MirroringProperties
        {
            Allowed = true,
            AllowedNamespaces = "other-ns",
            AllowedNamespacesSelector = "env=production"
        };
        // Label matches even though name doesn't
        Assert.True(props.CanBeReflectedToNamespace(ns));
    }

    [Fact]
    public void MirroringProperties_AutoNamespacesSelector()
    {
        var ns = CreateNamespace("my-ns", new Dictionary<string, string> { { "env", "production" } });
        var props = new MirroringProperties
        {
            Allowed = true,
            AutoEnabled = true,
            AutoNamespacesSelector = "env=production"
        };
        Assert.True(props.CanBeAutoReflectedToNamespace(ns));
    }

    [Theory]
    [InlineData("-env=prod")]
    [InlineData("env-=prod")]
    [InlineData(".env=prod")]
    [InlineData("env name=prod")]
    public void InvalidSelector_InvalidLabelKey_FailsClosed(string selector)
    {
        var ns = CreateNamespace("test", new Dictionary<string, string> { { "env", "prod" } });
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch(selector, ns));
    }

    [Fact]
    public void InvalidSelector_LabelKeyTooLong_FailsClosed()
    {
        var longName = new string('a', 64);
        var ns = CreateNamespace("test");
        Assert.False(MirroringPropertiesExtensions.LabelSelectorMatch($"{longName}=value", ns));
    }

    [Fact]
    public void ValidSelector_PrefixedLabelKey_Matches()
    {
        var ns = CreateNamespace("test",
            new Dictionary<string, string> { { "app.kubernetes.io/name", "reflector" } });
        Assert.True(MirroringPropertiesExtensions.LabelSelectorMatch(
            "app.kubernetes.io/name=reflector", ns));
    }

    [Fact]
    public void GetLabelSelectorErrors_ValidSelectors_ReturnsEmpty()
    {
        var props = new MirroringProperties
        {
            AllowedNamespacesSelector = "env=prod",
            AutoNamespacesSelector = "tier in (frontend,backend)"
        };
        Assert.Empty(props.GetLabelSelectorErrors());
    }

    [Fact]
    public void GetLabelSelectorErrors_InvalidSelector_ReturnsErrors()
    {
        var props = new MirroringProperties
        {
            AllowedNamespacesSelector = "=prod",
            AutoNamespacesSelector = "valid=value"
        };
        var errors = props.GetLabelSelectorErrors();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("reflection-allowed-namespaces-selector"));
    }

    [Fact]
    public void GetLabelSelectorErrors_EmptySelectors_ReturnsEmpty()
    {
        var props = new MirroringProperties
        {
            AllowedNamespacesSelector = string.Empty,
            AutoNamespacesSelector = string.Empty
        };
        Assert.Empty(props.GetLabelSelectorErrors());
    }
}
