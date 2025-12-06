# Design-Prinzipien für DNS-ETW Tools

## Leitprinzipien

### 1. Vollständigkeit vor Kürze

**Grundregel**: Zeige standardmäßig ALLE Informationen. Kürzungen nur auf explizite Anfrage.

```csharp
// ❌ FALSCH - Automatisches Kürzen
var qname = evt.PayloadByName("QNAME")?.ToString()?.Substring(0, 50);

// ✅ RICHTIG - Vollständige Ausgabe
var qname = evt.PayloadByName("QNAME")?.ToString()?.TrimEnd('.');
```

**Warum?**
- DNS-Namen können lang sein (z.B. SRV-Records)
- Informationsverlust erschwert Debugging
- User erwartet zu sehen, was tatsächlich aufgelöst wurde

### 2. Opt-in statt Opt-out für Einschränkungen

**Regel**: Wer Einschränkungen will, muss sie EXPLIZIT aktivieren.

```bash
# ❌ FALSCH - Opt-out
DnsServerWatcher.exe --no-truncate

# ✅ RICHTIG - Opt-in (Default = alles)
DnsServerWatcher.exe --truncate=50
```

**Anwendung:**
- `--raw` zeigt MEHR Details (opt-in für Debug-Info)
- `--quiet` zeigt WENIGER (opt-in für Reduktion)
- Filter (`--domain=`, `--client=`) sind opt-in

### 3. Transparenz bei Modifikationen

**Regel**: Wenn Daten gefiltert werden, muss das sichtbar sein.

```csharp
// Startup-Meldung zeigt aktive Filter
if (!string.IsNullOrEmpty(domainFilter))
{
    Console.WriteLine($"[Filter] Nur Domains: {domainFilter}");
}
```

## Projekt-spezifische Entscheidungen

### ETW statt DNS-Log-Datei

**Entscheidung**: Direkter ETW-Consumer statt Parsing der DNS Analytical Logs.

```
❌ FALSCH: DNS Analytical Log → Datei → Parsing → Ausgabe
✅ RICHTIG: ETW Provider → Direkt → Ausgabe
```

**Warum?**
- Echtzeit (keine Verzögerung durch Log-Buffer)
- Kein Dateisystem-Overhead
- Strukturierte Daten (kein Text-Parsing nötig)
- Robuster (keine Log-Rotation-Probleme)

### Self-contained Build

**Entscheidung**: Single-File Executable mit eingebetteter Runtime.

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
```

**Warum?**
- Keine .NET-Installation auf Ziel-Server nötig
- Einfaches Deployment (eine Datei kopieren)
- Keine Versionskonflikte mit vorhandener .NET-Runtime

### SQLite für Persistenz

**Entscheidung**: SQLite statt Logfile für durchsuchbare Speicherung.

```
❌ FALSCH: Nur Text-Logfile (schwer durchsuchbar)
✅ RICHTIG: SQLite + optionales Logfile
```

**Warum?**
- SQL-Queries für Suche (z.B. "Wer hat IP X aufgelöst?")
- Indices für schnelle Suche
- Automatische Retention (alte Daten löschen)
- Kein externes Tool nötig (SQLite ist eingebettet)

### Zwei separate Tools statt eines

**Entscheidung**: DnsServerWatcher und DnsClientWatcher als separate Executables.

```
❌ FALSCH: DnsWatcher --mode=server / DnsWatcher --mode=client
✅ RICHTIG: DnsServerWatcher.exe / DnsClientWatcher.exe
```

**Warum?**
- Klarere Benennung (Name sagt was es tut)
- Unterschiedliche ETW-Provider (unterschiedliche Events)
- Unterschiedliche Use Cases (Server vs. Client)
- Kleinere Executables (nur relevanter Code)

### Farbige Console-Ausgabe

**Entscheidung**: Unterschiedliche Farben für Event-Typen.

| Event | Farbe | Grund |
|-------|-------|-------|
| QUERY | Weiß | Neutral, Anfrage |
| RESPONSE | Grün | Positiv, Antwort |
| CACHE | Blau | Info, aus Cache |
| Fehler | Rot | Aufmerksamkeit |
| Zeitstempel | Grau | Unwichtig |
| IP-Adressen | Magenta | Hervorhebung |

**Warum?**
- Schnelles visuelles Scannen
- Fehler sofort erkennbar
- Unterscheidung Query/Response auf einen Blick

## Merksatz

> **"Der Default ist die Wahrheit, Einschränkungen sind explizit."**

- Zeige alles, filtere auf Wunsch
- Vollständige Ausgabe, kürze auf Wunsch
- Alle Events, unterdrücke auf Wunsch
