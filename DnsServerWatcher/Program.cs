using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using System.Diagnostics;
using ARSoft.Tools.Net.Dns;

namespace DnsServerWatcher;

class Program
{
    private const string ServiceName = "DnsServerWatcher";
    private const string ServiceDisplayName = "DNS Server ETW Watcher";
    private const string ServiceDescription = "Protokolliert DNS-Anfragen und -Antworten in Echtzeit via ETW. Speichert Events in SQLite fuer spaetere Analyse (z.B. IP-Adressen zu DNS-Namen zuordnen).";
    private const string EventLogSource = "DnsServerWatcher";

    static void Main(string[] args)
    {
        // Service-Kommandos zuerst pruefen
        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd == "install" || cmd == "--install")
            {
                InstallService(args.Skip(1).ToArray());
                return;
            }
            if (cmd == "uninstall" || cmd == "--uninstall")
            {
                UninstallService();
                return;
            }
            if (cmd == "start" || cmd == "--start")
            {
                StartService();
                return;
            }
            if (cmd == "stop" || cmd == "--stop")
            {
                StopService();
                return;
            }
            if (cmd == "status" || cmd == "--status")
            {
                ShowServiceStatus();
                return;
            }
        }

        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            PrintHelp();
            return;
        }

        // Admin-Check
        if (!TraceEventSession.IsElevated() ?? false)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FEHLER: Muss als Administrator ausgefuehrt werden!");
            Console.ResetColor();
            return;
        }

        // Konfiguration aus Args parsen
        var config = DnsWatcherConfig.FromArgs(args);

        // Host erstellen - automatische Erkennung ob Service oder Console
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(config);
        builder.Services.AddHostedService<DnsServerWatcherService>();
        builder.Services.AddWindowsService(options => options.ServiceName = ServiceName);
        builder.Logging.AddEventLog(new EventLogSettings
        {
            SourceName = EventLogSource,
            LogName = "Application"
        });

        var host = builder.Build();
        host.Run();
    }

    private static void InstallService(string[] serviceArgs)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null)
        {
            Console.WriteLine("Fehler: Konnte Programmpfad nicht ermitteln");
            return;
        }

        var args = serviceArgs.Length > 0 ? string.Join(" ", serviceArgs) : "";

        try
        {
            if (!EventLog.SourceExists(EventLogSource))
            {
                EventLog.CreateEventSource(EventLogSource, "Application");
                Console.WriteLine($"[OK] EventLog Source '{EventLogSource}' erstellt");
            }

            var binPath = string.IsNullOrEmpty(args) ? $"\"{exePath}\"" : $"\"{exePath}\" {args}";
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{ServiceDisplayName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
            {
                // Description setzen
                var descPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"description {ServiceName} \"{ServiceDescription}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var descProc = Process.Start(descPsi);
                descProc?.WaitForExit();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Service '{ServiceName}' installiert");
                Console.WriteLine($"     Pfad: {binPath}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Starten mit: DnsServerWatcher.exe start");
            }
            else
            {
                var error = proc?.StandardError.ReadToEnd();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FEHLER] {error}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FEHLER] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void UninstallService()
    {
        try
        {
            StopService();

            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Service '{ServiceName}' deinstalliert");
                Console.ResetColor();
            }

            if (EventLog.SourceExists(EventLogSource))
            {
                EventLog.DeleteEventSource(EventLogSource);
                Console.WriteLine($"[OK] EventLog Source '{EventLogSource}' entfernt");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FEHLER] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void StartService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Service '{ServiceName}' gestartet");
                Console.ResetColor();
            }
            else
            {
                var error = proc?.StandardError.ReadToEnd();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FEHLER] {error?.Trim()}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FEHLER] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void StopService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] Service '{ServiceName}' gestoppt");
                Console.ResetColor();
            }
        }
        catch { }
    }

    private static void ShowServiceStatus()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd();
            proc?.WaitForExit();

            if (proc?.ExitCode == 0 && output != null)
            {
                Console.WriteLine($"Service: {ServiceName}");
                Console.WriteLine(new string('-', 40));

                if (output.Contains("RUNNING"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Status: RUNNING");
                }
                else if (output.Contains("STOPPED"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Status: STOPPED");
                }
                else
                {
                    Console.WriteLine(output);
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Service '{ServiceName}' ist nicht installiert");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FEHLER] {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
DNS Server Real-Time ETW Watcher
================================

Zeigt DNS Server Events in Echtzeit an.
Muss als Administrator ausgefuehrt werden.

Verwendung:
  DnsServerWatcher.exe [optionen]
  DnsServerWatcher.exe [service-kommando] [optionen]

Service-Kommandos:
  install [opts]     Installiert als Windows-Dienst mit den angegebenen Optionen
  uninstall          Deinstalliert den Windows-Dienst
  start              Startet den Windows-Dienst
  stop               Stoppt den Windows-Dienst
  status             Zeigt den Status des Windows-Dienstes

Optionen:
  -r, --raw          Zeige alle Event-Properties
  -j, --json         Ausgabe als JSON (eine Zeile pro Event)
  -q, --quiet        Keine Console-Ausgabe (nur SQLite/Log schreiben)
  -l, --log=X        Schreibe Events zusaetzlich in Datei X
  -s, --sqlite=X     Speichere Events in SQLite Datenbank X
      --retention=N  Behalte Eintraege fuer N Tage (default: 30)
      --max-size=X   Max. Datenbankgroesse (z.B. 500MB, 1GB)
      --backups=N    Anzahl Backup-Versionen (default: 3, 0=keine)
  -h, --help, /?     Diese Hilfe anzeigen

SQLite Wartung:
  - Retention-Cleanup: Beim Start + taeglich waehrend Laufzeit
  - Size-Cleanup: Loescht aelteste 10% wenn max-size ueberschritten
  - VACUUM: Automatisch nach groesseren Loeschungen (>1000 Eintraege)
  - Backup: Vor jedem Cleanup, rotiert (backup1, backup2, backup3...)

Beispiele (Konsole):
  DnsServerWatcher.exe
  DnsServerWatcher.exe --sqlite=C:\Logs\dns.db --retention=30 --max-size=500MB

Beispiele (Service):
  DnsServerWatcher.exe install --sqlite=C:\Logs\dns.db --retention=30
  DnsServerWatcher.exe start
  DnsServerWatcher.exe status
  DnsServerWatcher.exe stop
  DnsServerWatcher.exe uninstall

EventLog:
  Im Service-Modus werden Meldungen ins Windows EventLog geschrieben:
  - Quelle: DnsServerWatcher
  - Log: Application
  - Statistik alle 5 Minuten

SQLite-Abfragen:
  SELECT * FROM dns_events WHERE resolved_ips LIKE '%142.250.185%';
  SELECT * FROM dns_events WHERE client_ip = '10.0.0.51';
  SELECT * FROM dns_events WHERE query_name LIKE '%google%';
");
    }
}

// Konfiguration
public class DnsWatcherConfig
{
    public bool ShowRaw { get; set; }
    public bool JsonOutput { get; set; }
    public bool Quiet { get; set; }
    public string? LogFile { get; set; }
    public string? SqliteDb { get; set; }
    public int RetentionDays { get; set; } = 30;
    public long MaxSizeBytes { get; set; }
    public int BackupCount { get; set; } = 3;

    public static DnsWatcherConfig FromArgs(string[] args)
    {
        var config = new DnsWatcherConfig
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
}

// Der eigentliche Watcher als BackgroundService
public class DnsServerWatcherService : BackgroundService
{
    private static readonly Guid DnsServerProviderGuid = new("EB79061A-A566-4698-9119-3ED2807060E7");

    private static readonly Dictionary<int, string> QueryTypes = new()
    {
        { 1, "A" }, { 2, "NS" }, { 5, "CNAME" }, { 6, "SOA" },
        { 12, "PTR" }, { 15, "MX" }, { 16, "TXT" }, { 28, "AAAA" },
        { 33, "SRV" }, { 35, "NAPTR" }, { 37, "CERT" },
        // DNSSEC
        { 43, "DS" }, { 46, "RRSIG" }, { 47, "NSEC" }, { 48, "DNSKEY" },
        { 50, "NSEC3" }, { 51, "NSEC3PARAM" },
        // TSIG/TKEY
        { 249, "TKEY" }, { 250, "TSIG" },
        // Sonstige
        { 52, "TLSA" }, { 64, "SVCB" }, { 65, "HTTPS" },
        { 99, "SPF" }, { 255, "ANY" }, { 257, "CAA" }
    };

    private static readonly Dictionary<int, string> ResponseCodes = new()
    {
        { 0, "OK" }, { 1, "FormErr" }, { 2, "ServFail" },
        { 3, "NXDomain" }, { 4, "NotImpl" }, { 5, "Refused" },
        { 6, "YXDomain" }, { 7, "YXRRSet" }, { 8, "NXRRSet" },
        { 9, "NotAuth" }, { 10, "NotZone" }
    };

    // Fehler-Kategorien
    private static readonly HashSet<string> ConfigErrors = new()
    {
        "NXDomain", "ServFail", "Refused", "NotImpl", "NotAuth", "NotZone"
    };

    private static readonly HashSet<string> ClientErrors = new()
    {
        "FormErr", "YXDomain", "YXRRSet", "NXRRSet"
    };

    private readonly ILogger<DnsServerWatcherService> _logger;
    private readonly DnsWatcherConfig _config;
    private readonly List<DnsServerEvent> _eventQueue = new();
    private readonly object _queueLock = new();

    private TraceEventSession? _session;
    private SqliteConnection? _sqliteConn;
    private SqliteCommand? _insertCmd;
    private StreamWriter? _logWriter;
    private Timer? _flushTimer;
    private Timer? _statsTimer;
    private DateTime _startTime;
    private DateTime _lastCleanup = DateTime.MinValue;
    private long _eventCount;

    private const int BatchSize = 100;
    private const int FlushIntervalSec = 5;
    private const int CleanupIntervalDays = 1;

    public DnsServerWatcherService(ILogger<DnsServerWatcherService> logger, DnsWatcherConfig config)
    {
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _startTime = DateTime.Now;
        var isConsole = Environment.UserInteractive;

        if (isConsole && !_config.Quiet)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("  DNS Server Real-Time ETW Watcher");
            Console.WriteLine("========================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        // SQLite initialisieren
        if (_config.SqliteDb != null)
        {
            try
            {
                InitSqlite(_config.SqliteDb);
                _logger.LogInformation("SQLite Datenbank initialisiert: {Path} (Retention: {Days} Tage)",
                    _config.SqliteDb, _config.RetentionDays);

                if (isConsole && !_config.Quiet)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[OK] SQLite Datenbank: {_config.SqliteDb}");
                    Console.WriteLine($"     Retention: {_config.RetentionDays} Tage");
                    if (_config.MaxSizeBytes > 0)
                        Console.WriteLine($"     Max-Size: {_config.MaxSizeBytes / 1024 / 1024} MB");
                    Console.WriteLine($"     Backups: {_config.BackupCount}");
                    Console.ResetColor();
                }
                CleanupBySize();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQLite Fehler");
                if (isConsole)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FEHLER] SQLite: {ex.Message}");
                    Console.ResetColor();
                }
                return;
            }
        }

        if (_config.LogFile != null)
            _logWriter = new StreamWriter(_config.LogFile, append: true) { AutoFlush = true };

        var sessionName = $"DnsServerWatcher_{Environment.ProcessId}";

        try
        {
            using var oldSession = TraceEventSession.GetActiveSession(sessionName);
            oldSession?.Stop();
        }
        catch { }

        if (isConsole && !_config.Quiet)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Starte ETW Session: {sessionName}");
            Console.ResetColor();
        }

        _session = new TraceEventSession(sessionName);
        _session.Source.Dynamic.All += ProcessEvent;
        _session.EnableProvider(DnsServerProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);

        _statsTimer = new Timer(_ => WriteStats(), null, 5 * 60 * 1000, 5 * 60 * 1000);

        _logger.LogInformation("DnsServerWatcher gestartet");

        if (isConsole && !_config.Quiet)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK] DNS Server Provider aktiviert");
            Console.WriteLine();
            Console.WriteLine("==========================================");
            Console.WriteLine("  LIVE - Warte auf DNS Events (Ctrl+C)");
            Console.WriteLine("==========================================");
            Console.ResetColor();
            Console.WriteLine();

            if (!_config.JsonOutput)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Legende: QUERY=Anfrage empfangen, RESPONSE=Antwort gesendet");
                Console.WriteLine();
                Console.ResetColor();
            }
        }

        var processingTask = Task.Run(() => _session.Source.Process(), stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _session.Stop();
        await processingTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DnsServerWatcher wird beendet. {Count:N0} Events verarbeitet.", _eventCount);

        _session?.Stop();
        _statsTimer?.Dispose();
        _flushTimer?.Dispose();

        FlushEventQueue();

        _insertCmd?.Dispose();
        _sqliteConn?.Close();
        _logWriter?.Close();

        if (Environment.UserInteractive && !_config.Quiet)
            Console.WriteLine("Fertig.");

        await base.StopAsync(cancellationToken);
    }

    private void WriteStats()
    {
        var runtime = DateTime.Now - _startTime;
        var eventsPerSec = _eventCount / Math.Max(1, runtime.TotalSeconds);
        _logger.LogInformation("Laeuft seit {Runtime:hh\\:mm\\:ss}, {Count:N0} Events ({Rate:F1}/s)",
            runtime, _eventCount, eventsPerSec);
    }

    private void InitSqlite(string dbPath)
    {
        _sqliteConn = new SqliteConnection($"Data Source={dbPath}");
        _sqliteConn.Open();

        using (var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", _sqliteConn))
            walCmd.ExecuteNonQuery();

        // Schema-Migration zuerst, dann Tabelle erstellen
        MigrateSchema();

        var createTable = @"
            CREATE TABLE IF NOT EXISTS dns_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                event_type TEXT NOT NULL,
                client_ip TEXT,
                query_name TEXT,
                query_type TEXT,
                response_code TEXT,
                resolved_ips TEXT,
                zone TEXT,
                error_category TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_timestamp ON dns_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_client_ip ON dns_events(client_ip);
            CREATE INDEX IF NOT EXISTS idx_query_name ON dns_events(query_name);
            CREATE INDEX IF NOT EXISTS idx_resolved_ips ON dns_events(resolved_ips);
            CREATE INDEX IF NOT EXISTS idx_error_category ON dns_events(error_category);
        ";

        using var cmd = new SqliteCommand(createTable, _sqliteConn);
        cmd.ExecuteNonQuery();

        _insertCmd = new SqliteCommand(@"
            INSERT INTO dns_events
            (timestamp, event_type, client_ip, query_name, query_type, response_code, resolved_ips, zone, error_category)
            VALUES
            (@timestamp, @event_type, @client_ip, @query_name, @query_type, @response_code, @resolved_ips, @zone, @error_category)
        ", _sqliteConn);
        _insertCmd.Parameters.Add("@timestamp", SqliteType.Text);
        _insertCmd.Parameters.Add("@event_type", SqliteType.Text);
        _insertCmd.Parameters.Add("@client_ip", SqliteType.Text);
        _insertCmd.Parameters.Add("@query_name", SqliteType.Text);
        _insertCmd.Parameters.Add("@query_type", SqliteType.Text);
        _insertCmd.Parameters.Add("@response_code", SqliteType.Text);
        _insertCmd.Parameters.Add("@resolved_ips", SqliteType.Text);
        _insertCmd.Parameters.Add("@zone", SqliteType.Text);
        _insertCmd.Parameters.Add("@error_category", SqliteType.Text);
        _insertCmd.Prepare();

        _flushTimer = new Timer(_ => FlushEventQueue(), null, FlushIntervalSec * 1000, FlushIntervalSec * 1000);

        CreateBackup();
        CleanupOldEntries();
    }

    private void CreateBackup()
    {
        if (_config.SqliteDb == null || !File.Exists(_config.SqliteDb) || _config.BackupCount <= 0) return;

        var fileInfo = new FileInfo(_config.SqliteDb);
        var directory = fileInfo.DirectoryName ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(_config.SqliteDb);
        var extension = Path.GetExtension(_config.SqliteDb);

        for (int i = _config.BackupCount; i >= 1; i--)
        {
            var oldBackup = Path.Combine(directory, $"{baseName}.backup{i}{extension}");
            var newBackup = Path.Combine(directory, $"{baseName}.backup{i + 1}{extension}");

            if (File.Exists(oldBackup))
            {
                if (i == _config.BackupCount)
                    File.Delete(oldBackup);
                else
                {
                    if (File.Exists(newBackup)) File.Delete(newBackup);
                    File.Move(oldBackup, newBackup);
                }
            }
        }

        var backupPath = Path.Combine(directory, $"{baseName}.backup1{extension}");
        try
        {
            File.Copy(_config.SqliteDb, backupPath, overwrite: true);
            if (Environment.UserInteractive && !_config.Quiet)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[SQLite] Backup erstellt: {Path.GetFileName(backupPath)}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Backup fehlgeschlagen: {Message}", ex.Message);
        }
    }

    private void CleanupOldEntries()
    {
        if (_sqliteConn == null) return;

        var cutoff = DateTime.Now.AddDays(-_config.RetentionDays).ToString("o");
        using var cmd = new SqliteCommand("DELETE FROM dns_events WHERE timestamp < @cutoff", _sqliteConn);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        var deleted = cmd.ExecuteNonQuery();

        if (deleted > 0)
        {
            if (Environment.UserInteractive && !_config.Quiet)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[SQLite] {deleted} Eintraege aelter als {_config.RetentionDays} Tage geloescht");
                Console.ResetColor();
            }
            RunVacuumIfNeeded(deleted);
        }

        _lastCleanup = DateTime.Now;
    }

    private void CleanupBySize()
    {
        if (_sqliteConn == null || _config.MaxSizeBytes <= 0 || _config.SqliteDb == null) return;

        var fileInfo = new FileInfo(_config.SqliteDb);
        if (!fileInfo.Exists || fileInfo.Length <= _config.MaxSizeBytes) return;

        var countCmd = new SqliteCommand("SELECT COUNT(*) FROM dns_events", _sqliteConn);
        var totalCount = Convert.ToInt64(countCmd.ExecuteScalar());
        var deleteCount = Math.Max(1, totalCount / 10);

        using var cmd = new SqliteCommand(@"
            DELETE FROM dns_events WHERE id IN (
                SELECT id FROM dns_events ORDER BY timestamp ASC LIMIT @count
            )", _sqliteConn);
        cmd.Parameters.AddWithValue("@count", deleteCount);
        var deleted = cmd.ExecuteNonQuery();

        if (deleted > 0)
        {
            if (Environment.UserInteractive && !_config.Quiet)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[SQLite] {deleted} aelteste Eintraege geloescht (max-size erreicht)");
                Console.ResetColor();
            }
            RunVacuumIfNeeded(deleted);
        }
    }

    private void RunVacuumIfNeeded(int deletedCount)
    {
        if (_sqliteConn == null || deletedCount < 1000) return;

        try
        {
            using var vacuumCmd = new SqliteCommand("VACUUM", _sqliteConn);
            vacuumCmd.ExecuteNonQuery();
            if (Environment.UserInteractive && !_config.Quiet)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("[SQLite] VACUUM ausgefuehrt");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("VACUUM fehlgeschlagen: {Message}", ex.Message);
        }
    }

    private void CheckPeriodicCleanup()
    {
        if (_sqliteConn == null) return;
        if ((DateTime.Now - _lastCleanup).TotalDays >= CleanupIntervalDays)
        {
            CreateBackup();
            CleanupOldEntries();
            CleanupBySize();
        }
    }

    private const int CurrentSchemaVersion = 2;

    private int GetSchemaVersion()
    {
        if (_sqliteConn == null) return 0;

        // Pruefen ob schema_version Tabelle existiert
        using var checkCmd = new SqliteCommand(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'", _sqliteConn);
        var exists = checkCmd.ExecuteScalar() != null;

        if (!exists) return 1; // Alte DB ohne Versionstabelle = Version 1

        using var versionCmd = new SqliteCommand("SELECT version FROM schema_version LIMIT 1", _sqliteConn);
        var result = versionCmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 1;
    }

    private void SetSchemaVersion(int version)
    {
        if (_sqliteConn == null) return;

        using var createCmd = new SqliteCommand(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER)", _sqliteConn);
        createCmd.ExecuteNonQuery();

        using var deleteCmd = new SqliteCommand("DELETE FROM schema_version", _sqliteConn);
        deleteCmd.ExecuteNonQuery();

        using var insertCmd = new SqliteCommand($"INSERT INTO schema_version (version) VALUES ({version})", _sqliteConn);
        insertCmd.ExecuteNonQuery();
    }

    private void MigrateSchema()
    {
        if (_sqliteConn == null) return;

        var currentVersion = GetSchemaVersion();

        // Migration von Version 1 -> 2: error_category hinzufuegen
        if (currentVersion < 2)
        {
            // Pruefen ob dns_events Tabelle existiert (koennte neue DB sein)
            using var checkCmd = new SqliteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='dns_events'", _sqliteConn);
            var tableExists = checkCmd.ExecuteScalar() != null;

            if (tableExists)
            {
                // Pruefen welche Spalten existieren
                var existingColumns = new HashSet<string>();
                using (var cmd = new SqliteCommand("PRAGMA table_info(dns_events)", _sqliteConn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1).ToLowerInvariant());
                    }
                }

                if (!existingColumns.Contains("error_category"))
                {
                    using var alterCmd = new SqliteCommand(
                        "ALTER TABLE dns_events ADD COLUMN error_category TEXT", _sqliteConn);
                    alterCmd.ExecuteNonQuery();

                    using var indexCmd = new SqliteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_error_category ON dns_events(error_category)", _sqliteConn);
                    indexCmd.ExecuteNonQuery();

                    if (Environment.UserInteractive && !_config.Quiet)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("[SQLite] Schema migriert v1->v2: error_category Spalte hinzugefuegt");
                        Console.ResetColor();
                    }
                    _logger.LogInformation("Schema migriert v1->v2: error_category Spalte hinzugefuegt");
                }
            }
        }

        // Aktuelle Version setzen
        SetSchemaVersion(CurrentSchemaVersion);
    }

    private void QueueEvent(DnsServerEvent evt)
    {
        if (_sqliteConn == null) return;

        lock (_queueLock)
        {
            _eventQueue.Add(evt);
            if (_eventQueue.Count >= BatchSize)
                FlushEventQueueInternal();
        }
    }

    private void FlushEventQueue()
    {
        lock (_queueLock)
        {
            FlushEventQueueInternal();
        }
    }

    private void FlushEventQueueInternal()
    {
        if (_sqliteConn == null || _insertCmd == null || _eventQueue.Count == 0) return;

        try
        {
            using var transaction = _sqliteConn.BeginTransaction();
            _insertCmd.Transaction = transaction;

            foreach (var evt in _eventQueue)
            {
                _insertCmd.Parameters["@timestamp"].Value = evt.Timestamp.ToString("o");
                _insertCmd.Parameters["@event_type"].Value = evt.EventType;
                _insertCmd.Parameters["@client_ip"].Value = evt.ClientIp ?? (object)DBNull.Value;
                _insertCmd.Parameters["@query_name"].Value = evt.QueryName ?? (object)DBNull.Value;
                _insertCmd.Parameters["@query_type"].Value = evt.QueryType ?? (object)DBNull.Value;
                _insertCmd.Parameters["@response_code"].Value = evt.ResponseCode ?? (object)DBNull.Value;
                _insertCmd.Parameters["@resolved_ips"].Value = evt.ResolvedIps ?? (object)DBNull.Value;
                _insertCmd.Parameters["@zone"].Value = evt.Zone ?? (object)DBNull.Value;
                _insertCmd.Parameters["@error_category"].Value = evt.ErrorCategory ?? (object)DBNull.Value;
                _insertCmd.ExecuteNonQuery();
            }

            transaction.Commit();
            _eventQueue.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError("Batch-Write Fehler: {Message}", ex.Message);
            _eventQueue.Clear();
        }
    }

    private static (List<string> answers, string? parseError) ParseDnsAnswers(byte[] packetData)
    {
        var answers = new List<string>();
        if (packetData == null || packetData.Length < 12)
            return (answers, packetData == null ? null : "TRUNCATED");

        try
        {
            var message = DnsMessage.Parse(packetData);

            foreach (var record in message.AnswerRecords)
            {
                switch (record)
                {
                    case ARecord aRecord:
                        answers.Add(aRecord.Address.ToString());
                        break;
                    case AaaaRecord aaaaRecord:
                        answers.Add(aaaaRecord.Address.ToString());
                        break;
                    case CNameRecord cnameRecord:
                        answers.Add($"CNAME:{cnameRecord.CanonicalName}");
                        break;
                    case MxRecord mxRecord:
                        answers.Add($"MX:{mxRecord.Preference} {mxRecord.ExchangeDomainName}");
                        break;
                    case NsRecord nsRecord:
                        answers.Add($"NS:{nsRecord.NameServer}");
                        break;
                    case PtrRecord ptrRecord:
                        answers.Add($"PTR:{ptrRecord.PointerDomainName}");
                        break;
                    case TxtRecord txtRecord:
                        answers.Add($"TXT:{string.Join(" ", txtRecord.TextData)}");
                        break;
                    case SrvRecord srvRecord:
                        answers.Add($"SRV:{srvRecord.Priority} {srvRecord.Weight} {srvRecord.Port} {srvRecord.Target}");
                        break;
                    case SoaRecord soaRecord:
                        answers.Add($"SOA:{soaRecord.MasterName} {soaRecord.ResponsibleName}");
                        break;
                    case CAARecord caaRecord:
                        answers.Add($"CAA:{caaRecord.Flags} {caaRecord.Tag} {caaRecord.Value}");
                        break;
                    default:
                        answers.Add($"{record.RecordType}:{record}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            return (answers, $"MALFORMED:{ex.Message}");
        }

        return (answers, null);
    }

    private static string GetEventName(int eventId) => eventId switch
    {
        256 => "QUERY_RECEIVED",
        257 => "RESPONSE_SUCCESS",
        258 => "RECURSE_QUERY_OUT",
        259 => "RECURSE_RESPONSE_IN",
        260 => "QUERY_TIMEOUT",
        261 => "RESPONSE_FAILURE",
        280 => "INTERNAL_LOOKUP",
        _ => $"EVENT_{eventId}"
    };

    private void ProcessEvent(TraceEvent evt)
    {
        if (evt.ID == 0) return;

        var timestamp = evt.TimeStamp.ToString("HH:mm:ss.fff");
        var eventId = (int)evt.ID;

        string? qname = null, source = null, dest = null, qtype = null, rcode = null, zone = null;
        string? parseError = null;
        byte[]? packetData = null;
        List<string> resolvedIps = new();

        try
        {
            qname = evt.PayloadByName("QNAME")?.ToString()?.TrimEnd('.');
            source = evt.PayloadByName("Source")?.ToString();
            dest = evt.PayloadByName("Destination")?.ToString();

            var qtypeVal = evt.PayloadByName("QTYPE");
            if (qtypeVal != null && int.TryParse(qtypeVal.ToString()?.Trim(), out int qt))
                qtype = QueryTypes.TryGetValue(qt, out var qtName) ? qtName : qt.ToString();

            var rcodeVal = evt.PayloadByName("RCODE");
            if (rcodeVal != null && int.TryParse(rcodeVal.ToString()?.Trim(), out int rc))
                rcode = ResponseCodes.TryGetValue(rc, out var rcName) ? rcName : rc.ToString();

            zone = evt.PayloadByName("Zone")?.ToString();

            packetData = evt.PayloadByName("PacketData") as byte[];
            if (packetData != null && (eventId == 257 || eventId == 261))
            {
                var (answers, error) = ParseDnsAnswers(packetData);
                resolvedIps = answers;
                parseError = error;
            }
        }
        catch { }

        // Fehler-Kategorie bestimmen
        string? errorCategory = null;
        if (parseError != null)
            errorCategory = "CLIENT_ERROR";
        else if (rcode != null && ClientErrors.Contains(rcode))
            errorCategory = "CLIENT_ERROR";
        else if (rcode != null && ConfigErrors.Contains(rcode))
            errorCategory = "CONFIG_ERROR";

        // JSON Output
        if (_config.JsonOutput)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = evt.TimeStamp,
                eventId,
                eventName = GetEventName(eventId),
                source,
                destination = dest,
                queryName = qname,
                queryType = qtype,
                responseCode = rcode,
                resolvedAddresses = resolvedIps.Count > 0 ? resolvedIps : null,
                zone,
                parseError,
                errorCategory
            });
            Console.WriteLine(json);
            _logWriter?.WriteLine(json);
            return;
        }

        // Log to file
        if (_logWriter != null && (eventId == 256 || eventId == 257))
        {
            var line = eventId switch
            {
                256 => $"{evt.TimeStamp:O}\tQUERY\t{source}\t{qname}\t{qtype}",
                257 => $"{evt.TimeStamp:O}\tRESPONSE\t{dest}\t{qname}\t{qtype}\t{rcode}",
                _ => $"{evt.TimeStamp:O}\t{eventId}\t{source ?? dest}\t{qname}\t{qtype}"
            };
            _logWriter.WriteLine(line);
        }

        // SQLite speichern (auch Fehler-Events 260, 261)
        if (_sqliteConn != null && (eventId == 256 || eventId == 257 || eventId == 260 || eventId == 261))
        {
            var eventType = eventId switch
            {
                256 => "QUERY",
                257 => "RESPONSE",
                260 => "TIMEOUT",
                261 => "RECURSE",
                _ => $"EVENT_{eventId}"
            };
            var clientIp = eventId == 256 ? source : dest;
            var resolvedInfo = parseError ?? (resolvedIps.Count > 0 ? string.Join(",", resolvedIps) : null);
            var dnsEvent = new DnsServerEvent(
                evt.TimeStamp, eventType, clientIp, qname, qtype, rcode,
                resolvedInfo, zone, errorCategory);
            QueueEvent(dnsEvent);
            Interlocked.Increment(ref _eventCount);
            CheckPeriodicCleanup();
        }

        // Console-Ausgabe
        if (_config.Quiet || !Environment.UserInteractive) return;
        if (eventId == 280 && !_config.ShowRaw) return;
        if (eventId != 256 && eventId != 257 && eventId != 260 && eventId != 261 && eventId != 280) return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{timestamp}] ");

        switch (eventId)
        {
            case 256:
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("QUERY    ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(source ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(qname ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[{qtype ?? "?"}]");
                break;

            case 257:
                // Response-Farbe je nach Kategorie
                var respColor = rcode == "OK" ? ConsoleColor.Green :
                    (errorCategory == "CLIENT_ERROR" ? ConsoleColor.DarkYellow : ConsoleColor.Red);
                Console.ForegroundColor = respColor;
                Console.Write("RESPONSE ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(dest ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" <- ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(qname ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = respColor;
                Console.Write(rcode ?? "?");
                if (errorCategory != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({errorCategory})");
                }
                if (parseError != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($" [{parseError}]");
                }
                if (resolvedIps.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write(" => " + string.Join(", ", resolvedIps));
                }
                Console.WriteLine();
                break;

            case 260:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("TIMEOUT  ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(source ?? dest ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -- ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(qname ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[{qtype ?? "?"}]");
                break;

            case 261:
                // RECURSE: Antwort vom Upstream-Server (rekursive Auflösung)
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("RECURSE  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(dest ?? source ?? "?");
                Console.Write(" <- ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(qname ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = rcode == "OK" ? ConsoleColor.DarkGreen : ConsoleColor.DarkYellow;
                Console.Write(rcode ?? "?");
                if (resolvedIps.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.Write(" => " + string.Join(", ", resolvedIps));
                }
                if (parseError != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write($" [{parseError}]");
                }
                Console.WriteLine();
                break;

            case 280:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("LOOKUP   ");
                Console.WriteLine($"{qname} [{qtype}]");
                break;
        }

        Console.ResetColor();

        if (_config.ShowRaw)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = 0; i < evt.PayloadNames.Length; i++)
                Console.WriteLine($"    {evt.PayloadNames[i]} = {evt.PayloadValue(i)}");
            Console.ResetColor();
        }
    }
}

// Event-Struktur
public record DnsServerEvent(
    DateTime Timestamp,
    string EventType,
    string? ClientIp,
    string? QueryName,
    string? QueryType,
    string? ResponseCode,
    string? ResolvedIps,
    string? Zone,
    string? ErrorCategory = null
);
