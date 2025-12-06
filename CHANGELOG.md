# Changelog

Alle wichtigen Änderungen an diesem Projekt werden hier dokumentiert.

Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.0.0/),
und dieses Projekt folgt [Semantic Versioning](https://semver.org/lang/de/).

## [1.0.0] - 2025-12-06

### Hinzugefügt

#### DnsServerWatcher (vorher DnsWatcher)
- ETW Real-Time Consumer für Microsoft-Windows-DNSServer
- Console-Output mit Farben (Query=weiß, Response=grün)
- JSON-Output (`--json`)
- Logfile-Support (`--log=PFAD`)
- SQLite-Speicherung (`--sqlite=PFAD`)
- Retention-Konfiguration (`--retention=TAGE`, default: 30)
- IP-Adress-Extraktion aus DNS-Responses
- Raw-Modus für Debugging (`--raw`)
- Self-contained Single-File Build

#### DnsClientWatcher (neu)
- ETW Real-Time Consumer für Microsoft-Windows-DNS-Client
- Console-Output mit Farben (Query, Send, Response, Cache)
- JSON-Output (`--json`)
- Logfile-Support (`--log=PFAD`)
- SQLite-Speicherung (`--sqlite=PFAD`)
- Retention-Konfiguration (`--retention=TAGE`, default: 30)
- Cache-Hit-Erkennung (Event 3018)
- DNS-Server-Tracking (welcher Server wurde gefragt)
- Raw-Modus für Debugging (`--raw`)
- Self-contained Single-File Build

### Geändert
- Projekt von "DnsWatcher" zu "DnsServerWatcher" umbenannt
- Klarere Unterscheidung zwischen Server- und Client-Monitoring
