# DNS Watcher - Datenbank Schema

Dokumentation der SQLite-Datenbank-Schemas fuer die Integration in andere Projekte.

## Allgemeine Hinweise

- **Datenbank-Format**: SQLite 3
- **Journal-Mode**: WAL (Write-Ahead Logging) fuer bessere Performance
- **Encoding**: UTF-8
- **Zeitstempel**: ISO 8601 Format (`2024-12-06T19:43:53.626+01:00`)

### Zugriff auf laufende Datenbank

Die Datenbank kann waehrend des Betriebs gelesen werden (WAL-Modus). Beim Schreiben:
- Nur der Watcher-Service schreibt
- Andere Projekte sollten nur lesen
- Bei Bedarf: Kopie der Datenbank erstellen

---

## Schema-Versionierung

Beide Datenbanken enthalten eine `schema_version` Tabelle zur Kompatibilitaetspruefung.

**Tabelle**: `schema_version`

| Spalte | Typ | Beschreibung |
|--------|-----|--------------|
| `version` | INTEGER | Aktuelle Schema-Version |

### Versionshistorie

| Version | Aenderungen |
|---------|-------------|
| 1 | Initiales Schema (ohne Versionstabelle) |
| 2 | + `error_category` Spalte, + `schema_version` Tabelle |

### Kompatibilitaetspruefung

Viewer-Komponenten sollten die Schema-Version pruefen:

```sql
-- Version abfragen (falls Tabelle existiert)
SELECT version FROM schema_version LIMIT 1;

-- Pruefen ob Tabelle existiert (alte Datenbanken haben keine)
SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version';
```

**Empfohlene Logik:**
1. Wenn `schema_version` Tabelle nicht existiert → Version 1 annehmen
2. Wenn Version < erwartete Version → Warnung ausgeben oder Feature deaktivieren
3. Wenn Version > erwartete Version → Viewer ist veraltet, Update empfehlen

---

## DnsServerWatcher - Schema

**Tabelle**: `dns_events`

### Spalten

| Spalte | Typ | Beschreibung |
|--------|-----|--------------|
| `id` | INTEGER | Primary Key, Auto-Increment |
| `timestamp` | TEXT | ISO 8601 Zeitstempel |
| `event_type` | TEXT | Event-Typ: `QUERY`, `RESPONSE`, `TIMEOUT`, `FAILURE` |
| `client_ip` | TEXT | IP-Adresse des anfragenden Clients |
| `query_name` | TEXT | Angefragter DNS-Name (ohne trailing dot) |
| `query_type` | TEXT | DNS-Record-Typ: `A`, `AAAA`, `CNAME`, `MX`, `TXT`, etc. |
| `response_code` | TEXT | DNS Response Code: `OK`, `NXDomain`, `ServFail`, etc. |
| `resolved_ips` | TEXT | Aufgeloeste Adressen (komma-separiert) oder Parse-Fehler |
| `zone` | TEXT | DNS-Zone (falls verfuegbar) |
| `error_category` | TEXT | Fehler-Kategorie: `CONFIG_ERROR`, `CLIENT_ERROR`, oder NULL |

### Indices

- `idx_timestamp` - Schnelle zeitbasierte Abfragen
- `idx_client_ip` - Suche nach Client-IP
- `idx_query_name` - Suche nach Domain-Namen
- `idx_resolved_ips` - Suche nach aufgeloesten IPs
- `idx_error_category` - Filterung nach Fehlertyp

### Event-Typen

| Event-Type | ETW Event-ID | Beschreibung |
|------------|--------------|--------------|
| `QUERY` | 256 | DNS-Anfrage empfangen |
| `RESPONSE` | 257 | DNS-Antwort gesendet |
| `TIMEOUT` | 260 | Anfrage Timeout |
| `FAILURE` | 261 | Antwort fehlgeschlagen |

### Response Codes

| Code | Bedeutung |
|------|-----------|
| `OK` | Erfolgreiche Aufloesung |
| `NXDomain` | Domain existiert nicht |
| `ServFail` | Server-Fehler |
| `Refused` | Anfrage abgelehnt |
| `NotImpl` | Nicht implementiert |
| `FormErr` | Fehlerhafte Anfrage |

