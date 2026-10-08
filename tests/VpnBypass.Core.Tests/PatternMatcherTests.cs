using VpnBypass.Core.Rules;
using Xunit;

public class PatternMatcherTests
{
    [Theory]
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9300\Discord.exe", true)]
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9300\Update.exe", false)]
    [InlineData(@"c:\apps\tool\*\tool.exe",
                @"C:\Apps\Tool\1.5.0\sub\tool.exe", false)] // a * egyetlen szegmens
    public void Matches(string pattern, string path, bool expected)
        => Assert.Equal(expected, PatternMatcher.IsMatch(pattern, path));
}
