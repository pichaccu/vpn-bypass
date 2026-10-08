using VpnBypass.Core.Rules;
using Xunit;

namespace VpnBypass.Core.Tests;

public class RuleSetTests
{
    [Fact]
    public void Create_derives_name_and_pattern()
    {
        var r = ExclusionRule.Create(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        Assert.Equal("Discord", r.DisplayName);
        Assert.Equal(@"c:\users\x\appdata\local\discord\app-*\discord.exe", r.Pattern);
    }

    [Fact]
    public void Duplicate_pattern_is_rejected()
    {
        var set = new RuleSet();
        Assert.True(set.TryAdd(ExclusionRule.Create(@"C:\a\app-1.0.1\x.exe"), out _));
        Assert.False(set.TryAdd(ExclusionRule.Create(@"C:\a\app-2.0.0\x.exe"), out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Duplicate_pattern_check_ignores_case()
    {
        var set = new RuleSet();
        Assert.True(set.TryAdd(new ExclusionRule("x", @"C:\a\x.exe", @"c:\a\x.exe"), out _));
        Assert.False(set.TryAdd(new ExclusionRule("X", @"C:\A\X.EXE", @"C:\A\X.EXE"), out var err));
        Assert.NotNull(err);
        Assert.Single(set.Rules);
    }

    [Fact]
    public void Rule_limit_is_enforced()
    {
        var set = new RuleSet();
        for (int i = 0; i < RuleSet.MaxRules; i++)
            Assert.True(set.TryAdd(ExclusionRule.Create($@"C:\apps\tool{i}\tool.exe"), out _));

        Assert.False(set.TryAdd(ExclusionRule.Create(@"C:\apps\extra\extra.exe"), out var err));
        Assert.NotNull(err);
        Assert.Equal(RuleSet.MaxRules, set.Rules.Count);
    }

    [Fact]
    public void RemoveByPattern_removes_rule_and_allows_readding()
    {
        var set = new RuleSet();
        var rule = ExclusionRule.Create(@"C:\a\app-1.0.1\x.exe");
        Assert.True(set.TryAdd(rule, out _));

        Assert.True(set.RemoveByPattern(rule.Pattern));
        Assert.Empty(set.Rules);
        Assert.False(set.RemoveByPattern(rule.Pattern)); // már nincs benne
        Assert.True(set.TryAdd(rule, out _));            // újra felvehető
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_pattern_is_rejected(string? pattern)
    {
        var set = new RuleSet();
        Assert.False(set.TryAdd(new ExclusionRule("x", @"C:\a\x.exe", pattern!), out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));
        Assert.Empty(set.Rules);
    }
}
