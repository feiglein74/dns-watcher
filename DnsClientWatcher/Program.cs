using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using System.Diagnostics;

namespace DnsClientWatcher;

class Program
{
    // Service-Konstanten
    private const string ServiceName = "DnsClientWatcher";
    private const string ServiceDisplayName = "DNS Client ETW Watcher";
    private const string ServiceDescription = "Protokolliert lokale DNS-Anfragen in Echtzeit via ETW. Zeigt welche Prozesse DNS-Abfragen machen und speichert Events in SQLite.";
    private const string EventLogSource = "DnsClientWatcher";

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

        // Konfiguration als Singleton registrieren
        builder.Services.AddSingleton(config);

        // Den Watcher-Service registrieren
        builder.Services.AddHostedService<DnsClientWatcherService>();

        // Windows Service Support - erkennt automatisch ob als Service gestartet
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = ServiceName;
        });

        // EventLog konfigurieren
        builder.Logging.AddEventLog(new EventLogSettings
        {
            SourceName = EventLogSource,
            LogName = "Application"
        });

        var host = builder.Build();
        host.Run();
    }

    // Service installieren
    private static void InstallService(string[] serviceArgs)
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null)
        {
            Console.WriteLine("Fehler: Konnte Programmpfad nicht ermitteln");
            return;
        }

        // Service-Argumente zusammenbauen (ohne --service, da Generic Host das automatisch erkennt)
        var args = serviceArgs.Length > 0 ? string.Join(" ", serviceArgs) : "";

        try
        {
            // EventLog Source erstellen
            if (!EventLog.SourceExists(EventLogSource))
            {
                EventLog.CreateEventSource(EventLogSource, "Application");
                Console.WriteLine($"[OK] EventLog Source '{EventLogSource}' erstellt");
            }

            // sc.exe zum Erstellen des Service
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
                Console.WriteLine("Starten mit: DnsClientWatcher.exe start");
                Console.WriteLine("Status mit:  DnsClientWatcher.exe status");
            }
            else
            {
                var error = proc?.StandardError.ReadToEnd();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FEHLER] Service konnte nicht installiert werden: {error}");
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

    // Service deinstallieren
    private static void UninstallService()
    {
        try
        {
            // Service stoppen falls laufend
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
            else
            {
                var error = proc?.StandardError.ReadToEnd();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Service-Deinstallation: {error?.Trim()}");
                Console.ResetColor();
            }

            // EventLog Source entfernen
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

    // Service starten
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

    // Service stoppen
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

    // Service-Status anzeigen
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
DNS Client Real-Time ETW Watcher
================================

Zeigt DNS Client Events in Echtzeit an.
Ueberwacht lokale DNS-Aufloesungen auf einem Client oder Server.
Muss als Administrator ausgefuehrt werden.

Verwendung:
  DnsClientWatcher.exe [optionen]
  DnsClientWatcher.exe [service-kommando] [optionen]

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
  DnsClientWatcher.exe
  DnsClientWatcher.exe --sqlite=C:\Logs\dnsclient.db --retention=30 --max-size=500MB

Beispiele (Service):
  DnsClientWatcher.exe install --sqlite=C:\Logs\dnsclient.db --retention=30
  DnsClientWatcher.exe start
  DnsClientWatcher.exe status
  DnsClientWatcher.exe stop
  DnsClientWatcher.exe uninstall

EventLog:
  Im Service-Modus werden Meldungen ins Windows EventLog geschrieben:
  - Quelle: DnsClientWatcher
  - Log: Application
  - Statistik alle 5 Minuten

SQLite-Abfragen:
  SELECT * FROM dns_events WHERE query_results LIKE '%142.250.185%';
  SELECT * FROM dns_events WHERE query_name LIKE '%google%';
  SELECT * FROM dns_events WHERE event_type = 'CACHE';
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
public class DnsClientWatcherService : BackgroundService
{
    private static readonly Guid DnsClientProviderGuid = new("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D");

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

    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        { 0, "OK" },
        { 87, "Cached" },
        { 1168, "NotFound" },
        { 1214, "InvalidName" },
        { 1460, "Timeout" },
        { 9002, "ServFail" },
        { 9003, "NXDomain" },
        { 9004, "NotImpl" },
        { 9005, "Refused" },
        { 9501, "NoRecords" },
        { 9560, "Timeout" },
        { 9701, "NoRecord" },
        { 9702, "RecordFormat" },
        { 11001, "HostNotFound" },
        { 11002, "TryAgain" },
        { 11003, "NoRecovery" },
        { 11004, "NoData" }
    };

    // Fehler-Kategorien nach Status-Code
    // CONFIG_ERROR = DNS-Server/Netzwerk-Konfiguration fehlerhaft
    // CLIENT_ERROR = Client-seitiger Fehler (ungueltige Anfrage, falscher Name)
    private static readonly HashSet<int> ConfigErrorCodes = new()
    {
        9002, // ServFail - DNS-Server Problem
        9004, // NotImpl - nicht implementiert
        9005, // Refused - Server verweigert
        1460, // Timeout - Netzwerk-Problem
        9560, // Timeout
        11002, // TryAgain - temporaer nicht erreichbar
        11003  // NoRecovery - unbekannter Serverfehler
    };

    private static readonly HashSet<int> ClientErrorCodes = new()
    {
        1214, // InvalidName - ungueltiger DNS-Name
        9702  // RecordFormat - fehlerhafte Antwort
    };

    private readonly ILogger<DnsClientWatcherService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly DnsWatcherConfig _config;
    private readonly Dictionary<int, string> _processNameCache = new();
    private readonly List<DnsClientEvent> _eventQueue = new();
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

    public DnsClientWatcherService(
        ILogger<DnsClientWatcherService> logger,
        IHostApplicationLifetime lifetime,
        DnsWatcherConfig config)
    {
        _logger = logger;
        _lifetime = lifetime;
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
            Console.WriteLine("  DNS Client Real-Time ETW Watcher");
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

        // Log-Datei oeffnen
        if (_config.LogFile != null)
        {
            _logWriter = new StreamWriter(_config.LogFile, append: true) { AutoFlush = true };
        }

        var sessionName = $"DnsClientWatcher_{Environment.ProcessId}";

        // Alte Session beenden
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
        _session.EnableProvider(DnsClientProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);

        // Stats-Timer starten (alle 5 Minuten)
        _statsTimer = new Timer(_ => WriteStats(), null, 5 * 60 * 1000, 5 * 60 * 1000);

        _logger.LogInformation("DnsClientWatcher gestartet");

        if (isConsole && !_config.Quiet)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK] DNS Client Provider aktiviert");
            Console.WriteLine();
            Console.WriteLine("==========================================");
            Console.WriteLine("  LIVE - Warte auf DNS Events (Ctrl+C)");
            Console.WriteLine("==========================================");
            Console.ResetColor();
            Console.WriteLine();

            if (!_config.JsonOutput)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Legende: QUERY=Anfrage, RESPONSE=Antwort, CACHE=Aus Cache");
                Console.WriteLine();
                Console.ResetColor();
            }
        }

        // ETW Processing in separatem Task starten
        var processingTask = Task.Run(() => _session.Source.Process(), stoppingToken);

        // Warten auf Cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal bei Shutdown
        }

        // Cleanup
        _session.Stop();
        await processingTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DnsClientWatcher wird beendet. {Count:N0} Events verarbeitet.", _eventCount);

        _session?.Stop();
        _statsTimer?.Dispose();
        _flushTimer?.Dispose();

        FlushEventQueue();

        _insertCmd?.Dispose();
        _sqliteConn?.Close();
        _logWriter?.Close();

        if (Environment.UserInteractive && !_config.Quiet)
        {
            Console.WriteLine("Fertig.");
        }

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
                event_id INTEGER,
                process_id INTEGER,
                process_name TEXT,
                query_name TEXT,
                query_type TEXT,
                status TEXT,
                query_results TEXT,
                dns_server TEXT,
                interface_index INTEGER,
                error_category TEXT,
                raw_payload TEXT,
                correlation_id INTEGER,
                parent_correlation_id INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_timestamp ON dns_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_process_name ON dns_events(process_name);
            CREATE INDEX IF NOT EXISTS idx_query_name ON dns_events(query_name);
            CREATE INDEX IF NOT EXISTS idx_query_results ON dns_events(query_results);
            CREATE INDEX IF NOT EXISTS idx_error_category ON dns_events(error_category);
            CREATE INDEX IF NOT EXISTS idx_correlation_id ON dns_events(correlation_id);
        ";

        using var cmd = new SqliteCommand(createTable, _sqliteConn);
        cmd.ExecuteNonQuery();

        _insertCmd = new SqliteCommand(@"
            INSERT INTO dns_events
            (timestamp, event_type, event_id, process_id, process_name, query_name, query_type, status, query_results, dns_server, interface_index, error_category, raw_payload, correlation_id, parent_correlation_id)
            VALUES
            (@timestamp, @event_type, @event_id, @process_id, @process_name, @query_name, @query_type, @status, @query_results, @dns_server, @interface_index, @error_category, @raw_payload, @correlation_id, @parent_correlation_id)
        ", _sqliteConn);
        _insertCmd.Parameters.Add("@timestamp", SqliteType.Text);
        _insertCmd.Parameters.Add("@event_type", SqliteType.Text);
        _insertCmd.Parameters.Add("@event_id", SqliteType.Integer);
        _insertCmd.Parameters.Add("@process_id", SqliteType.Integer);
        _insertCmd.Parameters.Add("@process_name", SqliteType.Text);
        _insertCmd.Parameters.Add("@query_name", SqliteType.Text);
        _insertCmd.Parameters.Add("@query_type", SqliteType.Text);
        _insertCmd.Parameters.Add("@status", SqliteType.Text);
        _insertCmd.Parameters.Add("@query_results", SqliteType.Text);
        _insertCmd.Parameters.Add("@dns_server", SqliteType.Text);
        _insertCmd.Parameters.Add("@interface_index", SqliteType.Integer);
        _insertCmd.Parameters.Add("@error_category", SqliteType.Text);
        _insertCmd.Parameters.Add("@raw_payload", SqliteType.Text);
        _insertCmd.Parameters.Add("@correlation_id", SqliteType.Integer);
        _insertCmd.Parameters.Add("@parent_correlation_id", SqliteType.Integer);
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

    private const int CurrentSchemaVersion = 4;

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

        // Migration von Version 2 -> 3: raw_payload hinzufuegen
        if (currentVersion < 3)
        {
            using var checkCmd = new SqliteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='dns_events'", _sqliteConn);
            var tableExists = checkCmd.ExecuteScalar() != null;

            if (tableExists)
            {
                var existingColumns = new HashSet<string>();
                using (var cmd = new SqliteCommand("PRAGMA table_info(dns_events)", _sqliteConn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1).ToLowerInvariant());
                    }
                }

                if (!existingColumns.Contains("raw_payload"))
                {
                    using var alterCmd = new SqliteCommand(
                        "ALTER TABLE dns_events ADD COLUMN raw_payload TEXT", _sqliteConn);
                    alterCmd.ExecuteNonQuery();

                    if (Environment.UserInteractive && !_config.Quiet)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("[SQLite] Schema migriert v2->v3: raw_payload Spalte hinzugefuegt");
                        Console.ResetColor();
                    }
                    _logger.LogInformation("Schema migriert v2->v3: raw_payload Spalte hinzugefuegt");
                }
            }
        }

        // Migration von Version 3 -> 4: correlation_id und parent_correlation_id hinzufuegen
        if (currentVersion < 4)
        {
            using var checkCmd = new SqliteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='dns_events'", _sqliteConn);
            var tableExists = checkCmd.ExecuteScalar() != null;

            if (tableExists)
            {
                var existingColumns = new HashSet<string>();
                using (var cmd = new SqliteCommand("PRAGMA table_info(dns_events)", _sqliteConn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1).ToLowerInvariant());
                    }
                }

                if (!existingColumns.Contains("correlation_id"))
                {
                    using var alterCmd = new SqliteCommand(
                        "ALTER TABLE dns_events ADD COLUMN correlation_id INTEGER", _sqliteConn);
                    alterCmd.ExecuteNonQuery();

                    using var indexCmd = new SqliteCommand(
                        "CREATE INDEX IF NOT EXISTS idx_correlation_id ON dns_events(correlation_id)", _sqliteConn);
                    indexCmd.ExecuteNonQuery();
                }

                if (!existingColumns.Contains("parent_correlation_id"))
                {
                    using var alterCmd = new SqliteCommand(
                        "ALTER TABLE dns_events ADD COLUMN parent_correlation_id INTEGER", _sqliteConn);
                    alterCmd.ExecuteNonQuery();
                }

                if (!existingColumns.Contains("correlation_id") || !existingColumns.Contains("parent_correlation_id"))
                {
                    if (Environment.UserInteractive && !_config.Quiet)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("[SQLite] Schema migriert v3->v4: correlation_id/parent_correlation_id Spalten hinzugefuegt");
                        Console.ResetColor();
                    }
                    _logger.LogInformation("Schema migriert v3->v4: correlation_id/parent_correlation_id Spalten hinzugefuegt");
                }
            }
        }

        // Aktuelle Version setzen
        SetSchemaVersion(CurrentSchemaVersion);
    }

    private void QueueEvent(DnsClientEvent evt)
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
                _insertCmd.Parameters["@event_id"].Value = evt.EventId;
                _insertCmd.Parameters["@process_id"].Value = evt.ProcessId;
                _insertCmd.Parameters["@process_name"].Value = evt.ProcessName;
                _insertCmd.Parameters["@query_name"].Value = evt.QueryName ?? (object)DBNull.Value;
                _insertCmd.Parameters["@query_type"].Value = evt.QueryType ?? (object)DBNull.Value;
                _insertCmd.Parameters["@status"].Value = evt.Status ?? (object)DBNull.Value;
                _insertCmd.Parameters["@query_results"].Value = evt.QueryResults ?? (object)DBNull.Value;
                _insertCmd.Parameters["@dns_server"].Value = evt.DnsServer ?? (object)DBNull.Value;
                _insertCmd.Parameters["@interface_index"].Value = evt.InterfaceIndex ?? (object)DBNull.Value;
                _insertCmd.Parameters["@error_category"].Value = evt.ErrorCategory ?? (object)DBNull.Value;
                _insertCmd.Parameters["@raw_payload"].Value = evt.RawPayload ?? (object)DBNull.Value;
                _insertCmd.Parameters["@correlation_id"].Value = evt.CorrelationId ?? (object)DBNull.Value;
                _insertCmd.Parameters["@parent_correlation_id"].Value = evt.ParentCorrelationId ?? (object)DBNull.Value;
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

    private string GetProcessName(int processId)
    {
        if (processId <= 0) return "?";

        if (_processNameCache.TryGetValue(processId, out var cached))
            return cached;

        try
        {
            using var proc = Process.GetProcessById(processId);
            var name = proc.ProcessName;
            _processNameCache[processId] = name;
            return name;
        }
        catch
        {
            return $"PID:{processId}";
        }
    }

    private static string FormatQueryResults(string? results)
    {
        if (string.IsNullOrEmpty(results)) return "";

        return System.Text.RegularExpressions.Regex.Replace(
            results,
            @"type:\s*(\d+)\s+",
            match =>
            {
                if (int.TryParse(match.Groups[1].Value, out int typeNum))
                {
                    var typeName = QueryTypes.TryGetValue(typeNum, out var name) ? name : $"TYPE{typeNum}";
                    return $"{typeName} ";
                }
                return match.Value;
            });
    }

    private static ConsoleColor GetStatusColor(int statusNum)
    {
        return statusNum switch
        {
            0 => ConsoleColor.Green,
            87 => ConsoleColor.DarkGray,
            9701 => ConsoleColor.DarkYellow,
            9501 => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Red
        };
    }

    private static string GetEventName(int eventId) => eventId switch
    {
        1001 => "SERVER_LIST",
        1015 => "SERVER_TIMEOUT",
        1016 => "NAME_ERROR",
        3006 => "QUERY",
        3008 => "COMPLETE",
        3009 => "SEND",
        3010 => "SEND_TO",
        3011 => "RECV",
        3016 => "CACHE_LOOKUP",
        3018 => "CACHE",
        3019 => "WIRE_QUERY",
        3020 => "RESPONSE",
        _ => $"EVENT_{eventId}"
    };

    // Dekodiert SOCKADDR_IN Struktur aus Base64 zu IP-Adresse
    private static string? DecodeSockAddr(object? addressObj)
    {
        if (addressObj == null) return null;
        try
        {
            byte[] bytes;
            if (addressObj is byte[] byteArray)
                bytes = byteArray;
            else if (addressObj is string base64)
                bytes = Convert.FromBase64String(base64);
            else
                return null;

            if (bytes.Length < 8) return null;

            // AF_INET = 2 (IPv4)
            var family = BitConverter.ToUInt16(bytes, 0);
            if (family == 2 && bytes.Length >= 8)
            {
                // IPv4: Bytes 4-7 sind die IP-Adresse
                return $"{bytes[4]}.{bytes[5]}.{bytes[6]}.{bytes[7]}";
            }
            // AF_INET6 = 23 (IPv6)
            else if (family == 23 && bytes.Length >= 24)
            {
                // IPv6: Bytes 8-23 sind die IP-Adresse
                var ipv6Bytes = new byte[16];
                Array.Copy(bytes, 8, ipv6Bytes, 0, 16);
                return new System.Net.IPAddress(ipv6Bytes).ToString();
            }
            return null;
        }
        catch { return null; }
    }

    private void ProcessEvent(TraceEvent evt)
    {
        if (evt.ID == 0) return;

        var timestamp = evt.TimeStamp.ToString("HH:mm:ss.fff");
        var eventId = (int)evt.ID;
        var processId = evt.ProcessID;
        var processName = GetProcessName(processId);

        string? queryName = null, queryResults = null, dnsServer = null;
        string? qtype = null, status = null;
        int qtypeNum = 0, statusNum = 0, interfaceIndex = 0;
        long? correlationId = null, parentCorrelationId = null;

        try
        {
            // Event 1001: SERVER_LIST - DNS Server Konfiguration
            if (eventId == 1001)
            {
                var iface = evt.PayloadByName("Interface")?.ToString();
                var address = evt.PayloadByName("Address");
                var serverIp = DecodeSockAddr(address);
                var totalCount = evt.PayloadByName("TotalServerCount")?.ToString();
                var index = evt.PayloadByName("Index")?.ToString();
                var dynamic = evt.PayloadByName("DynamicAddress");
                var isDynamic = dynamic?.ToString() == "1";

                // Interface als queryName, Server-IP als queryResults
                queryName = iface;
                queryResults = serverIp;
                dnsServer = serverIp;
                // Format: "Server 1/2 (DHCP)" oder "Server 1/2 (static)"
                status = $"Server {index}/{totalCount} ({(isDynamic ? "DHCP" : "static")})";
            }
            // Event 1015: SERVER_TIMEOUT - DNS-Server antwortet nicht
            else if (eventId == 1015)
            {
                queryName = evt.PayloadByName("QueryName")?.ToString()?.TrimEnd('.');
                var address = evt.PayloadByName("Address");
                var serverIp = DecodeSockAddr(address);
                dnsServer = serverIp;
                status = "Timeout";
                statusNum = 1460; // Windows Timeout-Code
            }
            // Event 1016: NAME_ERROR - DNS-Namensaufloesung fehlgeschlagen (z.B. NXDomain)
            else if (eventId == 1016)
            {
                queryName = evt.PayloadByName("QueryName")?.ToString()?.TrimEnd('.');
                var address = evt.PayloadByName("Address");
                var serverIp = DecodeSockAddr(address);
                dnsServer = serverIp;
                status = "NXDomain";
                statusNum = 9003; // Windows NXDomain-Code
            }
            // Event 3010: SEND_TO - Query an spezifischen Server gesendet
            else if (eventId == 3010)
            {
                queryName = evt.PayloadByName("QueryName")?.ToString()?.TrimEnd('.');
                dnsServer = evt.PayloadByName("DnsServerIpAddress")?.ToString();

                var qtypeVal = evt.PayloadByName("QueryType");
                if (qtypeVal != null && int.TryParse(qtypeVal.ToString()?.Trim(), out int qt))
                {
                    qtypeNum = qt;
                    qtype = QueryTypes.TryGetValue(qt, out var qtName) ? qtName : qt.ToString();
                }
            }
            // Event 3011: RESPONSE_EX - Extended Response mit Status
            else if (eventId == 3011)
            {
                queryName = evt.PayloadByName("QueryName")?.ToString()?.TrimEnd('.');
                dnsServer = evt.PayloadByName("DnsServerIpAddress")?.ToString();

                var qtypeVal = evt.PayloadByName("QueryType");
                if (qtypeVal != null && int.TryParse(qtypeVal.ToString()?.Trim(), out int qt))
                {
                    qtypeNum = qt;
                    qtype = QueryTypes.TryGetValue(qt, out var qtName) ? qtName : qt.ToString();
                }

                var statusVal = evt.PayloadByName("ResponseStatus");
                if (statusVal != null && int.TryParse(statusVal.ToString()?.Trim(), out int st))
                {
                    statusNum = st;
                    status = StatusCodes.TryGetValue(st, out var stName) ? stName : st.ToString();
                }
            }
            else
            {
                queryName = evt.PayloadByName("QueryName")?.ToString()?.TrimEnd('.');

                var qtypeVal = evt.PayloadByName("QueryType");
                if (qtypeVal != null && int.TryParse(qtypeVal.ToString()?.Trim(), out int qt))
                {
                    qtypeNum = qt;
                    qtype = QueryTypes.TryGetValue(qt, out var qtName) ? qtName : qt.ToString();
                }

                var statusVal = evt.PayloadByName("Status") ?? evt.PayloadByName("QueryStatus");
                if (statusVal != null && int.TryParse(statusVal.ToString()?.Trim(), out int st))
                {
                    statusNum = st;
                    status = StatusCodes.TryGetValue(st, out var stName) ? stName : st.ToString();
                }

                queryResults = evt.PayloadByName("QueryResults")?.ToString();
                dnsServer = evt.PayloadByName("DNSServerAddress")?.ToString();

                var ifIdxVal = evt.PayloadByName("InterfaceIndex");
                if (ifIdxVal != null && int.TryParse(ifIdxVal.ToString()?.Trim(), out int idx))
                    interfaceIndex = idx;
            }

            // QueryBlob und ParentBlob extrahieren (Korrelations-IDs)
            var queryBlobVal = evt.PayloadByName("QueryBlob");
            if (queryBlobVal != null && long.TryParse(queryBlobVal.ToString()?.Trim(), out long qb))
                correlationId = qb;

            var parentBlobVal = evt.PayloadByName("ParentBlob");
            if (parentBlobVal != null && long.TryParse(parentBlobVal.ToString()?.Trim(), out long pb) && pb != 0)
                parentCorrelationId = pb;
        }
        catch { }

        // Fehler-Kategorie bestimmen
        string? errorCategory = null;
        if (ConfigErrorCodes.Contains(statusNum))
            errorCategory = "CONFIG_ERROR";
        else if (ClientErrorCodes.Contains(statusNum))
            errorCategory = "CLIENT_ERROR";

        // JSON Output
        if (_config.JsonOutput)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = evt.TimeStamp,
                eventId,
                eventName = GetEventName(eventId),
                processId,
                processName,
                queryName,
                queryType = qtype,
                status,
                queryResults = FormatQueryResults(queryResults),
                dnsServer,
                interfaceIndex = interfaceIndex > 0 ? interfaceIndex : (int?)null,
                errorCategory,
                correlationId,
                parentCorrelationId
            });
            Console.WriteLine(json);
            _logWriter?.WriteLine(json);
            return;
        }

        // Log to file
        if (_logWriter != null)
        {
            var line = $"{evt.TimeStamp:O}\t{eventId}\t{GetEventName(eventId)}\t{queryName}\t{qtype}\t{status}\t{FormatQueryResults(queryResults)}";
            _logWriter.WriteLine(line);
        }

        // SQLite speichern (alle Events)
        if (_sqliteConn != null)
        {
            // raw_payload nur bei unbekannten Events speichern
            string? rawPayload = null;
            var isKnownEvent = eventId == 1001 || eventId == 1015 || eventId == 1016 || eventId == 3006 || eventId == 3008 || eventId == 3009 || eventId == 3010 || eventId == 3011 || eventId == 3016 || eventId == 3018 || eventId == 3019 || eventId == 3020;
            if (!isKnownEvent)
            {
                var payloadDict = new Dictionary<string, object?>();
                for (int i = 0; i < evt.PayloadNames.Length; i++)
                    payloadDict[evt.PayloadNames[i]] = evt.PayloadValue(i);
                rawPayload = System.Text.Json.JsonSerializer.Serialize(payloadDict);
            }

            var dnsEvent = new DnsClientEvent(
                evt.TimeStamp, GetEventName(eventId), eventId, processId, processName,
                queryName, qtype, status, FormatQueryResults(queryResults), dnsServer,
                interfaceIndex > 0 ? interfaceIndex : null, errorCategory, rawPayload,
                correlationId, parentCorrelationId);
            QueueEvent(dnsEvent);
            Interlocked.Increment(ref _eventCount);
            CheckPeriodicCleanup();
        }

        // Console-Ausgabe
        if (_config.Quiet || !Environment.UserInteractive)
            return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{timestamp}] ");

        switch (eventId)
        {
            case 1001:
                // SERVER_LIST: DNS Server Konfiguration
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("SERVER_LIST ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");  // Interface
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(queryResults ?? "?");  // Server IP
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"({status})");
                break;

            case 1015:
                // SERVER_TIMEOUT: DNS-Server antwortet nicht
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("SERVER_TIMEOUT ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" <- ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(dnsServer ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 1016:
                // NAME_ERROR: DNS-Namensaufloesung fehlgeschlagen (NXDomain)
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("NAME_ERROR ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" <- ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(dnsServer ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 3006:
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("QUERY    ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}]");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 3010:
                // SEND_TO: Query an spezifischen Server gesendet
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("SEND_TO  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}]");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(dnsServer ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 3011:
                // RECV: Antwort vom DNS-Server empfangen
                var recvColor = statusNum == 0 ? ConsoleColor.Green :
                    (errorCategory == "CLIENT_ERROR" ? ConsoleColor.DarkYellow : ConsoleColor.Red);
                Console.ForegroundColor = recvColor;
                Console.Write("RECV     ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = GetStatusColor(statusNum);
                Console.Write(status ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" <- ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(dnsServer ?? "?");
                break;

            case 3016:
                // CACHE_LOOKUP: Cache-Abfrage gestartet
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("CACHE_LOOKUP ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}]");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 3019:
                // WIRE_QUERY: Query auf Netzwerk gesendet
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("WIRE_QUERY ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}]");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;

            case 3008:
                var completeColor = statusNum == 0 ? ConsoleColor.Green :
                    (errorCategory == "CLIENT_ERROR" ? ConsoleColor.DarkYellow : ConsoleColor.Red);
                Console.ForegroundColor = completeColor;
                Console.Write("COMPLETE ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = GetStatusColor(statusNum);
                Console.Write(status ?? "?");
                if (errorCategory != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({errorCategory})");
                }
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {FormatQueryResults(queryResults)}");
                }
                Console.WriteLine();
                break;

            case 3009:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("SEND     ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(dnsServer ?? "?");
                break;

            case 3018:
                var cacheColor = statusNum == 0 || statusNum == 87 ? ConsoleColor.Blue :
                    (errorCategory == "CLIENT_ERROR" ? ConsoleColor.DarkYellow : ConsoleColor.Red);
                Console.ForegroundColor = cacheColor;
                Console.Write("CACHE    ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = GetStatusColor(statusNum);
                Console.Write(status ?? "?");
                if (errorCategory != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({errorCategory})");
                }
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {FormatQueryResults(queryResults)}");
                }
                Console.WriteLine();
                break;

            case 3020:
                var respColor = statusNum == 0 ? ConsoleColor.Green :
                    (errorCategory == "CLIENT_ERROR" ? ConsoleColor.DarkYellow : ConsoleColor.Red);
                Console.ForegroundColor = respColor;
                Console.Write("RESPONSE ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = GetStatusColor(statusNum);
                Console.Write(status ?? "?");
                if (errorCategory != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({errorCategory})");
                }
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {FormatQueryResults(queryResults)}");
                }
                Console.WriteLine();
                break;

            default:
                // Alle anderen Events
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"EVENT_{eventId,-3} ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                if (!string.IsNullOrEmpty(qtype))
                {
                    Console.Write(" ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"[{qtype}]");
                }
                if (!string.IsNullOrEmpty(status))
                {
                    Console.Write(" ");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(status);
                }
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({processName})");
                break;
        }

        Console.ResetColor();

        if (_config.ShowRaw)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    [PayloadNames: {string.Join(", ", evt.PayloadNames)}]");
            for (int i = 0; i < evt.PayloadNames.Length; i++)
                Console.WriteLine($"    {evt.PayloadNames[i]} = {evt.PayloadValue(i)}");
            Console.ResetColor();
        }
    }
}

// Event-Struktur
public record DnsClientEvent(
    DateTime Timestamp,
    string EventType,
    int EventId,
    int ProcessId,
    string ProcessName,
    string? QueryName,
    string? QueryType,
    string? Status,
    string? QueryResults,
    string? DnsServer,
    int? InterfaceIndex,
    string? ErrorCategory = null,
    string? RawPayload = null,
    long? CorrelationId = null,
    long? ParentCorrelationId = null
);
