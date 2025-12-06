using System.Text.RegularExpressions;

namespace DnsWatcher.Tests;

/// <summary>
/// Tests fuer die Konfigurationsparsing-Logik
/// </summary>
public class ConfigParserTests
{
    // Duplikat der Logik aus DnsWatcherConfig.FromArgs für Tests
    private static TestConfig ParseArgs(string[] args)
    {
        var config = new TestConfig
        {
            ShowRaw = args.Contains("--raw") || args.Contains("-r"),
            JsonOutput = args.Contains("--json") || args.Contains("-j"),
            Quiet = args.Contains("--quiet") || args.Contains("-q")
        };

        var logArg = args.FirstOrDefault(a => a.StartsWith("--log=") || a.StartsWith("-l="));
        if (logArg != null)
            config.LogFile = logArg.Split('=')[1];

        var sqliteArg = args.FirstOrDefault(a => a.StartsWith("--sqlite=") || a.StartsWith("-s="));
        if (sqliteArg != null)
            config.SqliteDb = sqliteArg.Split('=')[1];

        var retentionArg = args.FirstOrDefault(a => a.StartsWith("--retention="));
        if (retentionArg != null && int.TryParse(retentionArg.Split('=')[1], out int days))
            config.RetentionDays = days;

        var maxSizeArg = args.FirstOrDefault(a => a.StartsWith("--max-size="));
        if (maxSizeArg != null)
        {
            var sizeStr = maxSizeArg.Split('=')[1].ToUpperInvariant();
            if (sizeStr.EndsWith("GB") && long.TryParse(sizeStr.Replace("GB", ""), out long gb))
                config.MaxSizeBytes = gb * 1024 * 1024 * 1024;
            else if (sizeStr.EndsWith("MB") && long.TryParse(sizeStr.Replace("MB", ""), out long mb))
                config.MaxSizeBytes = mb * 1024 * 1024;
            else if (long.TryParse(sizeStr, out long mb2))
                config.MaxSizeBytes = mb2 * 1024 * 1024;
        }

        var backupsArg = args.FirstOrDefault(a => a.StartsWith("--backups="));
        if (backupsArg != null && int.TryParse(backupsArg.Split('=')[1], out int backups))
            config.BackupCount = Math.Max(0, backups);

        return config;
    }

    [Fact]
    public void ParseArgs_Defaults()
    {
        var config = ParseArgs(Array.Empty<string>());

        Assert.False(config.ShowRaw);
        Assert.False(config.JsonOutput);
        Assert.False(config.Quiet);
        Assert.Null(config.LogFile);
        Assert.Null(config.SqliteDb);
        Assert.Equal(30, config.RetentionDays);
        Assert.Equal(0, config.MaxSizeBytes);
        Assert.Equal(3, config.BackupCount);
    }

    [Theory]
    [InlineData("--raw")]
    [InlineData("-r")]
    public void ParseArgs_RawFlag(string flag)
    {
        var config = ParseArgs(new[] { flag });
        Assert.True(config.ShowRaw);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("-j")]
    public void ParseArgs_JsonFlag(string flag)
    {
        var config = ParseArgs(new[] { flag });
        Assert.True(config.JsonOutput);
    }

    [Theory]
    [InlineData("--quiet")]
    [InlineData("-q")]
    public void ParseArgs_QuietFlag(string flag)
    {
        var config = ParseArgs(new[] { flag });
        Assert.True(config.Quiet);
    }

    [Theory]
    [InlineData("--log=C:\\Logs\\dns.log", "C:\\Logs\\dns.log")]
    [InlineData("-l=test.log", "test.log")]
    public void ParseArgs_LogFile(string arg, string expected)
    {
        var config = ParseArgs(new[] { arg });
        Assert.Equal(expected, config.LogFile);
    }

    [Theory]
    [InlineData("--sqlite=C:\\Data\\dns.db", "C:\\Data\\dns.db")]
    [InlineData("-s=test.db", "test.db")]
    public void ParseArgs_SqliteDb(string arg, string expected)
    {
        var config = ParseArgs(new[] { arg });
        Assert.Equal(expected, config.SqliteDb);
    }

    [Theory]
    [InlineData("--retention=7", 7)]
    [InlineData("--retention=90", 90)]
    [InlineData("--retention=1", 1)]
    public void ParseArgs_RetentionDays(string arg, int expected)
    {
        var config = ParseArgs(new[] { arg });
        Assert.Equal(expected, config.RetentionDays);
    }

    [Theory]
    [InlineData("--max-size=500MB", 500L * 1024 * 1024)]
    [InlineData("--max-size=1GB", 1L * 1024 * 1024 * 1024)]
    [InlineData("--max-size=100", 100L * 1024 * 1024)]
    public void ParseArgs_MaxSize(string arg, long expected)
    {
        var config = ParseArgs(new[] { arg });
        Assert.Equal(expected, config.MaxSizeBytes);
    }

    [Theory]
    [InlineData("--backups=0", 0)]
    [InlineData("--backups=5", 5)]
    [InlineData("--backups=1", 1)]
    public void ParseArgs_BackupCount(string arg, int expected)
    {
        var config = ParseArgs(new[] { arg });
        Assert.Equal(expected, config.BackupCount);
    }

    [Fact]
    public void ParseArgs_CombinedArgs()
    {
        var config = ParseArgs(new[]
        {
            "--json",
            "--sqlite=C:\\data\\dns.db",
            "--retention=14",
            "--max-size=2GB",
            "--backups=5"
        });

        Assert.True(config.JsonOutput);
        Assert.Equal("C:\\data\\dns.db", config.SqliteDb);
        Assert.Equal(14, config.RetentionDays);
        Assert.Equal(2L * 1024 * 1024 * 1024, config.MaxSizeBytes);
        Assert.Equal(5, config.BackupCount);
    }

    private class TestConfig
    {
        public bool ShowRaw { get; set; }
        public bool JsonOutput { get; set; }
        public bool Quiet { get; set; }
        public string? LogFile { get; set; }
        public string? SqliteDb { get; set; }
        public int RetentionDays { get; set; } = 30;
        public long MaxSizeBytes { get; set; }
        public int BackupCount { get; set; } = 3;
    }
}
