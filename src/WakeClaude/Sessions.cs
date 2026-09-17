using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WakeClaude
{
    /// <summary>Een Code-sessie zoals de desktop-app ze bijhoudt in zijn register.</summary>
    class Session
    {
        public string LocalId;
        public string CliSessionId;
        public string Title;
        public string Cwd;
        public bool IsArchived;
        public long CreatedAt;
        public long LastActivityAt;
        public long LastFocusedAt;
        public int CompletedTurns;
    }

    static class Json
    {
        static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        public static object Parse(string text) { return Serializer.DeserializeObject(text); }
        public static string Write(object value) { return Serializer.Serialize(value); }

        public static string Str(IDictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        public static long Long(IDictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
        }

        public static bool Bool(IDictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v is bool && (bool)v;
        }
    }

    /// <summary>
    /// De Claude-app komt uit de Microsoft Store. Windows leidt haar schrijfacties naar AppData om
    /// naar de map van het pakket. Wie zelf vanuit die app draait ziet de omgeleide kopie, wie van
    /// buitenaf draait (bijvoorbeeld een geplande taak) de oude. Daarom altijd de pakketmap eerst.
    /// </summary>
    static class AppPaths
    {
        const string Package = "Claude_pzs8sxrjxfjjc";

        public static string Roaming { get { return Kies(Environment.SpecialFolder.ApplicationData, "Roaming"); } }
        public static string Local { get { return Kies(Environment.SpecialFolder.LocalApplicationData, "Local"); } }

        static string Kies(Environment.SpecialFolder map, string tak)
        {
            var gewoon = Path.Combine(Environment.GetFolderPath(map), "Claude");
            var pakket = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", Package, "LocalCache", tak, "Claude");
            return Directory.Exists(pakket) ? pakket : gewoon;
        }

        /// <summary>Eigen logboek en toestand, buiten AppData: dat wordt namelijk omgeleid.</summary>
        public static string Eigen
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wake-claude"); }
        }
    }

    static class Time
    {
        public static long NowMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        public static DateTime Local(long ms) { return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime; }
    }

    /// <summary>Leest %APPDATA%\Claude\claude-code-sessions\**\local_*.json.</summary>
    static class Register
    {
        static string Root
        {
            get { return Path.Combine(AppPaths.Roaming, "claude-code-sessions"); }
        }

        public static List<Session> Load()
        {
            var result = new List<Session>();
            if (!Directory.Exists(Root)) return result;

            foreach (var file in Directory.EnumerateFiles(Root, "local_*.json", SearchOption.AllDirectories))
            {
                var o = TryRead(file);
                if (o == null) continue;
                result.Add(new Session
                {
                    LocalId = Json.Str(o, "sessionId") ?? Path.GetFileNameWithoutExtension(file),
                    CliSessionId = Json.Str(o, "cliSessionId"),
                    Title = Json.Str(o, "title"),
                    Cwd = Json.Str(o, "cwd"),
                    IsArchived = Json.Bool(o, "isArchived"),
                    CreatedAt = Json.Long(o, "createdAt"),
                    LastActivityAt = Json.Long(o, "lastActivityAt"),
                    LastFocusedAt = Json.Long(o, "lastFocusedAt"),
                    CompletedTurns = (int)Json.Long(o, "completedTurns"),
                });
            }
            return result;
        }

        public static Session Find(string localId)
        {
            return Load().FirstOrDefault(s => s.LocalId == localId);
        }

        // De app kan een bestand net aan het herschrijven zijn: dan even later opnieuw proberen.
        static IDictionary<string, object> TryRead(string file)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    string text;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                        text = reader.ReadToEnd();
                    return Json.Parse(text) as IDictionary<string, object>;
                }
                catch (IOException) { System.Threading.Thread.Sleep(100); }
                catch (ArgumentException) { System.Threading.Thread.Sleep(100); }
                catch (InvalidOperationException) { System.Threading.Thread.Sleep(100); }
            }
            return null;
        }
    }

    /// <summary>Draaiende sessieprocessen volgens `claude agents --json`, zonder model.</summary>
    static class Agents
    {
        /// <summary>cliSessionId → starttijd van het proces (ms).</summary>
        public static Dictionary<string, long> Running()
        {
            var psi = new ProcessStartInfo("cmd.exe", "/d /c claude agents --json")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                var output = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { }
                    throw new WakeException(ExitCodes.Other, "`claude agents --json` antwoordde niet binnen 30 seconden.");
                }
                var list = Json.Parse(output.Result) as object[];
                if (list == null)
                    throw new WakeException(ExitCodes.Other, "`claude agents --json` gaf geen lijst terug.");

                var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in list.OfType<IDictionary<string, object>>())
                {
                    var id = Json.Str(item, "sessionId");
                    if (id != null) result[id] = Json.Long(item, "startedAt");
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Het log van de app. Of Remote Control verbonden is, staat enkel daar:
    /// "Enabling remote control for session local_…" bij het aanzetten, en om het kwartier
    /// "Skipping pause for session local_… - remote control is active". Welke sessie in beeld
    /// staat ook ("setFocusedSession"); het register wordt daarvoor niet meteen bijgewerkt.
    /// </summary>
    class AppLog
    {
        static readonly Regex LineTime = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) ", RegexOptions.Compiled);

        readonly List<KeyValuePair<DateTime, string>> lines = new List<KeyValuePair<DateTime, string>>();

        static string LogDir
        {
            get { return Path.Combine(AppPaths.Local, "logs"); }
        }

        public static AppLog Load()
        {
            var log = new AppLog();
            // main1.log is het vorige, geroteerde log; eerst dat, dan het huidige.
            foreach (var name in new[] { "main1.log", "main.log" })
            {
                var file = Path.Combine(LogDir, name);
                if (!File.Exists(file)) continue;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.IndexOf("remote control", StringComparison.OrdinalIgnoreCase) < 0 &&
                            line.IndexOf("bridge_state", StringComparison.Ordinal) < 0 &&
                            line.IndexOf("Pausing session", StringComparison.Ordinal) < 0 &&
                            line.IndexOf(FocusMarker, StringComparison.Ordinal) < 0) continue;
                        var m = LineTime.Match(line);
                        DateTime t;
                        if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t))
                            log.lines.Add(new KeyValuePair<DateTime, string>(t, line));
                    }
                }
            }
            return log;
        }

        const string FocusMarker = "setFocusedSession: sessionId=";

        /// <summary>Is dit de sessie die de app sinds dit tijdstip als laatste in beeld bracht?</summary>
        public bool FocusedSince(string localId, long sinceMs)
        {
            var since = Time.Local(sinceMs).AddSeconds(-1);
            string last = null;
            foreach (var entry in lines)
            {
                if (entry.Key < since) continue;
                var i = entry.Value.IndexOf(FocusMarker, StringComparison.Ordinal);
                if (i < 0) continue;
                var id = entry.Value.Substring(i + FocusMarker.Length).Trim();
                if (id != "null") last = id;
            }
            return last == localId;
        }

        /// <summary>Staat sinds dit tijdstip het scherm voor een nieuwe sessie vooraan (focus op geen sessie)?</summary>
        public bool NewSessionScreenSince(long sinceMs)
        {
            var since = Time.Local(sinceMs).AddSeconds(-1);
            string last = null;
            foreach (var entry in lines)
            {
                if (entry.Key < since) continue;
                var i = entry.Value.IndexOf(FocusMarker, StringComparison.Ordinal);
                if (i >= 0) last = entry.Value.Substring(i + FocusMarker.Length).Trim();
            }
            return last == "null";
        }

        /// <summary>Werd Remote Control aangezet of actief gezien voor deze sessie, na dit tijdstip, zonder latere fout?</summary>
        public bool ActiveSince(string localId, long sinceMs)
        {
            // Logregels hebben secondeprecisie.
            var since = Time.Local(sinceMs).AddSeconds(-2);
            bool active = false, enabling = false;
            foreach (var entry in lines)
            {
                if (entry.Key < since) continue;
                var text = entry.Value;

                // "connected" noemt geen sessie: het telt enkel vlak na het aanzetten van deze sessie.
                if (text.Contains("bridge_state: \"connected\""))
                {
                    if (enabling) { active = true; enabling = false; }
                    continue;
                }
                if (text.IndexOf(localId, StringComparison.Ordinal) < 0) continue;

                if (text.Contains("Enabling remote control for session"))
                    enabling = true;
                else if (text.Contains("remote control is active"))
                    active = true;
                else if (text.Contains("Failed to toggle remote control") || text.Contains("Pausing session"))
                    active = enabling = false;
            }
            return active;
        }
    }

    /// <summary>Contextgrootte van een sessie, uit de laatste token-telling in haar transcript.</summary>
    static class Transcript
    {
        public static long ContextTokens(string cliSessionId)
        {
            var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (cliSessionId == null || !Directory.Exists(projects)) return 0;

            var file = Directory.EnumerateFiles(projects, cliSessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) return 0;

            // Enkel het einde lezen: de laatste beurt staat daar.
            const int tail = 8 * 1024 * 1024;
            string text;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var start = Math.Max(0, fs.Length - tail);
                fs.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[fs.Length - start];
                int read = 0, n;
                while (read < buffer.Length && (n = fs.Read(buffer, read, buffer.Length - read)) > 0) read += n;
                text = Encoding.UTF8.GetString(buffer, 0, read);
            }

            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                IDictionary<string, object> o;
                try { o = Json.Parse(line) as IDictionary<string, object>; }
                catch { continue; }
                object message, usage;
                if (o == null || !o.TryGetValue("message", out message) || !(message is IDictionary<string, object>)) continue;
                if (!((IDictionary<string, object>)message).TryGetValue("usage", out usage) || !(usage is IDictionary<string, object>)) continue;
                var u = (IDictionary<string, object>)usage;
                return Json.Long(u, "input_tokens") + Json.Long(u, "cache_read_input_tokens") + Json.Long(u, "cache_creation_input_tokens");
            }
            return 0;
        }
    }
}
