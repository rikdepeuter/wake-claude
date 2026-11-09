# Registreert de taak "wake-claude terminal-restore": alle geregistreerde terminalsessies terug
# aanhaken, bij het aanmelden en daarna elke 5 minuten. Dat tweede is de ingang van buitenaf: valt
# een sessie weg, dan staat ze binnen vijf minuten weer klaar zonder dat er iemand aan de laptop moet.
# Het kost niets: term-restore stuurt geen bericht en laat draaiende sessies met rust.
#
# Staat los van "wake-claude keep-alive" (install-task.ps1), die de app-sessies wekt door te klikken
# en te typen; die heeft hier niets mee te maken.
#
# De taak draait onder het aangemelde account, interactief en zonder verhoogde rechten: de
# consolevensters moeten in jouw bureaubladsessie verschijnen, ook al staan ze verborgen.

$ErrorActionPreference = 'Stop'
$naam = 'wake-claude terminal-restore'
$script = Join-Path $PSScriptRoot 'terminal-restore.ps1'
if (-not (Test-Path $script)) { throw "Niet gevonden: $script" }

$actie = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""

$bijAanmelden = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$bijAanmelden.Delay = 'PT30S'   # de shell en het netwerk even de tijd geven

# Herhalen vanaf vannacht, zodat het raster op een rond uur begint. Geen -RepetitionDuration:
# leeg betekent onbeperkt. Zet je er [TimeSpan]::MaxValue, dan wordt dat P99999999DT23H59M59S en
# weigert de Task Scheduler de XML ("value which is incorrectly formatted or out of range").
$elke5 = New-ScheduledTaskTrigger -Once -At (Get-Date).Date `
    -RepetitionInterval (New-TimeSpan -Minutes 5)

$instellingen = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew -StartWhenAvailable
$hoofd = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $naam -Action $actie -Trigger @($bijAanmelden, $elke5) `
    -Settings $instellingen -Principal $hoofd -Force `
    -Description 'Haakt de terminalsessies uit %USERPROFILE%\.wake-claude\terminal-sessions.json terug aan: bij het aanmelden en elke 5 minuten.' | Out-Null

"Taak geregistreerd: $naam"
Get-ScheduledTask -TaskName $naam | Select-Object TaskName, State
(Get-ScheduledTask -TaskName $naam).Triggers | Format-List -Property CimClass, StartBoundary, Repetition
