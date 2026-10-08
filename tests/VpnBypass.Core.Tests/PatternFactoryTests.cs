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
}
