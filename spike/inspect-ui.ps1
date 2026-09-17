# Spike: wat geeft de Claude-desktop-app prijs via Windows UI Automation?
# Draaien met Windows PowerShell 5.1:  powershell.exe -File spike\inspect-ui.ps1
#
# Electron/Chromium bouwt zijn toegankelijkheidsboom pas op zodra een UIA-client erom
# vraagt, dus we lezen twee keer met een korte pauze ertussen.

param([int]$MaxDiepte = 25, [int]$MaxRegels = 160)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$app = Get-Process -Name claude -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*WindowsApps*' } |
    Select-Object -First 1

if (-not $app) { Write-Output 'Geen Claude-app met een hoofdvenster gevonden.'; exit 1 }
Write-Output ("venster: '" + $app.MainWindowTitle + "'  pid " + $app.Id)

$root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)

# eerste aanvraag zet de boom aan, tweede leest hem
$null = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Start-Sleep -Milliseconds 1500

$interessant = 'Button','Edit','Document','ListItem','TreeItem','Hyperlink','MenuItem','Group','Text'
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$regels = New-Object System.Collections.Generic.List[string]

function Loop($el, $diepte) {
    if ($diepte -gt $MaxDiepte -or $regels.Count -ge $MaxRegels) { return }
    try {
        $c = $el.Current
        $type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $naam = $c.Name
        if ($interessant -contains $type -and ($naam -or $type -in 'Edit','Document')) {
            $extra = @()
            if ($c.IsKeyboardFocusable) { $extra += 'focusbaar' }
            if ($c.HasKeyboardFocus)    { $extra += 'HEEFT FOCUS' }
            try { if ($el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)) { $extra += 'value' } } catch {}
            try { if ($el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)) { $extra += 'invoke' } } catch {}
            if ($naam.Length -gt 70) { $naam = $naam.Substring(0, 70) + '…' }
            $regels.Add(('{0}{1,-9} "{2}"  [{3}]' -f ('  ' * [Math]::Min($diepte, 12)), $type, $naam, ($extra -join ',')))
        }
    } catch { }
    $kind = $walker.GetFirstChild($el)
    while ($kind -ne $null) {
        Loop $kind ($diepte + 1)
        $kind = $walker.GetNextSibling($kind)
    }
}

Loop $root 0
$regels
Write-Output ("(" + $regels.Count + " regels)")
