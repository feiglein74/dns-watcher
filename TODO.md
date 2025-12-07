# TODO

## Priorität 1 - Wichtig

### Beide Tools
- [ ] `--quiet` Option für Server-Betrieb (nur SQLite/Logfile, keine Console)
- [ ] Filter-Optionen
  - `--domain=PATTERN` (z.B. `--domain=*.google.com`)
  - `--type=A,AAAA` (Query-Types filtern)
  - `--exclude=PATTERN` (Domains ausschließen)
- [ ] Windows Service Modus (`--install-service`, `--uninstall-service`)

### DnsServerWatcher
- [ ] `--client=IP` Filter für bestimmte Clients

### DnsClientWatcher
- [ ] Prozess-Tracking: Welcher Prozess hat die DNS-Anfrage gemacht
- [ ] `--no-cache` Option um Cache-Events zu unterdrücken

### DnsServerWatcher
- [ ] CONFIG Events (65208-65279) analysieren und dokumentieren
  - Undokumentierte DNS Server Shutdown/Diagnostics Events
  - Payloads enthalten Konfig-Werte wie `IsRRlEnabled`, `IsAnalyticChannelEnabled`
  - Eventuell Mapping auf bekannte DnsServerDiagnostics-Properties

## Priorität 2 - Nice to Have

### Beide Tools
- [ ] Statistik-Zusammenfassung bei Ctrl+C (Top 10 Domains, etc.)
- [ ] CSV-Export-Modus
- [ ] Syslog-Output für zentrale Log-Server
- [ ] Prometheus/OpenTelemetry Metriken

### DnsServerWatcher
- [ ] Zone-Filter (`--zone=intern.local`)
- [ ] Rekursive Query/Response Events (258/259) anzeigen

### DnsClientWatcher
- [ ] Interface-Filter (`--interface=12`)
- [ ] DNS-Server-Filter (`--server=10.0.0.1`)

## Priorität 3 - Zukunft

- [ ] Web-Dashboard (lokaler HTTP-Server mit Live-View)
- [ ] Alerting bei bestimmten Patterns (z.B. NXDomain-Spikes)
- [ ] Gemeinsame Library für Code-Sharing zwischen beiden Tools
- [ ] Linux-Support via eBPF (langfristig)

## Erledigt

- [x] ETW Real-Time Consumer
- [x] Console-Output mit Farben
- [x] JSON-Output
- [x] Logfile-Support
- [x] SQLite-Speicherung mit Retention
- [x] IP-Adress-Extraktion aus DNS-Responses (Server)
- [x] Cache-Hit-Erkennung (Client)
- [x] Self-contained Build
- [x] Umbenennung DnsWatcher → DnsServerWatcher
- [x] Neues Tool DnsClientWatcher
