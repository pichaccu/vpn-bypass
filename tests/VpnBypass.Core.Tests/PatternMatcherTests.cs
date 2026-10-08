using VpnBypass.Core.Rules;
using Xunit;

namespace VpnBypass.Core.Tests;

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

    // --- A modul szerződése (spec §11): valós alakok ---

    [Theory]
    // Squirrel: a Discord új verziója is illeszkedik
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9999\Discord.exe", true)]
    // MSIX: a verzió bármi lehet, de a név, az architektúra és a publisher fix
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Claude_2.27.0.0_x64__abc123\Claude.exe", true)]
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Claude_2.27.0.0_x64__zzz\Claude.exe", false)]
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Other_2.27.0.0_x64__abc123\Claude.exe", false)]  // más csomagnév, azonos publisher
    // önálló "*" szegmens: pontosan egy mappa (se több, se kevesebb)
    [InlineData(@"c:\apps\tool\*\tool.exe",
                @"C:\Apps\Tool\1.5.0\tool.exe", true)]
    [InlineData(@"c:\apps\tool\*\tool.exe",
                @"C:\Apps\Tool\tool.exe", false)]
    [InlineData(@"c:\apps\tool\*\tool.exe",
                @"C:\Apps\Tool\1.5.0\tool.exe\extra", false)]  // a minta a teljes útvonalra illeszkedik, nem csak az elejére
    // pontos, wildcard nélküli egyezés
    [InlineData(@"c:\program files\teamspeak 3 client\ts3client_win64.exe",
                @"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe", true)]
    [InlineData(@"c:\program files\teamspeak 3 client\ts3client_win64.exe",
                @"C:\Program Files\TeamSpeak 3 Client\ts3client_win32.exe", false)]
    public void Matches_real_world_shapes(string pattern, string path, bool expected)
        => Assert.Equal(expected, PatternMatcher.IsMatch(pattern, path));

    // Szegmensen belüli wildcard: a "*" legalább 1 karakterre illeszkedik, a prefix és a suffix nem fedheti át egymást.
    [Theory]
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Claude_x64__abc123\Claude.exe", false)]   // átfedő prefix/suffix (nincs verzió)
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Claude__x64__abc123\Claude.exe", false)]  // üres wildcard
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-\Discord.exe", false)]            // puszta "app-"
    [InlineData(@"c:\program files\windowsapps\claude_*_x64__abc123\claude.exe",
                @"C:\Program Files\WindowsApps\Claude_1_x64__abc123\Claude.exe", true)]  // 1 karakter már elég
    [InlineData(@"c:\users\x\appdata\local\discord\app-*\discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1\Discord.exe", true)]            // 1 karakter már elég
    public void In_segment_wildcard_needs_at_least_one_char(string pattern, string path, bool expected)
        => Assert.Equal(expected, PatternMatcher.IsMatch(pattern, path));

    // Kontraktus: a pattern oldala is kisbetűsítve kerül összevetésre (kézzel szerkesztett/deszerializált minta).
    [Theory]
    [InlineData(@"C:\Users\X\AppData\Local\Discord\APP-*\Discord.exe",
                @"c:\users\x\appdata\local\discord\app-1.0.9300\discord.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_*_X64__ABC123\Claude.exe",
                @"C:\Program Files\WindowsApps\Claude_2.27.0.0_x64__abc123\Claude.exe")]
    public void Pattern_side_is_case_insensitive(string pattern, string path)
        => Assert.True(PatternMatcher.IsMatch(pattern, path));

    // Körút: a régi verzió útvonalából képzett minta az app ÚJ verziójának útvonalára is illeszkedik.
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Discord\app-1.0.9260\Discord.exe",
                @"C:\Users\x\AppData\Local\Discord\app-1.0.9999\Discord.exe")]            // Squirrel
    [InlineData(@"C:\Program Files\WindowsApps\Claude_2.26454.2.0_x64__abc123\Claude.exe",
                @"C:\Program Files\WindowsApps\Claude_2.27.0.0_x64__abc123\Claude.exe")]  // MSIX
    [InlineData(@"C:\Apps\Tool\1.4.2\tool.exe",
                @"C:\Apps\Tool\1.5.0\tool.exe")]                                          // sima verziómappa
    public void Pattern_from_old_version_path_matches_new_version_path(string oldPath, string newPath)
    {
        var pattern = PatternFactory.FromPath(oldPath);

        Assert.True(PatternMatcher.IsMatch(pattern, oldPath)); // a saját útvonalára is illeszkedik
        Assert.True(PatternMatcher.IsMatch(pattern, newPath));
    }
}
