using VpnBypass.Core.Rules;
using Xunit;

namespace VpnBypass.Core.Tests;

// Memóriabeli fájlrendszer: kis-/nagybetű-független, mint a Windows.
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem(params string[] files)
    {
        foreach (var f in files) _files.Add(f);
    }

    public bool FileExists(string path) => _files.Contains(path);

    public IEnumerable<string> GetSubdirectoryNames(string path)
    {
        var prefix = path.TrimEnd('\\') + "\\";
        return _files
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f => f[prefix.Length..])
            .Where(rest => rest.Contains('\\'))
            .Select(rest => rest[..rest.IndexOf('\\')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public class RuleResolverTests
{
    const string Local = @"C:\Users\x\AppData\Local\Discord";

    [Fact]
    public void Discord_pattern_resolves_every_installed_version()
    {
        var fs = new FakeFileSystem(
            $@"{Local}\app-1.0.9260\Discord.exe",
            $@"{Local}\app-1.0.9300\Discord.exe",
            $@"{Local}\Update.exe");
        var pattern = PatternFactory.FromPath($@"{Local}\app-1.0.9260\Discord.exe");

        var paths = RuleResolver.Resolve(pattern, fs);

        Assert.Equal(2, paths.Count);
        Assert.Contains(paths, p => p.EndsWith(@"app-1.0.9300\discord.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Squirrel_launcher_is_added()
    {
        var fs = new FakeFileSystem($@"{Local}\app-1.0.9260\Discord.exe", $@"{Local}\Update.exe");
        var pattern = PatternFactory.FromPath($@"{Local}\app-1.0.9260\Discord.exe");

        var paths = RuleResolver.ResolveWithLauncher(pattern, fs);

        Assert.Equal(2, paths.Count);
        Assert.Contains(paths, p => p.EndsWith(@"discord\update.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Msix_pattern_matches_only_the_same_publisher()
    {
        const string apps = @"C:\Program Files\WindowsApps";
        var fs = new FakeFileSystem(
            $@"{apps}\Claude_2.30.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe",
            $@"{apps}\Claude_2.30.0.0_x64__otherpublish\app\Claude.exe");
        var pattern = PatternFactory.FromPath($@"{apps}\Claude_2.26454.2.0_x64__pzs8sxrjxfjjc\app\Claude.exe");

        var paths = RuleResolver.Resolve(pattern, fs);

        Assert.Single(paths);
        Assert.Contains("pzs8sxrjxfjjc", paths[0]);
    }

    [Fact]
    public void Exact_path_resolves_to_itself_when_it_exists()
    {
        const string ts = @"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe";
        var paths = RuleResolver.Resolve(PatternFactory.FromPath(ts), new FakeFileSystem(ts));
        Assert.Single(paths);
    }

    [Fact]
    public void Missing_program_resolves_to_nothing()
    {
        var pattern = PatternFactory.FromPath($@"{Local}\app-1.0.9260\Discord.exe");
        Assert.Empty(RuleResolver.ResolveWithLauncher(pattern, new FakeFileSystem()));
    }
}
