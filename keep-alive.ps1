# Houdt één sessie altijd bereikbaar vanaf een ander toestel.
#
# "Remote main" wordt nooit heractiveerd: valt haar verbinding weg, dan komt er een nieuwe
# lege sessie met dezelfde naam en wordt de oude gearchiveerd. Vanuit die sessie kan je de andere
# sessies wekken. Zit er iemand aan de computer, dan gebeurt er niets: de exe klikt en typt namelijk
# in het venster van de app.
#
# Bedoeld voor de taak "wake-claude keep-alive"; zie install-task.ps1.

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\wake-claude.exe'
if (-not (Test-Path $exe)) { throw "Niet gevonden: $exe. Voer eerst build.ps1 uit." }

# Als geplande taak is er geen console: alles naar een logbestand.
# Niet in AppData: dat wordt voor de Store-app omgeleid, en dan zie je twee verschillende logboeken.
$log = Join-Path $env:USERPROFILE '.wake-claude\keep-alive.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
"$(Get-Date -Format s) start" | Add-Content $log -Encoding UTF8

# Windows PowerShell 5.1 maakt van stderr van een programma een fout; de exe logt net naar stderr.
$ErrorActionPreference = 'Continue'
& $exe fresh 'Remote main' --only-if-idle 3 --timeout 120 --json 2>&1 |
    ForEach-Object { $_.ToString() } | Add-Content $log -Encoding UTF8
"$(Get-Date -Format s) einde, exitcode $LASTEXITCODE" | Add-Content $log -Encoding UTF8

exit $LASTEXITCODE
