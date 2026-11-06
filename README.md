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
- `--image <pad>` - bijlage voor de nieuwe sessie, meermaals te geven; enkel bij `term`
- `--paste-images` - de afbeeldingen van het laatste bericht van de aanroepende sessie meesturen; enkel
  bij `term`
- `--timeout <seconden>` - hoe lang wachten tot de sessie bereikbaar is, standaard 60
- `--lock-timeout <seconden>` - hoe lang wachten op een andere wake-claude, standaard 180
- `--max-context <tokens>` - bij `resume`: grens waarboven een gestopte sessie te duur is om te wekken,
  standaard 200000
- `--if-too-large skip|replace|revive` - wat er dan gebeurt: niets doen (standaard), vervangen door een
  nieuwe en de oude archiveren, of ze toch wekken
- `--only-if-idle <minuten>` - niets doen als er recenter invoer van muis of toetsenbord was
- `--retry-after <minuten>` - na een mislukking dezelfde opdracht zo lang met rust laten, standaard 60
- `--dry-run` - tonen wat er zou gebeuren, zonder iets te doen
- `--json` - resultaat als JSON op stdout

Exitcodes: `0` gelukt, `1` andere fout, `2` argumenten, `3` app start niet, `4` stap in de interface
mislukt, `5` timeout, `6` er draait al een wake-claude. Voortgang gaat naar stderr en naar
`%USERPROFILE%\.wake-claude\wake-claude.log`.

## Altijd één sessie bereikbaar houden

> Sinds 2026-11-06 is "Remote main" een terminalsessie (zie hieronder) en staat de taak
> "wake-claude keep-alive" uit. Na een herstart sloeg de keep-alive "Remote main" over: het aanmelden
> zelf telde als invoer voor `--only-if-idle`. Wat volgt, beschrijft de app-variant.

`keep-alive.ps1` draait `resume "Remote main" --only-if-idle 3 --if-too-large replace`: is die
sessie niet meer bereikbaar, dan wordt ze heractiveerd met behoud van context en geschiedenis. Enkel
als ze niet bestaat of boven `--max-context` zit, komt er een nieuwe lege met dezelfde naam en wordt
de oude gearchiveerd. Vanuit die sessie kan je de andere wekken. `install-task.ps1` registreert dat
als taak, elke 10 minuten en bij aanmelden, onder je eigen account. Beheerdersrechten zijn er niet:
de taak moet net in je aangemelde bureaubladsessie draaien om te kunnen klikken.

## Terminalsessies

Naast sessies in de desktop-app kan wake-claude sessies starten in een eigen consolevenster:
`claude --remote-control "<naam>"`. Die verschijnen net zo in claude.ai/code, de mobiele app en de
zijbalk van de desktop-app, maar er wordt niets geklikt of getypt en een update van de app sluit ze niet.

```
wake-claude.exe term <naam> [--folder <pad>] [--prompt "<tekst>"] [--image <pad>] [--paste-images]
wake-claude.exe term-restore
wake-claude.exe term-list
wake-claude.exe term-forget <naam>
```

| modus | draait | geregistreerd, gestopt | onbekend |
|---|---|---|---|
| `term` | niets doen | terug aanhaken | nieuwe starten en registreren |
| `term-restore` | niets doen | terug aanhaken | - |

- **Register:** `%USERPROFILE%\.wake-claude\terminal-sessions.json` met naam, map en sessie-ID. Het
  gesprek zelf bewaart Claude Code onder `~/.claude/projects/<map>/<id>.jsonl`.
- **Nieuw:** wake-claude kiest zelf het ID en start `claude --session-id <id> --remote-control "<naam>"`,
  met `--prompt` als eerste bericht. Zonder prompt wordt er niets verstuurd en kost het niets.
- **Terug aanhaken:** `claude --resume <id> --remote-control "<naam>"`. Geschiedenis en Remote
  Control-link blijven dezelfde, en er gaat geen bericht uit: geen tokens. Een sessie zonder één
  bericht heeft nog geen transcript; die start opnieuw met `--session-id` en hetzelfde ID.
- **Draait ze?** Volgens `claude agents --json`, net als voor de app-sessies.
- **Omgeving:** gestart vanuit een Claude-sessie erft de exe variabelen als `CLAUDE_CODE_CHILD_SESSION`
  en een `ANTHROPIC_BASE_URL` naar de app; dan meldt de nieuwe sessie zich niet als agent. Alle
  `CLAUDE*`-, `ANTHROPIC*`- en `MCP_*`-variabelen gaan daarom weg, behalve wat in je gebruikers- of
  machineomgeving staat.
