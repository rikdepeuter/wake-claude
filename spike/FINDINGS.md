# Spike: een sessie tot leven wekken zonder tokens

Gemeten op 17 september 2026, Claude-desktop-app 2.110.1 (Microsoft Store), CLI 2.1.271.
Proefsessie "Test", kleine context, eigen transcript.

## Resultaat

| vraag | antwoord | bewijs |
|---|---|---|
| Kan een gestopte sessie van buitenaf opstarten zonder beurt? | **ja** | 2x reproduceerbaar, 0 tokens |
| Komt Remote Control vanzelf terug bij het opstarten? | **nee** | ook niet met `remoteControlUserEnabled: true` |
| Kan Remote Control aan zonder beurt? | **ja**, maar enkel binnenin een Claude-sessie | via de sessiebeheer-tool, 0 tokens |

## Recept om het proces op te starten

1. Laat de app eerst een **andere** sessie tonen:
   `claude://claude.ai/epitaxy/<local_id van een andere sessie>`
2. Open dan de doelsessie met:
   `claude://code/continue?session=<local_id>&source=desktop_action`

Het proces start binnen een seconde onder de app. Er komt hoogstens een `custom-title`-regel bij in het
transcript, geen gebruikers- of modelbericht.

**Valkuil:** staat de doelsessie al vooraan in de app, dan doet stap 2 niets. Het opstarten is een
neveneffect van ernaartoe navigeren, niet van de link zelf.

| ronde | app toonde vooraf | proces gestart |
|---|---|---|
| 1 | Devops | ja, 1 s na de link |
| 2 | Test (zelf) | nee |
| 3 | DV | ja, 1 s na de link |

## Wat niet werkt

- **UI Automation en MSAA**: tonen enkel de titelbalk. Chromium zet zijn toegankelijkheidsboom niet aan
  zonder de app te herstarten met een vlag of een systeembrede schermlezer-instelling.
- **`claude://claude.ai/epitaxy/<id>`** alleen: toont de sessie, start het proces niet.
- **Een verborgen CLI-sessie met `--remote-control`**: het proces draait, maar wordt nooit een sessie.
- **`claude --bg`**: draait wel, maar heeft geen app-record en geen Remote Control, dus onzichtbaar in de
  zijbalk en onbereikbaar van een andere machine.

## Kosten om te onthouden

Een bericht naar een grote sessie met koude cache schrijft de volledige context opnieuw weg. Eén
"ping" naar een sessie van ~1,2 miljoen tokens kostte 1.118.822 geschreven cache-tokens. Controleer
nooit met een bericht of een sessie leeft: `claude agents --json` toont het zonder model.

## Sessie-id's

- `local_<uuid>` - id van de desktop-app, gebruikt in de links
- `cliSessionId` - naam van het transcript `.jsonl`, gebruikt door `claude agents`
- de koppeling staat in `%APPDATA%\Claude\claude-code-sessions\**\local_<uuid>.json`, samen met `title`
  en `isArchived`

## Waarom Remote Control niet terugkomt

Uit de app-code (`app.asar`, sessiebeheer):

- Aanzetten op een sessie zonder actieve beurt faalt met *"requires an active session. Send a message
  first."* (`reason: "no_active_session"`).
- Een gewenste Remote Control wordt bewaard (`remoteControlUserEnabled`, `remoteControlUserRequested`) en
  hersteld met beleid `"next_turn"`: pas bij de **volgende beurt**, niet bij het opstarten van het proces.

Gevolg: een bestaande, stilstaande sessie wordt van buitenaf nooit bereikbaar zonder beurt. Een bericht
van een ander toestel komt er ook niet door, want dat heeft net die verbinding nodig. Het proces
opstarten is gratis; bereikbaar maken kost minstens een beurt, en die beurt kost de volledige context.

## Instellingen in `%APPDATA%\Claude\claude_desktop_config.json`

- `preferences.ccRemoteControlDefaultEnabled: true` - nieuwe Code-sessies krijgen Remote Control
  automatisch, zodra ze een beurt gehad hebben
