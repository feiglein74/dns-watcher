# DnsServerWatcher

Real-Time DNS Event Watcher für Windows DNS Server via ETW (Event Tracing for Windows).

## Use Case

**Problem:** In der Firewall steht ein Eintrag wie:
```
BLOCKED: 10.0.0.51 -> 142.250.185.110:443
```

**Fragen:**
- Welcher DNS-Name steckt hinter `142.250.185.110`?
- Wer (welcher Client) hat diesen Namen aufgelöst?
- Wann wurde die Auflösung gemacht?

**Lösung:** DnsServerWatcher protokolliert alle DNS-Auflösungen in Echtzeit:

```
[19:43:53.626] QUERY    10.0.0.51 -> google.com [A]
[19:43:53.627] RESPONSE 10.0.0.51 <- google.com [A] OK => 142.250.185.110
```

Jetzt kann man im Log nach `142.250.185.110` suchen und findet:
- **Client:** `10.0.0.51`
- **DNS-Name:** `google.com`
- **Zeitpunkt:** `19:43:53`

## Features

- **Echtzeit-Monitoring**: Direkt aus dem ETW-Stream, kein Polling
- **Standalone**: Keine .NET Installation auf dem Ziel-Server erforderlich
- **IP-Auflösung**: Zeigt aufgelöste IP-Adressen aus DNS-Responses
- **Flexible Ausgabe**: Console, JSON, Logfile, SQLite
- **Performant**: Kann tausende Events/Sekunde verarbeiten

## Installation

1. `DnsServerWatcher.exe` aus dem `publish/` Ordner auf den DNS-Server kopieren
2. Als Administrator ausführen

### Selbst bauen

Voraussetzung: .NET 6+ SDK

```powershell
cd DnsServerWatcher
dotnet publish -c Release -o publish
```

## Verwendung

```powershell
# Basis-Nutzung (als Administrator)
.\DnsServerWatcher.exe

# Mit JSON-Output
.\DnsServerWatcher.exe --json

# In Logfile schreiben
.\DnsServerWatcher.exe --log=C:\Logs\dns.log

# SQLite Datenbank (empfohlen für langfristige Speicherung)
.\DnsServerWatcher.exe --sqlite=C:\Logs\dns.db

# SQLite mit 90 Tage Retention
.\DnsServerWatcher.exe --sqlite=C:\Logs\dns.db --retention=90

# Alle Event-Properties anzeigen (Debug)
.\DnsServerWatcher.exe --raw

# Hilfe anzeigen
.\DnsServerWatcher.exe --help
```

## SQLite-Abfragen

Nach dem Sammeln von Events kann die Datenbank direkt abgefragt werden:

```sql
-- IP-Adresse aus Firewall-Log suchen:
SELECT timestamp, client_ip, query_name, query_type, resolved_ips
FROM dns_events
WHERE resolved_ips LIKE '%142.250.185.110%'
ORDER BY timestamp DESC;

-- Alle Anfragen eines Clients:
SELECT timestamp, query_name, query_type, response_code, resolved_ips
FROM dns_events
WHERE client_ip = '10.0.0.51'
ORDER BY timestamp DESC;

-- Domain suchen:
SELECT timestamp, client_ip, response_code, resolved_ips
FROM dns_events
WHERE query_name LIKE '%google%'
ORDER BY timestamp DESC;

-- Statistik: Top-Domains
SELECT query_name, COUNT(*) as count
FROM dns_events
WHERE event_type = 'QUERY'
GROUP BY query_name
ORDER BY count DESC
LIMIT 20;
```

## Voraussetzungen

- Windows Server mit DNS-Server Rolle
- Administrator-Rechte
- ETW Analytical Logging (wird automatisch geprüft)

## Technische Details

- ETW Provider: `Microsoft-Windows-DNSServer` (`EB79061A-A566-4698-9119-3ED2807060E7`)
- Event-IDs: 256 (Query), 257 (Response), 258-261 (Rekursion/Fehler), 280 (Lookup)
- Self-contained Single-File Executable

## Siehe auch

- **DnsClientWatcher** - Überwacht DNS-Anfragen auf dem Client (lokale Auflösungen)
