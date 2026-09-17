# Spike-hulp: toetsen naar het Claude-venster sturen, en ALLEEN naar het Claude-venster.
#
# De app-inhoud is niet via UI Automation bereikbaar, dus we werken blind met het
# toetsenbord. De veiligheid zit in Assert-Voorgrond: vlak voor elke toetsaanslag wordt
# gecontroleerd dat het Claude-hoofdvenster vooraan staat. Is dat niet zo, dan stopt
# het script in plaats van toetsen in een ander programma te typen.
#
# Gebruik (Windows PowerShell 5.1):  . .\spike\keys.ps1

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class Toetsen {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    const uint INPUT_KEYBOARD = 1, KEYUP = 0x2, UNICODE = 0x4;

    static INPUT Vk(ushort vk, bool up) {
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYUP : 0 };
        return i;
    }
    static INPUT Uni(char c, bool up) {
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE | (up ? KEYUP : 0) };
        return i;
    }
    static void Stuur(params INPUT[] inputs) { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))); }

    // Windows laat een achtergrondproces niet zomaar een venster naar voren halen.
    // Een korte Alt-aanslag vlak ervoor is de gangbare, onschuldige manier om dat toe te staan.
    public static bool NaarVoren(IntPtr h) {
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(h);
        Thread.Sleep(300);
        return GetForegroundWindow() == h;
    }

    public static void Combinatie(ushort modifier, ushort toets) {
        Stuur(Vk(modifier, false), Vk(toets, false), Vk(toets, true), Vk(modifier, true));
    }
    public static void CtrlShift(ushort toets) {
        Stuur(Vk(0x11, false), Vk(0x10, false), Vk(toets, false), Vk(toets, true), Vk(0x10, true), Vk(0x11, true));
    }
    public static void Tekst(string s) {
        foreach (var c in s) { Stuur(Uni(c, false), Uni(c, true)); Thread.Sleep(15); }
    }
    public static void Toets(ushort vk) { Stuur(Vk(vk, false), Vk(vk, true)); }
}
"@

function Get-ClaudeVenster {
    $app = Get-Process -Name claude -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 -and $_.Path -like '*WindowsApps*' } | Select-Object -First 1
    if (-not $app) { throw 'Claude-app draait niet of heeft geen hoofdvenster.' }
    return $app.MainWindowHandle
}

function Assert-Voorgrond([IntPtr]$h) {
    if ([Toetsen]::GetForegroundWindow() -ne $h) {
        if (-not [Toetsen]::NaarVoren($h)) { throw 'Claude-venster staat niet vooraan - geen toetsen gestuurd.' }
    }
}

function Stuur-CtrlN([IntPtr]$h)          { Assert-Voorgrond $h; [Toetsen]::Combinatie(0x11, 0x4E) }   # Ctrl+N
function Stuur-Tekst([IntPtr]$h, [string]$t) { Assert-Voorgrond $h; [Toetsen]::Tekst($t) }
function Stuur-Enter([IntPtr]$h)          { Assert-Voorgrond $h; [Toetsen]::Toets(0x0D) }
function Stuur-Escape([IntPtr]$h)         { Assert-Voorgrond $h; [Toetsen]::Toets(0x1B) }
