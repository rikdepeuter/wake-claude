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
>
> Bereikbaar blijven gebeurt nu via "wake-claude terminal-restore", die elke 5 minuten draait. Dat is
> een andere aanpak: `term-restore` klikt niet, typt niet en stuurt geen bericht, dus het kost niets en
> het stoort je niet terwijl je aan de laptop zit. `--only-if-idle` en `--max-context` zijn daar dan
> ook niet nodig.

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
wake-claude.exe term <naam> [--folder <pad>] [--prompt "<tekst>"] [--image <pad>] [--paste-images] [--visible]
wake-claude.exe term-restore [--visible]
wake-claude.exe term-list
wake-claude.exe term-forget <naam>
wake-claude.exe term-show <naam>
wake-claude.exe term-hide <naam>
wake-claude.exe term-stop <naam>
wake-claude.exe term-prune
wake-claude.exe term-archive [<naam>]
```

| modus | draait | geregistreerd, gestopt | onbekend |
|---|---|---|---|
| `term` | niets doen | terug aanhaken | nieuwe starten en registreren |
| `term-restore` | niets doen | terug aanhaken | - |

- **Register:** `%USERPROFILE%\.wake-claude\terminal-sessions.json` met naam, map, sessie-ID en de pid
  van de laatste start, plus `cseId`, `stoppedAt` en `pruneAfter` (zie "Archiveren is afsluiten"). Het
  gesprek zelf bewaart Claude Code onder `~/.claude/projects/<map>/<id>.jsonl`.
- **Nieuw:** wake-claude kiest zelf het ID en start `claude --session-id <id> --remote-control "<naam>"`,
  met `--prompt` als eerste bericht. Zonder prompt wordt er niets verstuurd en kost het niets.
- **Terug aanhaken:** `claude --resume <id> --remote-control "<naam>"`. Geschiedenis en Remote
  Control-link blijven dezelfde, en er gaat geen bericht uit: geen tokens. Een sessie zonder één
  bericht heeft nog geen transcript; die start opnieuw met `--session-id` en hetzelfde ID.
- **Draait ze?** Volgens `claude agents --json`, net als voor de app-sessies.
- **Verschoven sessie-ID:** `/clear` geeft een draaiend proces een nieuw sessie-ID, terwijl het venster
  en de Remote Control-naam dezelfde blijven. Het register wees dan naar een ID dat niemand meer draait:
  `term-list` zei "gestopt" en `term-restore` startte er bij de volgende aanmelding een tweede naast.
  Daarom zoekt wake-claude voor elke naam die niet bij de draaiende agents staat een `claude.exe` met
  `--remote-control "<die naam>"` in haar commandoregel (via WMI), en neemt het nieuwe ID over. Dat
  wordt gemeld als `bijgewerkt`. `term-list` toont het wel maar bewaart het niet, want die modus neemt
  geen lock; de eerstvolgende schrijvende actie legt het vast.
- **Omgeving:** gestart vanuit een Claude-sessie erft de exe variabelen als `CLAUDE_CODE_CHILD_SESSION`
  en een `ANTHROPIC_BASE_URL` naar de app; dan meldt de nieuwe sessie zich niet als agent. Alle
  `CLAUDE*`-, `ANTHROPIC*`- en `MCP_*`-variabelen gaan daarom weg, behalve wat in je gebruikers- of
  machineomgeving staat.
- **Vertrouwen:** in een map die Claude Code nog niet vertrouwt, wacht de CLI op "Do you trust this
  folder?". Dat beantwoordt wake-claude niet: het weigert te starten en vraagt je eenmalig `claude` in
  die map te starten. Ook een vertrouwde map kan het opnieuw vragen, bv. als `.claude/settings.local.json`
  tools vooraf toestaat; dan meldt de sessie zich niet en zegt de timeout-fout dat het venster wacht.
- `term-forget` haalt een sessie enkel uit het register; het proces blijft draaien tot je het sluit.
  Wil je ze echt weg, dan eerst `term-stop` en dan `term-forget`: een vergeten sessie vind je met de
  andere commando's niet meer terug.

### Archiveren is afsluiten

Een terminalsessie die je uit je sessielijst archiveert — op claude.ai, je gsm of een andere machine —
bleef hier gewoon draaien, en `term-restore` haakte ze binnen vijf minuten weer aan. Archiveren gebeurt
namelijk aan de serverkant: op deze machine is er niets van te zien. De desktop-app bewaart er geen
bestand van, en het archiefvlagje in `archived-sessions.idx` geldt alleen voor app-sessies op de machine
waar je het klikt.

Daarom vraagt `term-prune` het aan de sessie-API van Claude Code:

```
GET  https://api.anthropic.com/v1/code/sessions?limit=100&statuses=archived[&cursor=…]
POST https://api.anthropic.com/v1/code/sessions/<id>/archive        (200, of 409 als ze het al was)
POST https://api.anthropic.com/v1/code/sessions/<id>/unarchive
```

met het OAuth-token uit `~/.claude/.credentials.json` (scope `user:sessions:claude_code`) en
`anthropic-version: 2023-06-01`. Zet `WAKE_CLAUDE_API` om een ander adres te gebruiken.

- **Koppeling:** de API kent een sessie als `cse_<ULID>`. Die id staat als `bridgeSessionId` in het
  transcript; `term` bewaart ze bij het starten als `cseId`, en `term-prune` vult ze bij voor sessies
  van voor deze functie. `~/.claude/sessions/<pid>.json` heeft hetzelfde veld, maar daar ontbreekt het
  vaak en het springt soms terug naar `null` — het transcript is de betrouwbare bron. Matchen gebeurt
  op die id en nooit op naam: de titel aan de accountkant drijft af van de Remote Control-naam.
- **Afsluiten:** staat een geregistreerde sessie in de archieflijst, dan sluit `term-prune` haar venster
  en zet `stoppedAt` in het register. Ze blijft dus geregistreerd. `term-restore` laat zo'n sessie met
  rust — zonder dat zou ze bij de volgende ronde weer draaien.
- **Terughalen:** enkel manueel, met `term "<naam>"`. Dat haakt haar terug aan met haar geschiedenis en
  haalt haar ook uit het archief, zodat het volgende prune-rondje haar niet opnieuw afsluit. Lukt dat
  terugzetten niet (API onbereikbaar), dan krijgt ze een dag uitstel via `pruneAfter` en zegt het
  resultaat dat erbij.
- **Zelf archiveren:** `term-archive "<naam>"` archiveert een andere sessie én sluit ze meteen af.
  Zonder naam geldt het de sessie die het commando uitvoert; die wordt enkel gearchiveerd, want ze zou
  anders haar eigen antwoord afkappen. Het prune-rondje sluit haar af bij de volgende ronde van de taak. De skill
  `/stop` doet precies dit.
- **Uitstel:** `pruneAfter` in het register houdt een sessie tot dat tijdstip buiten schot. Gebruikt om
  sessies die al gearchiveerd waren toen dit erbij kwam, niet allemaal tegelijk af te sluiten.
- **Veiligheid:** mislukt de API-oproep, dan stopt `term-prune` met een fout en raakt het geen enkele
  sessie aan. Is het token verlopen, dan slaat het de ronde over (`actie: overgeslagen`). Niets doen is
  altijd beter dan een werksessie afsluiten op een halve meting. De sessie die `term-prune` zelf
  aanriep, wordt nooit afgesloten. Let op: dit is geen gedocumenteerde API; breekt ze, dan valt alles
  stil in plaats van verkeerd.

### Permissiemodus

Terminalsessies starten met `--permission-mode bypassPermissions`. Zonder die vlag nemen ze de
standaardmodus van Claude Code over, en die is sinds 2.1.x `auto`: dan blijft een onbewaakte sessie bij
de eerste de beste vraag staan, in een verborgen venster waar niemand ze ziet, en meldt ze zich niet eens
bij Remote Control — `term` loopt dan in zijn timeout.

### Verborgen vensters

Terminalsessies starten verborgen (`SW_HIDE`): geen venster op het scherm en geen taakbalkknop. Zo staat
er na het aanmelden geen rij consolevensters open, want `term-restore` haakt ze allemaal tegelijk terug aan.

Verborgen is niet hetzelfde als zonder console. Het venster bestaat, met een echte console erachter:
`process.stdout.isTTY` blijft `true`, met de gewone 120x30, dus de interface van Claude Code rendert
gewoon door, alleen kijkt er niemand naar. Een console helemaal weglaten (`CreateNoWindow` zonder
shell) zou dat net breken.

- `term-show` en `term-hide` zetten een draaiende sessie om. Tonen gebeurt met `SW_SHOWNOACTIVATE`, dus
  zonder de focus af te pakken terwijl je elders typt.
- `--visible` op `term` of `term-restore` geeft die ene start wel een venster.
- Meldt een nieuwe sessie zich niet binnen de timeout, dan toont wake-claude haar venster vanzelf: ze
  wacht dan meestal op een vraag, en verborgen zou je die nooit zien.
- `term-list` toont per sessie of haar venster zichtbaar of verborgen is.
- Het venster wordt gezocht met `EnumWindows` op klasse `ConsoleWindowClass`; Windows meldt daarbij de
  pid van het consoleprogramma zelf, niet die van `conhost.exe`. De pid komt uit `claude agents --json`,
  en voor een sessie die zich daar niet meldt uit het register, maar enkel als dat nog een draaiende
  `claude.exe` is: pids worden hergebruikt.
- Host Windows de console zonder eigen venster (Windows Terminal als standaardhost), dan vindt
  wake-claude niets en zegt dat ook. Op Windows 10 met de klassieke conhost werkt het.
- `term-stop` sluit het venster met `WM_CLOSE`, zodat de console `CTRL_CLOSE_EVENT` stuurt en Claude Code
  zelf mag afronden; hangt het proces daarna nog, dan wordt het alsnog afgebroken. De sessie blijft
  geregistreerd, dus `term-restore` haalt ze gewoon terug.

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
- **Geen `--add-dir`.** Die vlag leek logisch voor de bijlagemap, maar met haar erbij deed de CLI er
  ongeveer twee minuten over voor ze met Remote Control verbond; zonder is ze er in enkele seconden.
  wake-claude wacht standaard 60 s en meldde dus exitcode 5 terwijl de sessie gewoon aan het opstarten
  was, waarna een tweede sessie met dezelfde naam aan dezelfde opdracht begon. De bijlagen zijn ook
  zonder de vlag leesbaar: `term` start in `bypassPermissions`, en zet `~/.wake-claude/attachments`
  anders in `permissions.additionalDirectories` van je globale settings.
- Enkel bij `term`. De app-modi typen de prompt teken voor teken in het invoerveld; bij terug aanhaken
  gaat er sowieso geen bericht uit.

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

`install-terminal-task.ps1` registreert de taak "wake-claude terminal-restore" met twee triggers: 30 s
na het aanmelden, en daarna elke 5 minuten onbeperkt. `terminal-restore.ps1` wacht eerst op het
netwerk, draait dan `term-prune` en daarna `term-restore`: eerst opruimen wat je elders archiveerde,
dan pas aanhaken wat moet blijven draaien. Loopt `term-prune` mis, dan gaat het aanhaken gewoon door.
Log: `%USERPROFILE%\.wake-claude\terminal-restore.log`.

Onbeperkt herhalen is een herhaaltrigger *zonder* `-RepetitionDuration`: leeg betekent oneindig. Met
`[TimeSpan]::MaxValue` wordt het `P99999999DT23H59M59S` en weigert de Task Scheduler de XML met
"The task XML contains a value which is incorrectly formatted or out of range".

Die herhaling is de ingang van buitenaf: valt een sessie weg terwijl er niemand aan de laptop zit, dan
staat ze binnen vijf minuten weer klaar. Draaiende sessies worden met rust gelaten en er gaat geen
bericht uit, dus een ronde die niets te doen heeft, kost ook niets. De taak staat los van
"wake-claude keep-alive", die enkel app-sessies wekt en daarvoor wel klikt, typt en context verbruikt.

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
