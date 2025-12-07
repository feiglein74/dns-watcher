# DnsClientWatcher

Real-Time DNS Event Watcher für Windows DNS Client via ETW (Event Tracing for Windows).

## Use Case

Überwacht **lokale** DNS-Auflösungen auf einem Windows-Rechner (Client oder Server).

**Beispiel:** Du möchtest wissen, welche DNS-Anfragen ein bestimmter PC macht:

```
[19:43:53.626] QUERY    google.com [A]
[19:43:53.627] SEND     google.com -> 10.0.0.1
[19:43:53.640] RESPONSE google.com [A] OK => 142.250.185.110
```

## Unterschied zu DnsServerWatcher

| Feature | DnsServerWatcher | DnsClientWatcher |
|---------|------------------|------------------|
| Läuft auf | DNS Server | Beliebiger Windows-Rechner |
| Zeigt | Alle Anfragen von allen Clients | Nur lokale Anfragen |
| ETW Provider | Microsoft-Windows-DNSServer | Microsoft-Windows-DNS-Client |
| Use Case | Server-seitiges Logging | Client-seitiges Debugging |

## Features

- **Echtzeit-Monitoring**: Direkt aus dem ETW-Stream, kein Polling
- **Standalone**: Keine .NET Installation erforderlich
- **Cache-Erkennung**: Unterscheidet Cache-Hits von Netzwerk-Anfragen
- **DNS-Server-Tracking**: Zeigt welcher DNS-Server gefragt wird
- **Flexible Ausgabe**: Console, JSON, Logfile, SQLite

## Installation

1. `DnsClientWatcher.exe` aus dem `publish/` Ordner auf den Rechner kopieren
2. Als Administrator ausführen

### Selbst bauen

Voraussetzung: .NET 6+ SDK

```powershell
cd DnsClientWatcher
dotnet publish -c Release -o publish
```

## Verwendung

```powershell
# Basis-Nutzung (als Administrator)
.\DnsClientWatcher.exe

# Mit JSON-Output
.\DnsClientWatcher.exe --json

# In Logfile schreiben
.\DnsClientWatcher.exe --log=C:\Logs\dnsclient.log

# SQLite Datenbank
.\DnsClientWatcher.exe --sqlite=C:\Logs\dnsclient.db

# SQLite mit 90 Tage Retention
.\DnsClientWatcher.exe --sqlite=C:\Logs\dnsclient.db --retention=90

# Alle Event-Properties anzeigen (Debug)
.\DnsClientWatcher.exe --raw

# Hilfe anzeigen
.\DnsClientWatcher.exe --help
```

## SQLite-Abfragen

```sql
-- Aufgelöste IP suchen:
SELECT timestamp, query_name, query_type, query_results
FROM dns_events
WHERE query_results LIKE '%142.250.185.110%'
ORDER BY timestamp DESC;

-- Domain suchen:
SELECT timestamp, event_type, query_results
FROM dns_events
WHERE query_name LIKE '%google%'
ORDER BY timestamp DESC;

-- Nur Cache-Hits:
SELECT timestamp, query_name, query_results
FROM dns_events
WHERE event_type = 'CACHE'
ORDER BY timestamp DESC;

-- Statistik: Top-Domains
SELECT query_name, COUNT(*) as count
FROM dns_events
WHERE event_type = 'QUERY'
GROUP BY query_name
ORDER BY count DESC
LIMIT 20;
```

## Event-Typen

| Event-ID | Name | Beschreibung |
|----------|------|--------------|
| 3006 | QUERY | DNS-Anfrage gestartet |
| 3008 | COMPLETE | DNS-Anfrage abgeschlossen |
| 3009 | SEND | Anfrage an DNS-Server gesendet |
| 3018 | CACHE | Cache-Hit (aus lokalem Cache) |
| 3020 | RESPONSE | Antwort vom DNS-Server erhalten |

## Ausgabe-Format

### Console (Standard)

```
[19:43:53.626] QUERY    google.com [A]
[19:43:53.627] SEND     google.com -> 10.0.0.1
[19:43:53.640] RESPONSE google.com [A] OK => 142.250.185.110
[19:43:54.100] CACHE    google.com [A] OK => 142.250.185.110
```

### JSON (`--json`)

```json
{
  "timestamp": "2025-12-04T19:43:53.640",
  "eventId": 3020,
  "eventName": "RESPONSE",
  "queryName": "google.com",
  "queryType": "A",
  "status": "OK",
  "queryResults": "142.250.185.110",
  "dnsServer": "10.0.0.1",
  "interfaceIndex": 12
}
```

## Voraussetzungen

- Windows 7+ / Windows Server 2008 R2+
- Administrator-Rechte

## Windows Service (Dauerbetrieb)

Fuer den produktiven Einsatz wird der Betrieb als Windows-Dienst empfohlen.

### Service installieren

```powershell
# Als Administrator ausfuehren!
DnsClientWatcher.exe install --sqlite=C:\Logs\dns-client.db
```

### Service steuern

```powershell
DnsClientWatcher.exe start    # Service starten
DnsClientWatcher.exe stop     # Service stoppen
DnsClientWatcher.exe status   # Status anzeigen
DnsClientWatcher.exe uninstall # Service deinstallieren
```

### Service-Log Format

Im Service-Modus werden Statistik-Meldungen im .NET-Logging-Format ausgegeben:

```
info: DnsClientWatcher.DnsClientWatcherService[0]
      Laeuft seit 04:35:00, 7,031 Events (0.4/s)
```

Das Format ist: `{LogLevel}: {CategoryName}[{EventId}]`
- Die `[0]` ist die Event-ID (wir verwenden keine spezifischen IDs, daher immer 0)
- Dies ist rein informativ und kann ignoriert werden

## Technische Details

- ETW Provider: `Microsoft-Windows-DNS-Client` (`1C95126E-7EEA-49A9-A3FE-A378B03DDB4D`)
- Self-contained Single-File Executable

## Siehe auch

- **DnsServerWatcher** - Überwacht DNS-Anfragen auf dem DNS Server (alle Clients)
