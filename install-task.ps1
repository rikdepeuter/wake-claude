# Registreert de taak "wake-claude keep-alive": elke 10 minuten en bij aanmelden.
#
# De taak draait onder het aangemelde account, zonder verhoogde rechten. Dat is nodig: ze moet in
# jouw bureaubladsessie kunnen klikken en typen in het venster van de Claude-app.

$ErrorActionPreference = 'Stop'
$naam = 'wake-claude keep-alive'
$script = Join-Path $PSScriptRoot 'keep-alive.ps1'
if (-not (Test-Path $script)) { throw "Niet gevonden: $script" }

$actie = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$script`""

# Zonder -RepetitionDuration herhaalt de taak onbeperkt; een expliciete duur wordt hier geweigerd.
$elke10 = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 10)
$bijAanmelden = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

$instellingen = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew -StartWhenAvailable
$hoofd = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $naam -Action $actie -Trigger $elke10, $bijAanmelden `
    -Settings $instellingen -Principal $hoofd -Force `
    -Description 'Start een nieuwe "Remote main"-sessie zodra die niet meer bereikbaar is.' | Out-Null

"Taak geregistreerd: $naam"
Get-ScheduledTask -TaskName $naam | Select-Object TaskName, State
