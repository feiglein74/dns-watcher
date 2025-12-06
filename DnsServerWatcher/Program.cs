using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Data.Sqlite;
using System.Net;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;

namespace DnsServerWatcher;

class Program
{
    // Microsoft-Windows-DNSServer Provider GUID
    private static readonly Guid DnsServerProviderGuid = new("EB79061A-A566-4698-9119-3ED2807060E7");

    private static readonly Dictionary<int, string> QueryTypes = new()
    {
        { 1, "A" }, { 2, "NS" }, { 5, "CNAME" }, { 6, "SOA" },
        { 12, "PTR" }, { 15, "MX" }, { 16, "TXT" }, { 28, "AAAA" },
        { 33, "SRV" }, { 255, "ANY" }, { 65, "HTTPS" }
    };

    private static readonly Dictionary<int, string> ResponseCodes = new()
    {
        { 0, "OK" }, { 1, "FormErr" }, { 2, "ServFail" },
        { 3, "NXDomain" }, { 4, "NotImpl" }, { 5, "Refused" }
    };

    private static bool _showRaw = false;
    private static bool _jsonOutput = false;
    private static string? _logFile = null;
    private static StreamWriter? _logWriter = null;
    private static string? _sqliteDb = null;
    private static SqliteConnection? _sqliteConn = null;
    private static int _retentionDays = 30;

