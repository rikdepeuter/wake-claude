using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WakeClaude
{
    /// <summary>
    /// Bediening van de Claude-desktop-app. De inhoud van het venster is niet via UI Automation
    /// bereikbaar, dus er wordt geklikt en getypt. Elke muisklik en toetsaanslag gaat pas door
    /// nadat gecontroleerd is dat het Claude-venster vooraan staat.
    /// </summary>
    static class AppWindow
    {
        const string AppUserModelId = "Claude_pzs8sxrjxfjjc!Claude";

        // Afstand van het invoerveld tot de onderrand van het venster, bij 96 dpi.
        const int ComposerFromBottom = 63;

        public static IntPtr EnsureRunning(int timeoutSeconds)
        {
            var handle = FindWindow();
            if (handle != IntPtr.Zero) return handle;

            Log.Info("Claude-app draait niet, wordt gestart.");
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + AppUserModelId) { UseShellExecute = false });

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(timeoutSeconds, 60));
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(1000);
                handle = FindWindow();
                if (handle != IntPtr.Zero)
                {
                    // Het venster bestaat voor de app links kan verwerken.
                    Thread.Sleep(10000);
                    return handle;
                }
            }
            throw new WakeException(ExitCodes.App, "De Claude-app startte niet.");
        }

        static IntPtr FindWindow()
        {
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainModule.FileName.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                        return p.MainWindowHandle;
                }
                catch { } // processen van een ander account of die net stoppen
            }
            return IntPtr.Zero;
        }

        public static void OpenLink(string url)
        {
            Log.Info("Link: " + (url.Length > 140 ? url.Substring(0, 140) + "…" : url));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public static void ClickComposer(IntPtr handle)
        {
            BringToFront(handle);
            RECT r;
            GetWindowRect(handle, out r);
            double scale = GetDpiForWindow(handle) / 96.0;
            if (scale <= 0) scale = 1;

            // Links van het midden: in het invoerveld, met of zonder zijbalk, en weg van
            // meldingen die rechtsonder verschijnen.
            int x = r.Left + (int)((r.Right - r.Left) * 0.45);
            int y = r.Bottom - (int)(ComposerFromBottom * scale);

            POINT old;
            GetCursorPos(out old);
            AssertForeground(handle);
            SetCursorPos(x, y);
            Send(Mouse(MOUSEEVENTF_LEFTDOWN), Mouse(MOUSEEVENTF_LEFTUP));
            Thread.Sleep(150);
            SetCursorPos(old.X, old.Y);
            Thread.Sleep(300);
        }

        public static void TypeText(IntPtr handle, string text)
        {
            AssertForeground(handle);
            foreach (var c in text)
            {
                // Een regeleinde zou het bericht versturen: Shift+Enter houdt het in het veld.
                if (c == '\n') { AssertForeground(handle); Send(Key(VK_SHIFT, false), Key(VK_RETURN, false), Key(VK_RETURN, true), Key(VK_SHIFT, true)); }
                else if (c != '\r') Send(Unicode(c, false), Unicode(c, true));
                Thread.Sleep(10);
            }
        }

        public static void PressEnter(IntPtr handle)
        {
            AssertForeground(handle);
            Send(Key(VK_RETURN, false), Key(VK_RETURN, true));
        }

        public static void BringToFront(IntPtr handle)
        {
            if (GetForegroundWindow() == handle) return;
            if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
            // Windows laat een achtergrondproces niet zomaar een venster vooraan zetten;
            // een korte Alt-aanslag ervoor is de gangbare manier om dat toe te staan.
            keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(handle);
            Thread.Sleep(400);
        }

        static void AssertForeground(IntPtr handle)
        {
            if (GetForegroundWindow() == handle) return;
            BringToFront(handle);
            if (GetForegroundWindow() != handle)
                throw new WakeException(ExitCodes.Ui, "Het Claude-venster staat niet vooraan; er is niets geklikt of getypt.");
        }

        // --- Win32 ---

        const int SW_RESTORE = 9;
        const ushort VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_MENU = 0x12;
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
        const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }

        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();

        static INPUT Key(ushort vk, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 };
            return i;
        }

        static INPUT Unicode(char c, bool up)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) };
            return i;
        }

        static INPUT Mouse(uint flags)
        {
            var i = new INPUT { type = INPUT_MOUSE };
            i.u.mi = new MOUSEINPUT { dwFlags = flags };
            return i;
        }

        static void Send(params INPUT[] inputs)
        {
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
                throw new WakeException(ExitCodes.Ui, "Windows weigerde de invoer (vergrendeld scherm of geen actieve console?).");
        }
    }
}