### Error Categories

| Kategorie | Bedeutung | Typische Codes |
|-----------|-----------|----------------|
| `CONFIG_ERROR` | Server/Netzwerk-Konfigurationsproblem | NXDomain, ServFail, Refused, NotImpl, NotAuth, NotZone |
| `CLIENT_ERROR` | Client sendet fehlerhafte Anfrage | FormErr, YXDomain, YXRRSet, NXRRSet, MALFORMED, TRUNCATED |
| NULL | Kein Fehler | OK |

---

## DnsClientWatcher - Schema

**Tabelle**: `dns_events`

### Spalten

| Spalte | Typ | Beschreibung |
|--------|-----|--------------|
| `id` | INTEGER | Primary Key, Auto-Increment |
| `timestamp` | TEXT | ISO 8601 Zeitstempel |
| `event_type` | TEXT | Event-Typ: `QUERY`, `COMPLETE`, `SEND`, `CACHE`, `RESPONSE` |
| `event_id` | INTEGER | Original ETW Event-ID |
| `process_id` | INTEGER | PID des anfragenden Prozesses |
| `process_name` | TEXT | Name des Prozesses |
| `query_name` | TEXT | Angefragter DNS-Name |
| `query_type` | TEXT | DNS-Record-Typ |
| `status` | TEXT | Status-Code: `OK`, `Cached`, `NXDomain`, etc. |
| `query_results` | TEXT | Aufgeloeste Adressen (formatiert) |
| `dns_server` | TEXT | Verwendeter DNS-Server |
| `interface_index` | INTEGER | Netzwerk-Interface Index |
| `error_category` | TEXT | Fehler-Kategorie: `CONFIG_ERROR`, `CLIENT_ERROR`, oder NULL |

### Indices

- `idx_timestamp` - Schnelle zeitbasierte Abfragen
- `idx_process_name` - Suche nach Prozess
- `idx_query_name` - Suche nach Domain-Namen
- `idx_query_results` - Suche nach aufgeloesten IPs
- `idx_error_category` - Filterung nach Fehlertyp

### Event-Typen

| Event-Type | ETW Event-ID | Beschreibung |
|------------|--------------|--------------|
| `QUERY` | 3006 | DNS-Anfrage gestartet |
| `COMPLETE` | 3008 | Anfrage abgeschlossen |
| `SEND` | 3009 | Anfrage an Server gesendet |
| `CACHE` | 3018 | Antwort aus Cache |
| `RESPONSE` | 3020 | Antwort empfangen |

### Status Codes

| Code | Windows-Fehlercode | Bedeutung |
|------|-------------------|-----------|
| `OK` | 0 | Erfolg |
| `Cached` | 87 | Aus Cache |
| `NXDomain` | 9003 | Domain existiert nicht |
| `ServFail` | 9002 | Server-Fehler |
| `Refused` | 9005 | Abgelehnt |
| `Timeout` | 1460, 9560 | Zeitueberschreitung |
| `InvalidName` | 1214 | Ungueltiger DNS-Name |
| `HostNotFound` | 11001 | Host nicht gefunden |

### Error Categories

| Kategorie | Bedeutung | Typische Codes |
|-----------|-----------|----------------|
| `CONFIG_ERROR` | DNS-Server/Netzwerk-Problem | ServFail, NotImpl, Refused, Timeout, TryAgain, NoRecovery |
| `CLIENT_ERROR` | Fehlerhafte Anfrage | InvalidName, RecordFormat |
| NULL | Kein Fehler | OK, Cached |

---

## Beispiel-Abfragen

### IP-Adresse aus Firewall-Log suchen (Server)

```sql
SELECT timestamp, client_ip, query_name, query_type, resolved_ips
FROM dns_events
WHERE resolved_ips LIKE '%142.250.185.110%'
ORDER BY timestamp DESC;
```

### Alle Anfragen eines Clients (Server)

