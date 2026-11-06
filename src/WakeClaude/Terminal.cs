using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WakeClaude
{
    /// <summary>Een terminalsessie die wake-claude zelf startte en na een herstart terug aanhaakt.</summary>
    class TerminalSession
    {
        public string Name;
        public string Folder;
        public string Id;
        public string Created;
    }

    /// <summary>
    /// Sessies in een eigen consolevenster: `claude --remote-control "naam"`. Ze staan los van de
    /// desktop-app, dus er wordt niets geklikt of getypt. Claude Code bewaart het gesprek zelf onder
    /// het sessie-ID; hier wordt enkel bijgehouden welke naam bij welk ID en welke map hoort, in
    /// terminal-sessions.json. `--resume` haakt een sessie terug aan met haar geschiedenis en met
    /// dezelfde Remote Control-link, zonder bericht en dus zonder tokens.
    /// </summary>
    static class Terminal
    {
        static readonly string File = Path.Combine(AppPaths.Eigen, "terminal-sessions.json");

        public static string RegistryPath { get { return File; } }

        public static List<TerminalSession> Load()
        {
            try
            {
                var list = Json.Parse(System.IO.File.ReadAllText(File)) as object[];
                if (list == null) return new List<TerminalSession>();
                return list.OfType<IDictionary<string, object>>().Select(d => new TerminalSession
                {
                    Name = Json.Str(d, "name"),
                    Folder = Json.Str(d, "folder"),
                    Id = Json.Str(d, "id"),
                    Created = Json.Str(d, "created"),
                }).Where(s => s.Name != null && s.Id != null && s.Folder != null).ToList();
            }
            catch (FileNotFoundException) { return new List<TerminalSession>(); }
            catch (DirectoryNotFoundException) { return new List<TerminalSession>(); }
        }

        static void Save(List<TerminalSession> sessions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            var data = sessions.Select(s => new Dictionary<string, object>
            {
                { "name", s.Name }, { "folder", s.Folder }, { "id", s.Id }, { "created", s.Created },
            }).ToArray();
            var tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, Json.Write(data), Encoding.UTF8);
            if (System.IO.File.Exists(File)) System.IO.File.Replace(tmp, File, null);
            else System.IO.File.Move(tmp, File);
        }

        static TerminalSession Find(List<TerminalSession> sessions, string name)
        {
            return sessions.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>term: draait: niets; geregistreerd maar gestopt: terug aanhaken; onbekend: nieuw.</summary>
        public static void Ensure(Options o, Dictionary<string, object> result)
        {
            var sessions = Load();
            var known = Find(sessions, o.Name);
            var running = Agents.Running();

            if (known != null)
            {
                result["sessie"] = known.Id;
                result["map"] = known.Folder;
                if (running.ContainsKey(known.Id))
                {
                    result["actie"] = "niets";
                    result["reden"] = "terminalsessie met deze naam draait al";
                    return;
                }
                result["actie"] = "aangehaakt";
                result["reden"] = "geregistreerde terminalsessie draait niet";
                if (!string.IsNullOrWhiteSpace(o.Prompt) || o.Images.Count > 0 || o.PasteImages)
                    result["opmerking"] = "--prompt en bijlagen worden genegeerd bij aanhaken; stuur het bericht via Remote Control";
                if (o.DryRun) return;
                Launch(known, resume: true, prompt: null);
                WaitRunning(known.Id, o.TimeoutSeconds);
                return;
            }

            var session = new TerminalSession
            {
                Name = o.Name,
                Folder = Path.GetFullPath(o.Folder),
                Id = Guid.NewGuid().ToString(),
                Created = DateTime.Now.ToString("s"),
            };
            result["actie"] = "nieuw";
            result["reden"] = "geen terminalsessie met deze naam geregistreerd";
            result["sessie"] = session.Id;
            result["map"] = session.Folder;
            if (!IsTrusted(session.Folder)) result["opmerking"] = "map is niet vertrouwd door Claude Code; zonder dat start de sessie niet";

            // Bijlagen nu al wegschrijven, nog voor het venster opent: daarna hangt de nieuwe sessie
            // niet meer af van de sessie die wake-claude aanriep.
            var bijlagen = Attachments.Collect(o);
            if (bijlagen != null) result["bijlagen"] = bijlagen.Files;

            if (o.DryRun) return;
            if (!IsTrusted(session.Folder))
                throw new WakeException(ExitCodes.Arguments, "Map " + session.Folder + " is niet vertrouwd door Claude Code. Start eenmalig `claude` in die map en kies \"Yes, I trust this folder\".");

            // Eerst registreren: ook als het venster meteen sluit, kan restore het later opnieuw proberen.
            sessions.Add(session);
            Save(sessions);
            Launch(session, resume: false, prompt: Attachments.Decorate(o.Prompt, bijlagen),
                   addDir: bijlagen != null ? bijlagen.Dir : null);
            WaitRunning(session.Id, o.TimeoutSeconds);
        }

        /// <summary>term-restore: elke geregistreerde sessie die niet draait, terug aanhaken.</summary>
        public static void Restore(Options o, Dictionary<string, object> result)
        {
            var sessions = Load();
            var running = Agents.Running();
            var started = new List<string>();
            var alreadyRunning = new List<string>();
            var failed = new List<string>();

            foreach (var s in sessions)
            {
                if (running.ContainsKey(s.Id)) { alreadyRunning.Add(s.Name); continue; }
                if (!Directory.Exists(s.Folder)) { failed.Add(s.Name + " (map bestaat niet: " + s.Folder + ")"); continue; }
                if (o.DryRun) { started.Add(s.Name); continue; }
                try
                {
                    Launch(s, resume: true, prompt: null);
                    started.Add(s.Name);
                    Thread.Sleep(1500); // niet alle vensters tegelijk: elk proces verbindt met Remote Control
                }
                catch (Exception e) { failed.Add(s.Name + " (" + e.Message + ")"); }
            }

            if (!o.DryRun && started.Count > 0)
            {
                var deadline = DateTime.UtcNow.AddSeconds(o.TimeoutSeconds);
                var waitFor = sessions.Where(s => started.Contains(s.Name)).ToList();
                while (DateTime.UtcNow < deadline)
                {
                    running = Agents.Running();
                    if (waitFor.All(s => running.ContainsKey(s.Id))) break;
                    Thread.Sleep(2000);
                }
                foreach (var s in waitFor.Where(s => !running.ContainsKey(s.Id)))
                {
                    started.Remove(s.Name);
                    failed.Add(s.Name + " (meldt zich niet na " + o.TimeoutSeconds + " s; wacht het venster op een vraag?)");
                }
            }

            result["actie"] = started.Count > 0 ? "aangehaakt" : "niets";
            result["aangehaakt"] = started;
            result["draaiden al"] = alreadyRunning;
            if (failed.Count > 0)
            {
                result["mislukt"] = failed;
                throw new WakeException(ExitCodes.Other, failed.Count + " terminalsessie(s) niet terug aangehaakt.");
            }
        }

        public static void List(Dictionary<string, object> result)
        {
            var running = Agents.Running();
            result["register"] = RegistryPath;
            result["sessies"] = Load().Select(s => s.Name + " | " + (running.ContainsKey(s.Id) ? "draait" : "gestopt") + " | " + s.Folder + " | " + s.Id).ToList();
        }

        /// <summary>term-forget: uit het register halen, zodat restore ze niet meer start. Het proces blijft draaien.</summary>
        public static void Forget(Options o, Dictionary<string, object> result)
        {
            var sessions = Load();
            var known = Find(sessions, o.Name);
            if (known == null)
            {
                result["actie"] = "niets";
                result["reden"] = "geen terminalsessie met deze naam geregistreerd";
                return;
            }
            result["actie"] = "vergeten";
            result["sessie"] = known.Id;
            result["reden"] = "uit het register; een draaiend venster blijft open tot je het sluit";
            if (o.DryRun) return;
            sessions.Remove(known);
            Save(sessions);
        }

        // --- starten ---

        static void Launch(TerminalSession s, bool resume, string prompt, string addDir = null)
        {
            // Een sessie zonder één bericht heeft nog geen transcript; --resume vindt dan niets en stopt.
            // Dan opnieuw starten met hetzelfde ID.
            if (resume && !HasTranscript(s.Id)) resume = false;
            // In een niet-vertrouwde map wacht de CLI op "Do you trust this folder?" en verbindt ze
            // niet met Remote Control. Dat beantwoordt wake-claude niet in jouw plaats.
            if (!IsTrusted(s.Folder))
                throw new WakeException(ExitCodes.Arguments, "Map " + s.Folder + " is niet vertrouwd door Claude Code. Start eenmalig `claude` in die map en kies \"Yes, I trust this folder\".");
            var args = new List<string> { resume ? "--resume" : "--session-id", s.Id, "--remote-control", s.Name };
            if (!string.IsNullOrWhiteSpace(prompt)) args.Add(prompt.Trim());
            // --add-dir neemt meerdere mappen en zou een prompt erachter als map opslokken: dus laatst.
            if (!string.IsNullOrEmpty(addDir)) { args.Add("--add-dir"); args.Add(addDir); }
            var exe = ClaudeExe();
            var commandLine = string.Join(" ", args.Select(Quote));
            Log.Info("Terminal: " + exe + " " + (commandLine.Length > 200 ? commandLine.Substring(0, 200) + "…" : commandLine) + " (in " + s.Folder + ")");

            CleanInheritedEnvironment();
            // Met de shell starten geeft een console-programma een eigen, zichtbaar venster.
            Process.Start(new ProcessStartInfo(exe, commandLine)
            {
                UseShellExecute = true,
                WorkingDirectory = s.Folder,
                WindowStyle = ProcessWindowStyle.Normal,
            });
        }

        /// <summary>
        /// Wordt wake-claude vanuit een Claude-sessie gestart (de skill), dan erft het variabelen als
        /// CLAUDE_CODE_CHILD_SESSION, CLAUDE_CODE_SESSION_ID en een ANTHROPIC_BASE_URL naar de app.
        /// Daarmee denkt de nieuwe sessie dat ze een kind van die sessie is: ze verschijnt niet als
        /// agent en hangt aan de levensduur van de app. Weg ermee, behalve wat de gebruiker zelf
        /// blijvend instelde (gebruikers- of machineomgeving). ShellExecute geeft de omgeving van dit
        /// proces door, dus het volstaat ze hier te wissen.
        /// </summary>
        static void CleanInheritedEnvironment()
        {
            foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<object>().Select(k => k.ToString()).ToList())
            {
                var upper = name.ToUpperInvariant();
                if (!(upper.StartsWith("CLAUDE") || upper.StartsWith("ANTHROPIC") || upper.StartsWith("MCP_"))) continue;
                if (upper == "WAKE_CLAUDE_CLI") continue;
                if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) != null) continue;
                if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine) != null) continue;
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        /// <summary>Of ~/.claude.json voor deze map hasTrustDialogAccepted bevat (sleutels met / of \).</summary>
        static bool IsTrusted(string folder)
        {
            var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
            try
            {
                var root = Json.Parse(System.IO.File.ReadAllText(config)) as IDictionary<string, object>;
                object p;
                var projects = root != null && root.TryGetValue("projects", out p) ? p as IDictionary<string, object> : null;
                if (projects == null) return true;   // onbekend formaat: niet blokkeren, de CLI vraagt het dan zelf
                Func<string, string> norm = x => x.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
                var target = norm(Path.GetFullPath(folder));
                return projects.Any(kv => norm(kv.Key) == target && kv.Value is IDictionary<string, object> &&
                                          Json.Bool((IDictionary<string, object>)kv.Value, "hasTrustDialogAccepted"));
            }
            catch (IOException) { return true; }
            catch (ArgumentException) { return true; }
        }

        static bool HasTranscript(string id)
        {
            var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
            try { return Directory.Exists(projects) && Directory.EnumerateFiles(projects, id + ".jsonl", SearchOption.AllDirectories).Any(); }
            catch (IOException) { return true; }               // bij twijfel hervatten: dat verliest nooit geschiedenis
            catch (UnauthorizedAccessException) { return true; }
        }

        static void WaitRunning(string id, int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(2000);
                if (Agents.Running().ContainsKey(id)) return;
            }
            throw new WakeException(ExitCodes.Timeout, "De terminalsessie " + id + " meldt zich niet na " + timeoutSeconds +
                " s. Staat het venster open, dan wacht het waarschijnlijk op een vraag (bv. \"Do you trust this folder?\"); beantwoord die en draai term-restore.");
        }

        /// <summary>Het echte claude.exe: de npm-wrappers (claude.cmd) zouden de prompt door cmd laten interpreteren.</summary>
        static string ClaudeExe()
        {
            var env = Environment.GetEnvironmentVariable("WAKE_CLAUDE_CLI");
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(env)) candidates.Add(env);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin\claude.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Length > 0))
            {
                try { candidates.Add(Path.Combine(dir.Trim(), "claude.exe")); } catch (ArgumentException) { }
            }
            var found = candidates.FirstOrDefault(System.IO.File.Exists);
            if (found == null) throw new WakeException(ExitCodes.App, "claude.exe niet gevonden (zet WAKE_CLAUDE_CLI op het volledige pad).");
            return found;
        }

        /// <summary>Eén argument quoten volgens de regels van CommandLineToArgvW.</summary>
        static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', backslashes); sb.Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
