# Haakt na het aanmelden alle geregistreerde terminalsessies terug aan (wake-claude term-restore).
#
# Elke sessie krijgt een eigen consolevenster met `claude --resume <id> --remote-control "<naam>"`:
# geschiedenis en Remote Control-link blijven dezelfde, en er wordt geen bericht gestuurd, dus het
# kost geen tokens. Het register staat in %USERPROFILE%\.wake-claude\terminal-sessions.json.
#
# Bedoeld voor de taak "wake-claude terminal-restore"; zie install-terminal-task.ps1.

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\wake-claude.exe'
if (-not (Test-Path $exe)) { throw "Niet gevonden: $exe. Voer eerst build.ps1 uit." }

$log = Join-Path $env:USERPROFILE '.wake-claude\terminal-restore.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
"$(Get-Date -Format s) start" | Add-Content $log -Encoding UTF8

# Na het aanmelden heeft het netwerk soms nog even nodig; zonder netwerk verbindt Remote Control niet.
$wacht = 0
function NetwerkOk { try { [System.Net.Dns]::GetHostAddresses('claude.ai').Count -gt 0 } catch { $false } }
while ($wacht -lt 120 -and -not (NetwerkOk)) { Start-Sleep -Seconds 5; $wacht += 5 }
"$(Get-Date -Format s) netwerk na $wacht s" | Add-Content $log -Encoding UTF8

# Windows PowerShell 5.1 maakt van stderr van een programma een fout; de exe logt net naar stderr.
$ErrorActionPreference = 'Continue'
& $exe term-restore --timeout 120 --json 2>&1 | ForEach-Object { $_.ToString() } | Add-Content $log -Encoding UTF8
"$(Get-Date -Format s) einde, exitcode $LASTEXITCODE" | Add-Content $log -Encoding UTF8

exit $LASTEXITCODE
