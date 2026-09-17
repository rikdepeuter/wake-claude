# Spike, tweede poging: het Chromium-renderervenster rechtstreeks aanspreken.
# Het hoofdvenster van een Electron-app toont via UIA enkel de titelbalk; de webinhoud
# zit in een kindvenster van klasse Chrome_RenderWidgetHostHWND, en Chromium zet zijn
# toegankelijkheidsboom aan zodra dat venster een WM_GETOBJECT krijgt.

param([int]$MaxRegels = 200)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Win {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public static List<IntPtr> ChildrenOfClass(IntPtr parent, string cls) {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() == cls) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$app = Get-Process -Name claude -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*WindowsApps*' } | Select-Object -First 1
if (-not $app) { 'Geen Claude-hoofdvenster.'; exit 1 }

$renderers = [Win]::ChildrenOfClass($app.MainWindowHandle, 'Chrome_RenderWidgetHostHWND')
Write-Output ("renderervensters: " + $renderers.Count)
if ($renderers.Count -eq 0) { exit 1 }

# WM_GETOBJECT (0x003D) met OBJID_CLIENT (-4): het signaal waarop Chromium toegankelijkheid aanzet
foreach ($r in $renderers) { $null = [Win]::SendMessage($r, 0x003D, [IntPtr]::Zero, [IntPtr](-4)) }
Start-Sleep -Seconds 3

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$interessant = 'Button','Edit','Document','ListItem','TreeItem','Hyperlink','MenuItem','Link','Text','Tab','TabItem','ComboBox'
$regels = New-Object System.Collections.Generic.List[string]

function Loop($el, $diepte) {
    if ($diepte -gt 40 -or $regels.Count -ge $MaxRegels) { return }
    try {
        $c = $el.Current
        $type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $naam = ($c.Name -replace '\s+', ' ').Trim()
        if ($interessant -contains $type -and ($naam -or $type -in 'Edit','Document')) {
            $extra = @()
            if ($c.HasKeyboardFocus) { $extra += 'FOCUS' }
            try { if ($el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)) { $extra += 'value' } } catch {}
            try { if ($el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)) { $extra += 'invoke' } } catch {}
            if ($naam.Length -gt 60) { $naam = $naam.Substring(0, 60) + '…' }
            $regels.Add(('{0}{1,-8} "{2}" {3}' -f ('  ' * [Math]::Min($diepte, 10)), $type, $naam, $(if ($extra) { '[' + ($extra -join ',') + ']' } else { '' })))
        }
    } catch { }
    $k = $walker.GetFirstChild($el)
    while ($k -ne $null) { Loop $k ($diepte + 1); $k = $walker.GetNextSibling($k) }
}

foreach ($r in $renderers) {
    Write-Output '--- renderer ---'
    Loop ([System.Windows.Automation.AutomationElement]::FromHandle($r)) 0
}
$regels
Write-Output ("(" + $regels.Count + " regels)")