```sql
SELECT timestamp, query_name, query_type, response_code, resolved_ips
FROM dns_events
WHERE client_ip = '10.0.0.51'
ORDER BY timestamp DESC;
```

### Domain suchen (beide)

```sql
SELECT timestamp, client_ip, response_code, resolved_ips
FROM dns_events
WHERE query_name LIKE '%google%'
ORDER BY timestamp DESC;
```

### Nur Fehler anzeigen (beide)

```sql
SELECT timestamp, client_ip, query_name, response_code, error_category
FROM dns_events
WHERE error_category IS NOT NULL
ORDER BY timestamp DESC;
```

### Konfigurations-Fehler (beide)

```sql
SELECT timestamp, client_ip, query_name, response_code
FROM dns_events
WHERE error_category = 'CONFIG_ERROR'
ORDER BY timestamp DESC;
```

### Client-Fehler / fehlerhafte Pakete (beide)

```sql
SELECT timestamp, client_ip, query_name, resolved_ips
FROM dns_events
WHERE error_category = 'CLIENT_ERROR'
ORDER BY timestamp DESC;
```

### Top-Domains (Server)

```sql
SELECT query_name, COUNT(*) as count
FROM dns_events
WHERE event_type = 'QUERY'
GROUP BY query_name
ORDER BY count DESC
LIMIT 20;
```

### Anfragen pro Prozess (Client)

```sql
SELECT process_name, COUNT(*) as count
FROM dns_events
WHERE event_type = 'QUERY'
GROUP BY process_name
ORDER BY count DESC;
```

### Fehler-Statistik (beide)

```sql
SELECT
    error_category,
    response_code,
    COUNT(*) as count
FROM dns_events
WHERE error_category IS NOT NULL
GROUP BY error_category, response_code
ORDER BY count DESC;
```

---

## Integration

### PowerShell

```powershell
# SQLite-Modul installieren (einmalig)
Install-Module -Name PSSQLite -Scope CurrentUser

# Modul laden
Import-Module PSSQLite

$dbPath = "C:\Logs\dns-events.db"

# Schema-Version pruefen
$minVersion = 2  # Erwartete Mindestversion
$versionTable = Invoke-SqliteQuery -DataSource $dbPath -Query @"
SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'
"@
if ($versionTable) {
    $version = (Invoke-SqliteQuery -DataSource $dbPath -Query "SELECT version FROM schema_version LIMIT 1").version
} else {
    $version = 1  # Alte DB ohne Versionstabelle
}
if ($version -lt $minVersion) {
    Write-Warning "Datenbank-Schema Version $version ist aelter als erwartet ($minVersion)"
}

# IP suchen
$events = Invoke-SqliteQuery -DataSource $dbPath -Query @"
SELECT timestamp, client_ip, query_name, resolved_ips
FROM dns_events
WHERE resolved_ips LIKE '%142.250.185.110%'
ORDER BY timestamp DESC
LIMIT 10
"@

$events | Format-Table

# Fehler der letzten Stunde
$since = (Get-Date).AddHours(-1).ToString("o")
$errors = Invoke-SqliteQuery -DataSource $dbPath -Query @"
SELECT timestamp, client_ip, query_name, response_code, error_category
FROM dns_events
WHERE error_category IS NOT NULL
  AND timestamp > '$since'
ORDER BY timestamp DESC
"@

$errors | Format-Table
```

### Go