    // DNS Packet Parser - extrahiert Records aus der Answer Section
    // Nutzt die DNS Library (https://github.com/kapetan/dns) fuer robustes Parsing
    private static List<string> ParseDnsAnswers(byte[] packetData)
    {
        var answers = new List<string>();
        if (packetData == null || packetData.Length < 12) return answers;

        try
        {
            var response = Response.FromArray(packetData);

            foreach (var record in response.AnswerRecords)
            {
                switch (record)
                {
                    case IPAddressResourceRecord ipRecord:
                        // A und AAAA Records
                        answers.Add(ipRecord.IPAddress.ToString());
                        break;
                    case CanonicalNameResourceRecord cnameRecord:
                        // CNAME Records
                        answers.Add($"CNAME:{cnameRecord.CanonicalDomainName}");
                        break;
                    case MailExchangeResourceRecord mxRecord:
                        // MX Records
                        answers.Add($"MX:{mxRecord.Preference} {mxRecord.ExchangeDomainName}");
                        break;
                    case NameServerResourceRecord nsRecord:
                        // NS Records
                        answers.Add($"NS:{nsRecord.NSDomainName}");
                        break;
                    case PointerResourceRecord ptrRecord:
                        // PTR Records (Reverse DNS)
                        answers.Add($"PTR:{ptrRecord.PointerDomainName}");
                        break;
                    case TextResourceRecord txtRecord:
                        // TXT Records
                        answers.Add($"TXT:{txtRecord.ToStringTextData()}");
                        break;
                    default:
                        // Andere Record-Typen: Zeige Typ und Rohdaten
                        answers.Add($"{record.Type}:{record}");
                        break;
                }
            }
        }
        catch
        {
            // Bei Parse-Fehlern: leere Liste zurueckgeben
        }

        return answers;
    }

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
                client_ip TEXT,
                query_name TEXT,
                query_type TEXT,
                response_code TEXT,
                resolved_ips TEXT,
                zone TEXT
            );

            -- Indices fuer schnelle Suche
            CREATE INDEX IF NOT EXISTS idx_timestamp ON dns_events(timestamp);
            CREATE INDEX IF NOT EXISTS idx_client_ip ON dns_events(client_ip);
            CREATE INDEX IF NOT EXISTS idx_query_name ON dns_events(query_name);
            CREATE INDEX IF NOT EXISTS idx_resolved_ips ON dns_events(resolved_ips);
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
        string? clientIp,
        string? queryName,
        string? queryType,
        string? responseCode,
        List<string> resolvedIps,
        string? zone)
    {
        if (_sqliteConn == null) return;

        try
        {
            var insert = @"
                INSERT INTO dns_events
                (timestamp, event_type, client_ip, query_name, query_type, response_code, resolved_ips, zone)
                VALUES
                (@timestamp, @event_type, @client_ip, @query_name, @query_type, @response_code, @resolved_ips, @zone)
            ";

            using var cmd = new SqliteCommand(insert, _sqliteConn);
            cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@event_type", eventType);
            cmd.Parameters.AddWithValue("@client_ip", clientIp ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@query_name", queryName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@query_type", queryType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@response_code", responseCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@resolved_ips", resolvedIps.Count > 0 ? string.Join(",", resolvedIps) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@zone", zone ?? (object)DBNull.Value);

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
        Console.WriteLine("  DNS Server Real-Time ETW Watcher");
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

        var sessionName = $"DnsServerWatcher_{Environment.ProcessId}";

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

        // DNS Server Provider aktivieren
        session.EnableProvider(DnsServerProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[OK] DNS Server Provider aktiviert");
        Console.WriteLine();
        Console.WriteLine("==========================================");
        Console.WriteLine("  LIVE - Warte auf DNS Events (Ctrl+C)");
        Console.WriteLine("==========================================");
        Console.ResetColor();
        Console.WriteLine();

        if (!_jsonOutput)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Legende: QUERY=Anfrage empfangen, RESPONSE=Antwort gesendet");
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
        string? qname = null, source = null, dest = null, qtype = null, rcode = null;
        string? zone = null, interfaceIp = null;
        int qtypeNum = 0;
        byte[]? packetData = null;
        List<string> resolvedIps = new();

        try
        {
            qname = evt.PayloadByName("QNAME")?.ToString()?.TrimEnd('.');
            source = evt.PayloadByName("Source")?.ToString();
            dest = evt.PayloadByName("Destination")?.ToString();

            var qtypeVal = evt.PayloadByName("QTYPE");
            if (qtypeVal != null && int.TryParse(qtypeVal.ToString()?.Trim(), out int qt))
            {
                qtypeNum = qt;
                qtype = QueryTypes.TryGetValue(qt, out var qtName) ? qtName : qt.ToString();
            }

            var rcodeVal = evt.PayloadByName("RCODE");
            if (rcodeVal != null && int.TryParse(rcodeVal.ToString()?.Trim(), out int rc))
            {
                rcode = ResponseCodes.TryGetValue(rc, out var rcName) ? rcName : rc.ToString();
            }

            zone = evt.PayloadByName("Zone")?.ToString();
            interfaceIp = evt.PayloadByName("InterfaceIP")?.ToString();

            // PacketData fuer Record-Extraktion
            packetData = evt.PayloadByName("PacketData") as byte[];
            if (packetData != null && eventId == 257 && (rcode == "OK" || rcode == "0"))
            {
                resolvedIps = ParseDnsAnswers(packetData);
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
                source,
                destination = dest,
                queryName = qname,
                queryType = qtype,
                responseCode = rcode,
                resolvedAddresses = resolvedIps.Count > 0 ? resolvedIps : null,
                zone,
                interfaceIp
            });
            Console.WriteLine(json);
            _logWriter?.WriteLine(json);
            return;
        }

        // Event 280 (LOOKUP) frueh filtern - vor Timestamp-Ausgabe
        if (eventId == 280 && !_showRaw) return;

        // Nur bekannte Events anzeigen (256=Query, 257=Response, 280=Lookup wenn raw)
        if (eventId != 256 && eventId != 257 && eventId != 280) return;

        // Formatierte Ausgabe
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{timestamp}] ");

        switch (eventId)
        {
            case 256: // QUERY_RECEIVED
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

            case 257: // RESPONSE_SUCCESS
                Console.ForegroundColor = ConsoleColor.Green;
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
                Console.ForegroundColor = rcode == "OK" ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(rcode ?? "?");
                // Zeige aufgeloeste IP-Adressen
                if (resolvedIps.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write(" => " + string.Join(", ", resolvedIps));
                }
                Console.WriteLine();
                break;

            case 280: // Internal lookup (nur bei --raw)
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("LOOKUP   ");
                Console.WriteLine($"{qname} [{qtype}]");
                break;
        }

        Console.ResetColor();

        // Log to file
        if (_logWriter != null)
        {
            var line = eventId switch
            {
                256 => $"{evt.TimeStamp:O}\tQUERY\t{source}\t{qname}\t{qtype}",
                257 => $"{evt.TimeStamp:O}\tRESPONSE\t{dest}\t{qname}\t{qtype}\t{rcode}",
                _ => $"{evt.TimeStamp:O}\t{eventId}\t{source ?? dest}\t{qname}\t{qtype}"
            };
            _logWriter.WriteLine(line);
        }

        // SQLite speichern (nur Query und Response, nicht Lookup)
        if (_sqliteConn != null && (eventId == 256 || eventId == 257))
        {
            var eventType = eventId == 256 ? "QUERY" : "RESPONSE";
            var clientIp = eventId == 256 ? source : dest;
            InsertEvent(evt.TimeStamp, eventType, clientIp, qname, qtype, rcode, resolvedIps, zone);
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
        256 => "QUERY_RECEIVED",
        257 => "RESPONSE_SUCCESS",
        258 => "RECURSE_QUERY_OUT",
        259 => "RECURSE_RESPONSE_IN",
        260 => "QUERY_TIMEOUT",
        261 => "RESPONSE_FAILURE",
        280 => "INTERNAL_LOOKUP",
        _ => $"EVENT_{eventId}"
    };

    private static void PrintHelp()
    {
        Console.WriteLine(@"
DNS Server Real-Time ETW Watcher
================================

Zeigt DNS Server Events in Echtzeit an.
Muss als Administrator ausgefuehrt werden.

Verwendung:
  DnsServerWatcher.exe [optionen]

Optionen:
  -r, --raw          Zeige alle Event-Properties
  -j, --json         Ausgabe als JSON (eine Zeile pro Event)
  -l, --log=X        Schreibe Events zusaetzlich in Datei X
  -s, --sqlite=X     Speichere Events in SQLite Datenbank X
      --retention=N  Behalte Eintraege fuer N Tage (default: 30)
  -h, --help         Diese Hilfe anzeigen

Beispiele:
  DnsServerWatcher.exe
  DnsServerWatcher.exe --json --log=C:\Logs\dns.jsonl
  DnsServerWatcher.exe --sqlite=C:\Logs\dns.db
  DnsServerWatcher.exe --sqlite=C:\Logs\dns.db --retention=90
  DnsServerWatcher.exe -r

SQLite-Abfragen (nach dem Sammeln):
  -- IP-Adresse suchen:
  SELECT * FROM dns_events WHERE resolved_ips LIKE '%142.250.185.110%';

  -- Client-Aktivitaet:
  SELECT * FROM dns_events WHERE client_ip = '10.0.0.51' ORDER BY timestamp DESC;

  -- Domain suchen:
  SELECT * FROM dns_events WHERE query_name LIKE '%google%';

Event-Typen:
  256 = Query empfangen
  257 = Response gesendet (OK)
  258 = Rekursive Query ausgehend
  259 = Rekursive Response eingehend
  260 = Query Timeout
  261 = Response Fehler
");
    }
}
