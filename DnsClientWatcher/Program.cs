using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Data.Sqlite;
using System.Net;

namespace DnsClientWatcher;

class Program
{
    // Microsoft-Windows-DNS-Client Provider GUID
    private static readonly Guid DnsClientProviderGuid = new("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D");

    private static readonly Dictionary<int, string> QueryTypes = new()
    {
        { 1, "A" }, { 2, "NS" }, { 5, "CNAME" }, { 6, "SOA" },
        { 12, "PTR" }, { 15, "MX" }, { 16, "TXT" }, { 28, "AAAA" },
        { 33, "SRV" }, { 255, "ANY" }, { 65, "HTTPS" }
    };

    // DNS Client Status Codes
    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        { 0, "OK" },
        { 9003, "NXDomain" },
        { 9501, "NoRecords" },
        { 9560, "Timeout" },
        { 1460, "Timeout" },
        { 87, "InvalidParam" },
        { 1214, "InvalidName" }
    };

    private static bool _showRaw = false;
    private static bool _jsonOutput = false;
    private static string? _logFile = null;
    private static StreamWriter? _logWriter = null;
    private static string? _sqliteDb = null;
    private static SqliteConnection? _sqliteConn = null;
    private static int _retentionDays = 30;

    // SQLite Datenbank initialisieren
    private static void InitSqlite(string dbPath)
    {
        _sqliteConn = new SqliteConnection($"Data Source={dbPath}");
        _sqliteConn.Open();

        // Tabelle erstellen
        var createTable = @"
            CREATE TABLE IF NOT EXISTS dns_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                event_type TEXT NOT NULL,
                event_id INTEGER,
                query_name TEXT,
                query_type TEXT,
                status TEXT,
                query_results TEXT,
                dns_server TEXT,
                interface_index INTEGER
            );

            -- Indices fuer schnelle Suche
            CREATE INDEX IF NOT EXISTS idx_timestamp ON dns_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_query_name ON dns_events(query_name);
            CREATE INDEX IF NOT EXISTS idx_query_results ON dns_events(query_results);
        ";

        using var cmd = new SqliteCommand(createTable, _sqliteConn);
        cmd.ExecuteNonQuery();

        // Alte Eintraege loeschen (Retention)
        CleanupOldEntries();
    }

    // Alte Eintraege loeschen
    private static void CleanupOldEntries()
    {
        if (_sqliteConn == null) return;

        var cutoff = DateTime.Now.AddDays(-_retentionDays).ToString("o");
        var deleteOld = $"DELETE FROM dns_events WHERE timestamp < @cutoff";

        using var cmd = new SqliteCommand(deleteOld, _sqliteConn);
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        var deleted = cmd.ExecuteNonQuery();

        if (deleted > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[SQLite] {deleted} Eintraege aelter als {_retentionDays} Tage geloescht");
            Console.ResetColor();
        }
    }

    // Event in SQLite speichern
    private static void InsertEvent(
        DateTime timestamp,
        string eventType,
        int eventId,
        string? queryName,
        string? queryType,
        string? status,
        string? queryResults,
        string? dnsServer,
        int? interfaceIndex)
    {
        if (_sqliteConn == null) return;

        try
        {
            var insert = @"
                INSERT INTO dns_events
                (timestamp, event_type, event_id, query_name, query_type, status, query_results, dns_server, interface_index)
                VALUES
                (@timestamp, @event_type, @event_id, @query_name, @query_type, @status, @query_results, @dns_server, @interface_index)
            ";

            using var cmd = new SqliteCommand(insert, _sqliteConn);
            cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@event_type", eventType);
            cmd.Parameters.AddWithValue("@event_id", eventId);
            cmd.Parameters.AddWithValue("@query_name", queryName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@query_type", queryType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@status", status ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@query_results", queryResults ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@dns_server", dnsServer ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@interface_index", interfaceIndex ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SQLite] Fehler: {ex.Message}");
            Console.ResetColor();
        }
    }

    static void Main(string[] args)
    {
        // Args parsen
        _showRaw = args.Contains("--raw") || args.Contains("-r");
        _jsonOutput = args.Contains("--json") || args.Contains("-j");

        var logArg = args.FirstOrDefault(a => a.StartsWith("--log=") || a.StartsWith("-l="));
        if (logArg != null)
        {
            _logFile = logArg.Split('=')[1];
            _logWriter = new StreamWriter(_logFile, append: true) { AutoFlush = true };
        }

        // SQLite Argument
        var sqliteArg = args.FirstOrDefault(a => a.StartsWith("--sqlite=") || a.StartsWith("-s="));
        if (sqliteArg != null)
        {
            _sqliteDb = sqliteArg.Split('=')[1];
        }

        // Retention Argument (Tage)
        var retentionArg = args.FirstOrDefault(a => a.StartsWith("--retention="));
        if (retentionArg != null && int.TryParse(retentionArg.Split('=')[1], out int days))
        {
            _retentionDays = days;
        }

        if (args.Contains("--help") || args.Contains("-h"))
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

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("  DNS Client Real-Time ETW Watcher");
        Console.WriteLine("========================================");
        Console.ResetColor();
        Console.WriteLine();

        // SQLite initialisieren falls angegeben
        if (_sqliteDb != null)
        {
            try
            {
                InitSqlite(_sqliteDb);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] SQLite Datenbank: {_sqliteDb}");
                Console.WriteLine($"     Retention: {_retentionDays} Tage");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FEHLER] SQLite: {ex.Message}");
                Console.ResetColor();
                return;
            }
        }

        var sessionName = $"DnsClientWatcher_{Environment.ProcessId}";

        // Alte Session beenden falls vorhanden
        try
        {
            using var oldSession = TraceEventSession.GetActiveSession(sessionName);
            oldSession?.Stop();
        }
        catch { }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Starte ETW Session: {sessionName}");
        Console.ResetColor();

        using var session = new TraceEventSession(sessionName);

        // Ctrl+C Handler
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Beende...");
            Console.ResetColor();
            session.Stop();
        };

        // Event Handler registrieren
        session.Source.Dynamic.All += ProcessEvent;

        // DNS Client Provider aktivieren
        session.EnableProvider(DnsClientProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[OK] DNS Client Provider aktiviert");
        Console.WriteLine();
        Console.WriteLine("==========================================");
        Console.WriteLine("  LIVE - Warte auf DNS Events (Ctrl+C)");
        Console.WriteLine("==========================================");
        Console.ResetColor();
        Console.WriteLine();

        if (!_jsonOutput)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Legende: QUERY=Anfrage, RESPONSE=Antwort, CACHE=Aus Cache");
            Console.WriteLine();
            Console.ResetColor();
        }

        // Event Processing starten (blockiert bis Stop)
        session.Source.Process();

        _logWriter?.Close();
        _sqliteConn?.Close();
        Console.WriteLine("Fertig.");
    }

    private static void ProcessEvent(TraceEvent evt)
    {
        // Header Events ignorieren
        if (evt.ID == 0) return;

        var timestamp = evt.TimeStamp.ToString("HH:mm:ss.fff");
        var eventId = (int)evt.ID;

        // Event-Daten extrahieren
        string? queryName = null, queryResults = null, dnsServer = null;
        string? qtype = null, status = null;
        int qtypeNum = 0, statusNum = 0, interfaceIndex = 0;

        try
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
            {
                interfaceIndex = idx;
            }
        }
        catch { }

        // JSON Output
        if (_jsonOutput)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = evt.TimeStamp,
                eventId,
                eventName = GetEventName(eventId),
                queryName,
                queryType = qtype,
                status,
                queryResults,
                dnsServer,
                interfaceIndex = interfaceIndex > 0 ? interfaceIndex : (int?)null
            });
            Console.WriteLine(json);
            _logWriter?.WriteLine(json);
            return;
        }

        // Nur interessante Events anzeigen
        // 3006 = Query Start, 3008 = Query Response, 3009 = Query to Server, 3018 = Cache Hit, 3020 = Response
        if (eventId != 3006 && eventId != 3008 && eventId != 3009 && eventId != 3018 && eventId != 3020 && !_showRaw)
            return;

        // Formatierte Ausgabe
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{timestamp}] ");

        switch (eventId)
        {
            case 3006: // Query Start
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("QUERY    ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{qtype ?? "?"}]");
                break;

            case 3008: // Query Complete
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("COMPLETE ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = statusNum == 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(status ?? "?");
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {queryResults}");
                }
                Console.WriteLine();
                break;

            case 3009: // Query to DNS Server
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("SEND     ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" -> ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(dnsServer ?? "?");
                break;

            case 3018: // Cache Hit
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("CACHE    ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = statusNum == 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(status ?? "?");
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {queryResults}");
                }
                Console.WriteLine();
                break;

            case 3020: // Query Response
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("RESPONSE ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(queryName ?? "?");
                Console.Write(" ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{qtype ?? "?"}] ");
                Console.ForegroundColor = statusNum == 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(status ?? "?");
                if (!string.IsNullOrEmpty(queryResults))
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write($" => {queryResults}");
                }
                Console.WriteLine();
                break;

            default:
                if (_showRaw)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"EVENT_{eventId} {queryName ?? "?"}");
                }
                break;
        }

        Console.ResetColor();

        // Log to file
        if (_logWriter != null)
        {
            var line = $"{evt.TimeStamp:O}\t{eventId}\t{GetEventName(eventId)}\t{queryName}\t{qtype}\t{status}\t{queryResults}";
            _logWriter.WriteLine(line);
        }

        // SQLite speichern
        if (_sqliteConn != null && (eventId == 3006 || eventId == 3008 || eventId == 3018 || eventId == 3020))
        {
            var eventType = GetEventName(eventId);
            InsertEvent(evt.TimeStamp, eventType, eventId, queryName, qtype, status, queryResults, dnsServer, interfaceIndex > 0 ? interfaceIndex : null);
        }

        // Raw output
        if (_showRaw)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = 0; i < evt.PayloadNames.Length; i++)
            {
                Console.WriteLine($"    {evt.PayloadNames[i]} = {evt.PayloadValue(i)}");
            }
            Console.ResetColor();
        }
    }

    private static string GetEventName(int eventId) => eventId switch
    {
        3006 => "QUERY",
        3008 => "COMPLETE",
        3009 => "SEND",
        3018 => "CACHE",
        3020 => "RESPONSE",
        _ => $"EVENT_{eventId}"
    };

    private static void PrintHelp()
    {
        Console.WriteLine(@"
DNS Client Real-Time ETW Watcher
================================

Zeigt DNS Client Events in Echtzeit an.
Ueberwacht lokale DNS-Auflösungen auf einem Client oder Server.
Muss als Administrator ausgefuehrt werden.

Verwendung:
  DnsClientWatcher.exe [optionen]

Optionen:
  -r, --raw          Zeige alle Event-Properties
  -j, --json         Ausgabe als JSON (eine Zeile pro Event)
  -l, --log=X        Schreibe Events zusaetzlich in Datei X
  -s, --sqlite=X     Speichere Events in SQLite Datenbank X
      --retention=N  Behalte Eintraege fuer N Tage (default: 30)
  -h, --help         Diese Hilfe anzeigen

Beispiele:
  DnsClientWatcher.exe
  DnsClientWatcher.exe --json --log=C:\Logs\dnsclient.jsonl
  DnsClientWatcher.exe --sqlite=C:\Logs\dnsclient.db
  DnsClientWatcher.exe --sqlite=C:\Logs\dnsclient.db --retention=90
  DnsClientWatcher.exe -r

SQLite-Abfragen (nach dem Sammeln):
  -- Aufgeloeste IP suchen:
  SELECT * FROM dns_events WHERE query_results LIKE '%142.250.185.110%';

  -- Domain suchen:
  SELECT * FROM dns_events WHERE query_name LIKE '%google%';

  -- Cache Hits:
  SELECT * FROM dns_events WHERE event_type = 'CACHE';

Event-Typen:
  3006 = Query gestartet (Anfrage an DNS-Client)
  3008 = Query beendet (mit Status und Ergebnis)
  3009 = Query zu DNS-Server gesendet
  3018 = Cache Hit (aus lokalem Cache beantwortet)
  3020 = Response vom DNS-Server erhalten

Unterschied zu DnsServerWatcher:
  - DnsServerWatcher laeuft auf dem DNS-Server und zeigt alle Anfragen von allen Clients
  - DnsClientWatcher laeuft auf einem Client und zeigt dessen eigene DNS-Anfragen
");
    }
}
