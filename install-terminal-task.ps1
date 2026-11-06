# Registreert de taak "wake-claude terminal-restore": bij aanmelden alle geregistreerde
# terminalsessies terug aanhaken. Staat los van "wake-claude keep-alive" (install-task.ps1).
#
# De taak draait onder het aangemelde account, interactief en zonder verhoogde rechten: de
# consolevensters moeten in jouw bureaubladsessie verschijnen.

$ErrorActionPreference = 'Stop'
$naam = 'wake-claude terminal-restore'
$script = Join-Path $PSScriptRoot 'terminal-restore.ps1'
if (-not (Test-Path $script)) { throw "Niet gevonden: $script" }

$actie = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""

$bijAanmelden = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$bijAanmelden.Delay = 'PT30S'   # de shell en het netwerk even de tijd geven

$instellingen = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew -StartWhenAvailable
$hoofd = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $naam -Action $actie -Trigger $bijAanmelden `
    -Settings $instellingen -Principal $hoofd -Force `
    -Description 'Haakt de terminalsessies uit %USERPROFILE%\.wake-claude\terminal-sessions.json terug aan na het aanmelden.' | Out-Null

"Taak geregistreerd: $naam"
Get-ScheduledTask -TaskName $naam | Select-Object TaskName, State
