using ES.Kubernetes.Reflector.Watchers.Core;

namespace ES.Kubernetes.Reflector.Tests.Unit;

public class GlobMatcherTests
{
    // ParseGlobPatterns

    [Fact]
    public void ParseGlobPatterns_NullInput_ReturnsEmpty()
    {
        var result = GlobMatcher.ParseGlobPatterns(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGlobPatterns_EmptyString_ReturnsEmpty()
    {
        var result = GlobMatcher.ParseGlobPatterns("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGlobPatterns_WhitespaceOnly_ReturnsEmpty()
    {
        var result = GlobMatcher.ParseGlobPatterns("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseGlobPatterns_SinglePattern_ReturnsSingleRegex()
    {
        var result = GlobMatcher.ParseGlobPatterns("kube-system");
        Assert.Single(result);
    }

    [Fact]
    public void ParseGlobPatterns_MultiplePatterns_ReturnsMultipleRegexes()
    {
        var result = GlobMatcher.ParseGlobPatterns("kube-system,kube-public,default");
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void ParseGlobPatterns_PatternWithWhitespace_TrimsEntries()
    {
        var result = GlobMatcher.ParseGlobPatterns(" kube-system , kube-public ");
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void ParseGlobPatterns_EmptyEntriesInList_IgnoresEmpty()
    {
        var result = GlobMatcher.ParseGlobPatterns("kube-system,,kube-public");
        Assert.Equal(2, result.Length);
    }

    // IsNamespaceExcluded — empty patterns

    [Fact]
    public void IsNamespaceExcluded_EmptyPatterns_ReturnsFalse()
    {
        Assert.False(GlobMatcher.IsNamespaceExcluded("kube-system", []));
    }

    [Fact]
    public void IsNamespaceExcluded_NullNamespace_ReturnsFalse()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("kube-*");
        Assert.False(GlobMatcher.IsNamespaceExcluded(null, patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_EmptyNamespace_ReturnsFalse()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("kube-*");
        Assert.False(GlobMatcher.IsNamespaceExcluded("", patterns));
    }

    // IsNamespaceExcluded — exact match

    [Fact]
    public void IsNamespaceExcluded_ExactMatch_ReturnsTrue()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("kube-system");
        Assert.True(GlobMatcher.IsNamespaceExcluded("kube-system", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_NoMatch_ReturnsFalse()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("kube-system");
        Assert.False(GlobMatcher.IsNamespaceExcluded("default", patterns));
    }

    // IsNamespaceExcluded — star wildcard

    [Fact]
    public void IsNamespaceExcluded_StarWildcard_MatchesPrefix()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ephie-*");
        Assert.True(GlobMatcher.IsNamespaceExcluded("ephie-pr-123", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_StarAlone_MatchesAnyNamespace()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("*");
        Assert.True(GlobMatcher.IsNamespaceExcluded("any-namespace", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_StarWildcard_DoesNotMatchDifferentPrefix()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ephie-*");
        Assert.False(GlobMatcher.IsNamespaceExcluded("prod-namespace", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_StarWildcard_MatchesSuffix()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("*-temp");
        Assert.True(GlobMatcher.IsNamespaceExcluded("feature-temp", patterns));
        Assert.False(GlobMatcher.IsNamespaceExcluded("feature-prod", patterns));
    }

    // IsNamespaceExcluded — question mark wildcard

    [Fact]
    public void IsNamespaceExcluded_QuestionMarkWildcard_MatchesSingleChar()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ns-?");
        Assert.True(GlobMatcher.IsNamespaceExcluded("ns-a", patterns));
        Assert.True(GlobMatcher.IsNamespaceExcluded("ns-1", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_QuestionMarkWildcard_DoesNotMatchMultipleChars()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ns-?");
        Assert.False(GlobMatcher.IsNamespaceExcluded("ns-ab", patterns));
    }

    // IsNamespaceExcluded — multiple patterns

    [Fact]
    public void IsNamespaceExcluded_MultiplePatterns_MatchesAny()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("kube-system,kube-public");
        Assert.True(GlobMatcher.IsNamespaceExcluded("kube-system", patterns));
        Assert.True(GlobMatcher.IsNamespaceExcluded("kube-public", patterns));
        Assert.False(GlobMatcher.IsNamespaceExcluded("default", patterns));
    }

    // IsNamespaceExcluded — regex metacharacters in namespace names

    [Fact]
    public void IsNamespaceExcluded_NamespaceWithDot_MatchesLiterally()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ns.special");
        Assert.True(GlobMatcher.IsNamespaceExcluded("ns.special", patterns));
        Assert.False(GlobMatcher.IsNamespaceExcluded("nsXspecial", patterns));
    }

    [Fact]
    public void IsNamespaceExcluded_PatternWithBrackets_MatchesLiterally()
    {
        var patterns = GlobMatcher.ParseGlobPatterns("ns[1]");
        Assert.True(GlobMatcher.IsNamespaceExcluded("ns[1]", patterns));
        Assert.False(GlobMatcher.IsNamespaceExcluded("ns1", patterns));
    }
}
