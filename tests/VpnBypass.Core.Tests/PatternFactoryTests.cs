using VpnBypass.Core.Rules;
using Xunit;

public class PatternFactoryTests
{
    [Fact]
    public void Discord_squirrel_version_folder_becomes_wildcard()
    {
        var p = PatternFactory.FromPath(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe");
        Assert.Equal(@"c:\users\x\appdata\local\discord\app-*\discord.exe", p);
    }
}