- `preferences.remoteToolsDeviceName` - de naam waaronder deze computer op andere toestellen verschijnt

## Een nieuwe sessie starten die meteen bereikbaar is

Werkt, getest op 17 september 2026:

1. `claude://code/new?folder=<pad>&q=<prompt>&source=desktop_action`
   opent het nieuwe-sessiescherm met de map ingesteld en de prompt **ingevuld, niet verstuurd**.
2. **Klik in het invoerveld**, dan Enter. Enter alleen deed niets: de focus lag niet in het veld, en er
   hing een pop-up "Rate your conversation" over de rechterkant van het invoerveld.
3. De sessie start, draait de beurt, en krijgt automatisch Remote Control
   (`ccRemoteControlDefaultEnabled: true`).

Kost van die eerste beurt op een lege sessie:

| | tokens |
|---|---|
| invoer, niet gecachet | 2 |
| cache gelezen | 42.638 |
| cache geschreven | 10.873 |
| uitvoer | 4 |

Die ~53.000 tokens zijn de systeemprompt en tooldefinities, grotendeels gedeeld met andere sessies en dus
al in de cache. Dat is de minimale prijs van een nieuwe, bereikbare sessie.

Wat dit debugbaar maakte: een schermafbeelding van **alleen het Claude-venster**
(`GetWindowRect` + `CopyFromScreen`). Zonder die afbeelding bleef het blind toetsen.

## Wat het bouwen van de exe nog opleverde

Gemeten op 17 september 2026 met `wake-claude.exe`.

- **De app pauzeert zelf.** Na 900 s zonder activiteit: `[WarmLifecycle:session] Idle timeout reached`,
  gevolgd door `Pausing session local_… (idle_timeout)`, tenzij Remote Control verbonden is
  (`Skipping pause … remote control is active`). Een sessie die haar verbinding verliest, is dus binnen
  een kwartier gestopt.
- **Een gepauzeerde sessie start niet met `code/continue`.** De link brengt ze in beeld, maar het
  proces start pas met een bericht. Dat maakt geen verschil: bereikbaar maken kost toch een beurt.
- **Het register loopt achter.** `lastFocusedAt` werd niet bijgewerkt bij het openen, en een nieuwe
  sessie stond pas ~10 s na haar aanmaak in het register. Het log is sneller en betrouwbaarder:
  - `LocalSessions.setFocusedSession: sessionId=<id>` - welke sessie in beeld staat (`null` op het
    scherm voor een nieuwe sessie)
  - `Enabling remote control for session <id>`, gevolgd door `[remote-control] bridge_state: "connected"`
  - het log staat in `%LOCALAPPDATA%\Claude\logs\main.log`, geroteerd naar `main1.log` rond 10 MB
- **Enter te vroeg doet niets.** Kort na het laden van het nieuwe-sessiescherm werd Enter genegeerd; een
  tweede poging enkele seconden later werkte. De exe probeert opnieuw zolang dat scherm er nog staat.
- **Een naam die de sessie zichzelf geeft, blijft staan.** De app zet eerst een automatische titel
  (`titleSource: "auto"`), `set_session_title` overschrijft die (`titleSource: "tool"`) en die blijft.
- **Archiveren via de sessie werkt** zonder goedkeuring in bypass-modus; de app archiveert ook de
  Remote Control-sessie op de server.

## Officiele functie: mappen aanbieden via Remote Control

De app kan mappen op deze computer aanbieden, zodat je vanaf een ander toestel **zelf een nieuwe sessie
start** in die map ("Add a folder to Remote Control", vastgepinde mapslots, meldingen voor "a remotely
started Claude Code session"). Maar de initialisatie staat achter een feature gate:

    if (!gate) { log("[sessions-bridge] init skipped - gate off (yukon_silver_cuttlefish_desktop)") }

Bij deze gebruiker staat die gate uit: de instellingen verschijnen niet onder
Instellingen > Claude Code (of > Desktop). Niet bruikbaar zolang de uitrol hem niet bereikt.