```go
package main

import (
    "database/sql"
    "fmt"
    "log"

    _ "github.com/mattn/go-sqlite3"
)

type DnsEvent struct {
    ID            int64
    Timestamp     string
    EventType     string
    ClientIP      sql.NullString
    QueryName     sql.NullString
    QueryType     sql.NullString
    ResponseCode  sql.NullString
    ResolvedIPs   sql.NullString
    ErrorCategory sql.NullString
}

// GetSchemaVersion prueft die Schema-Version der Datenbank
func GetSchemaVersion(db *sql.DB) int {
    var name string
    err := db.QueryRow("SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'").Scan(&name)
    if err != nil {
        return 1 // Alte DB ohne Versionstabelle
    }
    var version int
    err = db.QueryRow("SELECT version FROM schema_version LIMIT 1").Scan(&version)
    if err != nil {
        return 1
    }
    return version
}

func main() {
    // Datenbank oeffnen (read-only empfohlen)
    db, err := sql.Open("sqlite3", "file:C:/Logs/dns-events.db?mode=ro")
    if err != nil {
        log.Fatal(err)
    }
    defer db.Close()

    // Schema-Version pruefen
    minVersion := 2
    version := GetSchemaVersion(db)
    if version < minVersion {
        log.Printf("Warnung: Schema Version %d ist aelter als erwartet (%d)", version, minVersion)
    }

    // IP suchen
    rows, err := db.Query(`
        SELECT id, timestamp, event_type, client_ip, query_name,
               query_type, response_code, resolved_ips, error_category
        FROM dns_events
        WHERE resolved_ips LIKE ?
        ORDER BY timestamp DESC
        LIMIT 10
    `, "%142.250.185.110%")
    if err != nil {
        log.Fatal(err)
    }
    defer rows.Close()

    for rows.Next() {
        var e DnsEvent
        err := rows.Scan(&e.ID, &e.Timestamp, &e.EventType, &e.ClientIP,
            &e.QueryName, &e.QueryType, &e.ResponseCode, &e.ResolvedIPs,
            &e.ErrorCategory)
        if err != nil {
            log.Fatal(err)
        }
        fmt.Printf("%s | %s | %s | %s\n",
            e.Timestamp, e.ClientIP.String, e.QueryName.String, e.ResolvedIPs.String)
    }
}
```

### Python

```python
import sqlite3
from datetime import datetime, timedelta

def get_schema_version(conn):
    """Prueft die Schema-Version der Datenbank"""
    cursor = conn.cursor()
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'")
    if cursor.fetchone() is None:
        return 1  # Alte DB ohne Versionstabelle
    cursor.execute("SELECT version FROM schema_version LIMIT 1")
    row = cursor.fetchone()
    return row[0] if row else 1

# Datenbank oeffnen
conn = sqlite3.connect("file:C:/Logs/dns-events.db?mode=ro", uri=True)
conn.row_factory = sqlite3.Row
cursor = conn.cursor()

# Schema-Version pruefen
MIN_VERSION = 2
version = get_schema_version(conn)
if version < MIN_VERSION:
    print(f"Warnung: Schema Version {version} ist aelter als erwartet ({MIN_VERSION})")

# IP suchen
cursor.execute("""
    SELECT timestamp, client_ip, query_name, resolved_ips
    FROM dns_events
    WHERE resolved_ips LIKE ?
    ORDER BY timestamp DESC
    LIMIT 10
""", ("%142.250.185.110%",))

for row in cursor.fetchall():
    print(f"{row['timestamp']} | {row['client_ip']} | {row['query_name']} | {row['resolved_ips']}")

# Fehler der letzten Stunde
since = (datetime.now() - timedelta(hours=1)).isoformat()
cursor.execute("""
    SELECT timestamp, client_ip, query_name, error_category
    FROM dns_events
    WHERE error_category IS NOT NULL AND timestamp > ?
""", (since,))

for row in cursor.fetchall():
    print(f"{row['timestamp']} | {row['error_category']} | {row['query_name']}")

conn.close()
```

---

## Hinweise

### Performance

- Bei grossen Datenbanken: Indices nutzen
- LIKE-Abfragen mit `%` am Anfang sind langsam
- Fuer haeufige Abfragen: Views oder zusaetzliche Indices erstellen

### Concurrent Access

- SQLite im WAL-Modus erlaubt paralleles Lesen
- Nur ein Writer (der Watcher-Service)
- Bei Bedarf: `PRAGMA busy_timeout = 5000;` setzen

### Backup

- Der Watcher erstellt automatisch Backups vor Cleanup
- Datei: `dns-events.backup1.db`, `dns-events.backup2.db`, etc.
