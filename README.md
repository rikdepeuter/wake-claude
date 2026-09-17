# wake-claude

Brengt lokale Claude Code-sessies in de desktop-app op aanvraag terug tot leven, of start een nieuwe.

## Waarom

De Claude-desktop-app update zichzelf via de Microsoft Store. Bij elke update sluit de app, en alle
interactieve sessies die eronder draaien, gaan mee. De app start daarna vanzelf opnieuw, maar de
sessies niet. Een sessie in de app openen is niet genoeg om ze te herstarten: pas een bericht
start het proces, en Remote Control volgt pas daarna.

## Onderdelen

- `wake-claude.exe` - zoekt een sessie op naam en maakt ze wakker, of start een nieuwe zonder argument
- `WakeClaudeApi` - kleine API op het lokale netwerk die de exe aanroept

## Status

In opbouw. Zie `spike/` voor wat er al onderzocht is.
