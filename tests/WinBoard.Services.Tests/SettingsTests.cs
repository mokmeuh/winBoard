using WinBoard.Services.Gemini;
using WinBoard.Services.Settings;

namespace WinBoard.Services.Tests;

public class SettingsTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "wbtest-" + Guid.NewGuid());

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var s = new SettingsStore(TempDir()).Load();
        Assert.Equal("fr", s.Language);
        Assert.Equal("gemini-2.5-flash", s.GeminiModel);
        Assert.Equal(DictationStyle.Clean, s.DictationStyle);
        Assert.NotNull(s.Custom);
    }

    [Fact]
    public void SaveLoad_RoundTrip_AndChangedEvent()
    {
        var dir = TempDir();
        try
        {
            var store = new SettingsStore(dir);
            int fired = 0;
            store.Changed += (_, _) => fired++;
            var s = store.Load();
            s.Layout = "QWERTY"; s.KeyHeight = 60; s.Docked = true; s.Opacity = 0.8;
            s.DictationStyle = DictationStyle.BulletPoints;
            s.Custom.Accent = "#123456"; s.Window.Left = 10; s.Window.Top = 20;
            store.Save();
            Assert.Equal(1, fired);
            Assert.False(File.Exists(Path.Combine(dir, "settings.json.tmp")));

            var l = new SettingsStore(dir).Load();
            Assert.Equal("QWERTY", l.Layout);
            Assert.Equal(60, l.KeyHeight);
            Assert.True(l.Docked);
            Assert.Equal(DictationStyle.BulletPoints, l.DictationStyle);
            Assert.Equal("#123456", l.Custom.Accent);
            Assert.Equal(10, l.Window.Left);
            Assert.Contains("BulletPoints", File.ReadAllText(Path.Combine(dir, "settings.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_CorruptFile_GivesDefaults()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ pas du json");
            Assert.Equal("fr", new SettingsStore(dir).Load().Language);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "null");
            Assert.Equal("AZERTY", new SettingsStore(dir).Load().Layout);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Themes_BuiltIn_AreValid()
    {
        var names = Themes.BuiltIn.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var n in new[] { "Clair", "Sombre", "Bleu nuit", "Contraste élevé" }) Assert.Contains(n, names);
        Assert.True(names.Count >= 6);
        var hex = new System.Text.RegularExpressions.Regex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
        foreach (var t in Themes.BuiltIn)
            foreach (var c in new[] { t.Background, t.Key, t.KeyPressed, t.SpecialKey, t.Text, t.Accent, t.SuggestionBar, t.Trail })
                Assert.Matches(hex, c);
        Assert.Equal("Sombre", new AppSettings { ThemeName = "inconnu" }.ResolveTheme().Name);
        Assert.Equal("Clair", new AppSettings { ThemeName = "clair" }.ResolveTheme().Name);
    }

    [Fact]
    public void ApiKey_IsNeverInSettings_AndRoundTrips()
    {
        var dir = TempDir();
        try
        {
            var store = new SettingsStore(dir);
            store.Save(new AppSettings());
            var secrets = new SecretStore(dir, new PassthroughProtector());
            Assert.Null(secrets.GetApiKey());
            secrets.SetApiKey("AIza-secret-123");
            Assert.Equal("AIza-secret-123", secrets.GetApiKey());
            Assert.DoesNotContain("AIza", File.ReadAllText(Path.Combine(dir, "settings.json")));
            secrets.Clear();
            Assert.Null(secrets.GetApiKey());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void AppPaths_EndsWithWinBoard() => Assert.EndsWith("WinBoard", AppPaths.Root);
}