- **Vertrouwen:** in een map die Claude Code nog niet vertrouwt, wacht de CLI op "Do you trust this
  folder?". Dat beantwoordt wake-claude niet: het weigert te starten en vraagt je eenmalig `claude` in
  die map te starten. Ook een vertrouwde map kan het opnieuw vragen, bv. als `.claude/settings.local.json`
  tools vooraf toestaat; dan meldt de sessie zich niet en zegt de timeout-fout dat het venster wacht.
- `term-forget` haalt een sessie enkel uit het register; een open venster blijft draaien tot je het sluit.

## Bijlagen meesturen

De CLI neemt geen afbeeldingen aan op de commandoregel (`--file` werkt enkel met geüploade bestanden).
Een sessie leest wél bestanden van schijf, en de Read tool toont een afbeelding echt als beeld. Daarom:
`--image` en `--paste-images` zetten de bestanden klaar en zetten hun paden onderaan de prompt.

```
wake-claude.exe term "QGIS-situatie" --prompt "waarom valideert dit niet?" --paste-images
```

- **Waar ze landen:** `%USERPROFILE%\.wake-claude\attachments\<tijdstempel>\`, genummerd in de volgorde
  waarin je ze gaf. Mappen ouder dan 30 dagen worden bij een volgende run opgeruimd.
- **Meteen gekopieerd**, nog vóór het venster opent. Dat is het punt: de sessie die wake-claude aanriep
  mag daarna `/clear` doen of haar scratchpad verliezen, de nieuwe sessie hangt nergens meer van af.
- **`--paste-images`** haalt de afbeeldingen uit het transcript van de aanroepende sessie
  (`CLAUDE_CODE_SESSION_ID`), uit het laatste bericht dat er bevat. Geplakte beelden staan daar als
  base64; ze worden gedecodeerd naar `geplakt-1.png` enzovoort. Buiten een Claude-sessie faalt dit met
  een uitleg: gebruik dan `--image`.
- **`--add-dir`** wordt meegegeven voor de bijlagemap, zodat de sessie er zonder toestemmingsvraag bij
  kan. Die vlag staat achteraan op de commandoregel: ze neemt meerdere mappen en zou een prompt die
  erachter staat als map opslokken.
- Enkel bij `term`. De app-modi typen de prompt teken voor teken in het invoerveld en kunnen `--add-dir`
  niet meegeven, dus daar wordt het geweigerd. Bij terug aanhaken gaat er sowieso geen bericht uit.

## Eén run tegelijk

Het register en `state.json` worden gelezen, aangepast en teruggeschreven, en de app-modi klikken en
typen in één venster. Twee runs tegelijk zouden dus elkaars registratie overschrijven, twee vensters
voor dezelfde naam openen, of in elkaars invoerveld typen. Daarom neemt elke run die iets verandert
eerst `%USERPROFILE%\.wake-claude\lock` (een bestand met `FileShare.None`), en houdt die vast tot het
einde - ook tijdens het wachten tot de sessie zich meldt, want net dan zou een tweede run de sessie nog
niet zien draaien.

Wie moet wachten, zegt dat in het logboek en meldt achteraf `gewacht: <n> s op een andere wake-claude`.
Duurt het langer dan `--lock-timeout` (standaard 180 s), dan stopt de run met exitcode 6 en noemt ze de
houder (`lock.owner`: pid, modus en starttijd). `term-list` en elke `--dry-run` nemen geen lock: die
lezen alleen, en het register wordt altijd via een tijdelijk bestand vervangen, dus een lezer ziet de
oude of de nieuwe inhoud, nooit een halve.

`install-terminal-task.ps1` registreert de taak "wake-claude terminal-restore": 30 s na het aanmelden
wacht `terminal-restore.ps1` op het netwerk en draait `term-restore`. Log:
`%USERPROFILE%\.wake-claude\terminal-restore.log`. Deze taak staat los van "wake-claude keep-alive".

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

## Waar de app haar bestanden bewaart

De Claude-app komt uit de Microsoft Store. Windows leidt haar schrijfacties naar `AppData` om naar
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\…`. Een programma dat vanuit de app start ziet
die omgeleide kopie, een geplande taak ziet de oude. Sinds een update van november 2026 verhuist de app
haar gebruikersdata bovendien naar `%LOCALAPPDATA%\Claude-Data\<submap>` (nu `claude_pzs8sxrjxfjjc`).

De exe legt daarom geen vaste plek vast. Ze bekijkt elke submap van `Claude-Data`, de pakketmap en de
klassieke map, en neemt voor het sessieregister (`claude-code-sessions`) en voor het log (`logs`) de map
waarin de app het laatst schreef. Achtergebleven oude kopieën winnen zo niet. Welke mappen gekozen werden,
staat in het logboek. Vindt ze geen register, dan stopt ze met een fout in plaats van een nieuwe sessie
te starten. Haar eigen logboek en toestand houdt ze in `%USERPROFILE%\.wake-claude`, buiten `AppData`.

Zie `spike/FINDINGS.md` voor het onderzoek erachter.
