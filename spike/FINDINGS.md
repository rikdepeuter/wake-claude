# Spike: een sessie tot leven wekken zonder tokens

Gemeten op 17 september 2026, Claude-desktop-app 2.110.1 (Microsoft Store), CLI 2.1.271.
Proefsessie "Test" (`local_e90e329b-...`), kleine context, eigen transcript.

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

## Open

Remote Control van buitenaf aanzetten, zonder Claude-sessie. Te onderzoeken: een item in het
commandopalet (`Ctrl+K`), of een link of parameter in de app-code.
