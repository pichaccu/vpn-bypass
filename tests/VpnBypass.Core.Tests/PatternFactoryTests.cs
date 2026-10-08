using VpnBypass.Core.Rules;
using Xunit;

namespace VpnBypass.Core.Tests;

public class PatternFactoryTests
{
    [Fact]
    public void Discord_squirrel_version_folder_becomes_wildcard()
    {
        var p = PatternFactory.FromPath(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        Assert.Equal(@"c:\users\x\appdata\local\discord\app-*\discord.exe", p);
    }

    [Fact]
    public void Msix_folder_keeps_name_arch_and_publisher()
    {
        var p = PatternFactory.FromPath(@"C:\Program Files\WindowsApps\Claude_2.26454.2.0_x64__abc123\Claude.exe");
        Assert.Equal(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe", p);
    }

    [Fact]
    public void Plain_version_folder_becomes_wildcard()
    {
        var p = PatternFactory.FromPath(@"C:\Apps\Tool\1.4.2\tool.exe");
        Assert.Equal(@"c:\apps\tool\*\tool.exe", p);
    }

    [Fact]
    public void No_version_folder_returns_lowercased_path()
    {
        var p = PatternFactory.FromPath(@"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe");
        Assert.Equal(@"c:\program files\teamspeak 3 client\ts3client_win64.exe", p);
    }

    // --- Negatívok és él-esetek ---

    [Theory]
    [InlineData(@"C:\Apps\Tool\app-beta\tool.exe", @"c:\apps\tool\app-beta\tool.exe")]  // "app-", de nem verziószám
    [InlineData(@"C:\Apps\Tool\v1.2.3\tool.exe", @"c:\apps\tool\v1.2.3\tool.exe")]      // "v" előtag: nem tisztán verziószám
    [InlineData(@"C:\Apps\Tool\1.4.2", @"c:\apps\tool\1.4.2")]                          // az utolsó szegmens (fájlnév) érintetlen
    public void Non_version_names_are_left_unchanged(string path, string expected)
        => Assert.Equal(expected, PatternFactory.FromPath(path));

    [Theory]
    [InlineData(@"C:\Apps\1.2.3\Tool\4.5.6\tool.exe", @"c:\apps\*\tool\*\tool.exe")]    // két sima verziómappa
    [InlineData(@"C:\Apps\app-1.0.1\1.2.3\tool.exe", @"c:\apps\app-*\*\tool.exe")]      // Squirrel + sima verziómappa
    public void Every_version_folder_in_the_path_becomes_a_wildcard(string path, string expected)
        => Assert.Equal(expected, PatternFactory.FromPath(path));

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_2.26454.2.0_x64__abc123\Claude.exe")]
    [InlineData(@"C:\Apps\Tool\1.4.2\tool.exe")]
    [InlineData(@"C:\Apps\1.2.3\Tool\4.5.6\tool.exe")]
    [InlineData(@"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe")]
    public void FromPath_is_idempotent(string path)
    {
        var once = PatternFactory.FromPath(path);
        Assert.Equal(once, PatternFactory.FromPath(once));
    }
}
