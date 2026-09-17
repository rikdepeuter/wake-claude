# wake-claude

Brengt lokale Claude Code-sessies in de desktop-app op aanvraag terug tot leven, of start een nieuwe,
zodat ze via Remote Control vanaf een ander toestel bereikbaar zijn.

## Waarom

De Claude-desktop-app update zichzelf via de Microsoft Store. Bij elke update sluit de app, en alle
interactieve sessies die eronder draaien, gaan mee. Daarnaast pauzeert de app zelf elke sessie die een
kwartier niets doet en geen verbonden Remote Control heeft. Een gestopte sessie wordt pas weer
bereikbaar na een nieuw bericht.

## Gebruik

```
wake-claude.exe resume <naam> [opties]
wake-claude.exe fresh  <naam> [opties]
wake-claude.exe new   [<naam>] [opties]
```

| modus | actief en bereikbaar | gestopt | niet gevonden |
|---|---|---|---|
| `resume` | niets doen | heractiveren | nieuwe starten |
| `fresh` | niets doen | nieuwe starten, oude archiveren | nieuwe starten |
| `new` | nieuwe starten | nieuwe starten | nieuwe starten |

Opties:

- `--folder <pad>` - map voor een nieuwe sessie, standaard `D:\`
- `--prompt "<tekst>"` - eerste bericht voor de sessie
- `--timeout <seconden>` - hoe lang wachten tot de sessie bereikbaar is, standaard 60
- `--max-context <tokens>` - bij `resume`: is de gestopte sessie groter, dan wordt ze vervangen door een
  nieuwe (en gearchiveerd) in plaats van heractiveerd, standaard 200000
- `--dry-run` - tonen wat er zou gebeuren, zonder iets te doen
- `--json` - resultaat als JSON op stdout

Exitcodes: `0` gelukt, `1` andere fout, `2` argumenten, `3` app start niet, `4` stap in de interface
mislukt, `5` timeout. Voortgang gaat naar stderr en naar `%LOCALAPPDATA%\wake-claude\wake-claude.log`.

## Hoe het werkt

- **Zoeken op naam:** exacte titel, hoofdletters tellen niet, gearchiveerde sessies niet. Bij meerdere
  treffers de meest recente.
- **Actief** betekent: het proces draait (`claude agents --json`) en het log van de app toont Remote
  Control verbonden voor die sessie sinds het proces startte.
- **Nieuw:** `claude://code/new` vult map en prompt in, de exe klikt in het invoerveld en drukt Enter. De
  prompt vraagt de sessie eerst zichzelf te hernoemen (`set_session_title`) en vervangen sessies te
  archiveren (`archive_session`). De exe wacht tot naam, archivering en Remote Control in orde zijn.
- **Heractiveren:** eerst naar een andere sessie navigeren, dan `claude://code/continue`, wachten tot
  het log toont dat de sessie in beeld is, en dan een kort bericht typen.

Veiligheid: er wordt enkel geklikt of getypt als het Claude-venster vooraan staat, en pas nadat het log
bevestigt dat het juiste scherm open is (het nieuwe-sessiescherm, of de doelsessie).

## Kosten in tokens (gemeten)

| actie | cache gelezen | cache geschreven | uitvoer |
|---|---|---|---|
| nieuw, zonder naam | ~43k | ~11k | 4 |
| nieuw, met naam | ~151k | ~12k | ~190 |
| nieuw, met naam en 1 archivering | ~205k | ~14k | ~530 |
| heractiveren, sessie van 53k | ~40k | ~14k | 4 |

Een naam of archivering kost extra rondes (de sessietools moeten eerst geladen worden), maar dat zijn
vooral goedkope cache-lezingen. Heractiveren kost altijd ongeveer de volledige context van die sessie.

## Bouwen

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Vereist Visual Studio Build Tools (Roslyn `csc.exe`) en .NET Framework 4.x. Resultaat: `bin\wake-claude.exe`.

## Onderdelen

- `wake-claude.exe` - zie hierboven
- `WakeClaudeApi` - kleine API op het lokale netwerk die de exe aanroept (nog te bouwen)

Zie `spike/FINDINGS.md` voor het onderzoek erachter.
