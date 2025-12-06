# DNS-ETW Tools

Real-Time DNS Event Monitoring Tools für Windows via ETW (Event Tracing for Windows).

## Tools

### DnsServerWatcher

Überwacht DNS-Anfragen auf einem **Windows DNS Server**. Zeigt alle Queries von allen Clients.

```
[19:43:53.626] QUERY    10.0.0.51 -> google.com [A]
[19:43:53.627] RESPONSE 10.0.0.51 <- google.com [A] OK => 142.250.185.110
```

**Use Case:** Firewall zeigt geblockte IP - wer hat diese aufgelöst?

[Details → DnsServerWatcher/README.md](DnsServerWatcher/README.md)

### DnsClientWatcher

Überwacht DNS-Anfragen auf einem **beliebigen Windows-Rechner** (Client oder Server). Zeigt lokale Auflösungen.

```
[19:43:53.626] QUERY    google.com [A]
[19:43:53.627] SEND     google.com -> 10.0.0.1
[19:43:53.640] RESPONSE google.com [A] OK => 142.250.185.110
```

**Use Case:** Welche DNS-Anfragen macht dieser Rechner?

[Details → DnsClientWatcher/README.md](DnsClientWatcher/README.md)

## Vergleich

| Feature | DnsServerWatcher | DnsClientWatcher |
|---------|------------------|------------------|
| Läuft auf | DNS Server | Beliebiger Windows-Rechner |
| Zeigt | Anfragen von **allen** Clients | Nur **lokale** Anfragen |
| ETW Provider | Microsoft-Windows-DNSServer | Microsoft-Windows-DNS-Client |
| Client-IP | Ja | Nein (ist lokal) |
| DNS-Server | Nein (ist lokal) | Ja |
| Cache-Hits | Nein | Ja |

## Gemeinsame Features

- Echtzeit-Monitoring via ETW
- Standalone Executables (keine .NET-Installation nötig)
- Ausgabe: Console, JSON, Logfile, SQLite
- SQLite mit konfigurierbarer Retention
- Administrator-Rechte erforderlich
- DNS-Paket-Parsing via [ARSoft.Tools.Net](https://github.com/alexreinert/ARSoft.Tools.Net) (60+ RFCs)

## Build

```powershell
# DnsServerWatcher
cd DnsServerWatcher
dotnet publish -c Release -o publish

# DnsClientWatcher
cd DnsClientWatcher
dotnet publish -c Release -o publish
```

## Lizenz

MIT License
