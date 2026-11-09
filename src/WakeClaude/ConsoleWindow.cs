using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WakeClaude
{
    /// <summary>
    /// Het consolevenster van een ander proces tonen, verbergen of sluiten. Terminalsessies starten
    /// verborgen: de console blijft een echte TTY (de interface rendert gewoon door), er is alleen
    /// geen venster en geen taakbalkknop. Zo staat er na het aanmelden geen rij vensters open.
    /// </summary>
    static class ConsoleWindow
    {
        const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
        const uint WM_CLOSE = 0x0010;

        delegate bool EnumProc(IntPtr window, IntPtr param);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

        /// <summary>
        /// Het consolevenster van een proces. Windows meldt voor een ConsoleWindowClass-venster de
        /// pid van het consoleprogramma zelf, niet die van conhost.exe, dus de pid uit
        /// `claude agents --json` volstaat. IntPtr.Zero: het proces draait niet meer, of het heeft
        /// geen eigen consolevenster (Windows Terminal host de console zonder zo'n venster).
        /// </summary>
        public static IntPtr Find(int pid)
        {
            var found = IntPtr.Zero;
            EnumProc callback = (window, param) =>
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != (uint)pid) return true;
                var name = new StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() != "ConsoleWindowClass") return true;
                found = window;
                return false;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }

        /// <summary>null als er geen consolevenster is, anders of het zichtbaar is.</summary>
        public static bool? Visible(int pid)
        {
            var window = Find(pid);
            return window == IntPtr.Zero ? (bool?)null : IsWindowVisible(window);
        }

        public static bool Hide(int pid) { return Switch(pid, SW_HIDE); }

        /// <summary>Tonen zonder de focus weg te nemen: de gebruiker is mogelijk elders aan het typen.</summary>
        public static bool Show(int pid) { return Switch(pid, SW_SHOWNOACTIVATE); }

        static bool Switch(int pid, int command)
        {
            var window = Find(pid);
            if (window == IntPtr.Zero) return false;
            ShowWindow(window, command);
            return true;
        }

        /// <summary>
        /// Sluiten via het venster (WM_CLOSE): de console stuurt dan CTRL_CLOSE_EVENT en Claude Code
        /// mag zelf afronden. Blijft het proces hangen, dan alsnog hard afbreken. Geeft terug hoe het
        /// afliep, voor in het resultaat.
        /// </summary>
        public static string Stop(int pid, int timeoutSeconds)
        {
            Process process;
            try { process = Process.GetProcessById(pid); }
            catch (ArgumentException) { return "draaide niet meer"; }

            var window = Find(pid);
            var how = "gesloten";
            if (window != IntPtr.Zero) PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            else how = "afgebroken (geen consolevenster gevonden)";

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(5, timeoutSeconds));
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited) return how;
                Thread.Sleep(500);
            }
            try { process.Kill(); process.WaitForExit(5000); }
            catch (Exception e) { return "sluiten mislukt: " + e.Message; }
            return "afgebroken na " + Math.Max(5, timeoutSeconds) + " s wachten";
        }

        /// <summary>Of deze pid nog een draaiende claude.exe is: een pid uit het register kan hergebruikt zijn.</summary>
        public static bool IsClaude(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                var process = Process.GetProcessById(pid);
                return !process.HasExited && string.Equals(process.ProcessName, "claude", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }

    /// <summary>De commandoregel van draaiende claude.exe-processen.</summary>
    static class Processes
    {
        /// <summary>
        /// pid naar de naam uit `--remote-control "<naam>"`, voor elke draaiende claude.exe. Dat is
        /// wat een sessie herkenbaar houdt als haar sessie-ID verschuift, bijvoorbeeld na /clear.
        /// </summary>
        public static Dictionary<int, string> RemoteControlNames()
        {
            var result = new Dictionary<int, string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'claude.exe'"))
                using (var found = searcher.Get())
                    foreach (ManagementBaseObject process in found)
                        using (process)
                        {
                            var name = NameIn(process["CommandLine"] as string);
                            if (name != null) result[Convert.ToInt32(process["ProcessId"])] = name;
                        }
            }
            // Zonder WMI valt er niets te herstellen; dat mag nooit een start tegenhouden.
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (COMException) { }
            return result;
        }

        /// <summary>De waarde achter --remote-control, met of zonder aanhalingstekens.</summary>
        static string NameIn(string commandLine)
        {
            const string flag = "--remote-control";
            if (commandLine == null) return null;
            var at = commandLine.IndexOf(flag, StringComparison.Ordinal);
            if (at < 0) return null;
            at += flag.Length;
            while (at < commandLine.Length && commandLine[at] == ' ') at++;
            if (at >= commandLine.Length) return null;
            if (commandLine[at] != '"')
            {
                var space = commandLine.IndexOf(' ', at);
                return space < 0 ? commandLine.Substring(at) : commandLine.Substring(at, space - at);
            }
            at++;
            // Een naam met aanhalingstekens weigert Parse al, dus het eerste sluitteken is het juiste.
            var close = commandLine.IndexOf('"', at);
            return close < 0 ? null : commandLine.Substring(at, close - at);
        }
    }
}
