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
        /// <summary>De pid van de laatste start: om haar venster te kunnen tonen of sluiten.</summary>
        public int Pid;
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
                    Pid = (int)Json.Long(d, "pid"),
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
                { "name", s.Name }, { "folder", s.Folder }, { "id", s.Id }, { "created", s.Created }, { "pid", (long)s.Pid },
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
            Dictionary<string, AgentInfo> running;
            var sessions = Sync(result, out running, save: !o.DryRun);
            var known = Find(sessions, o.Name);

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
                Launch(known, resume: true, prompt: null, visible: o.Visible);
                Save(sessions);
                Ready(known, o, result);
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
                   visible: o.Visible, addDir: bijlagen != null ? bijlagen.Dir : null);
            Save(sessions);   // nu met de pid erbij, zodat haar venster terug te vinden is
            Ready(session, o, result);
        }

        /// <summary>term-restore: elke geregistreerde sessie die niet draait, terug aanhaken.</summary>
        public static void Restore(Options o, Dictionary<string, object> result)
        {
            Dictionary<string, AgentInfo> running;
            var sessions = Sync(result, out running, save: !o.DryRun);
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
                    Launch(s, resume: true, prompt: null, visible: o.Visible);
                    started.Add(s.Name);
                    Thread.Sleep(1500); // niet alle vensters tegelijk: elk proces verbindt met Remote Control
                }
                catch (Exception e) { failed.Add(s.Name + " (" + e.Message + ")"); }
            }

            if (!o.DryRun && started.Count > 0)
            {
                Save(sessions);   // de pids van deze start bewaren
                var deadline = DateTime.UtcNow.AddSeconds(o.TimeoutSeconds);
                var waitFor = sessions.Where(s => started.Contains(s.Name)).ToList();
                while (DateTime.UtcNow < deadline)
                {
                    running = Agents.Running();
                    if (waitFor.All(s => running.ContainsKey(s.Id))) break;
                    Thread.Sleep(2000);
                }
                // Een sessie die zich niet meldt, wacht meestal op een vraag. Verborgen zou je die
                // nooit zien, dus komt net dat venster in beeld.
                foreach (var s in waitFor.Where(s => !running.ContainsKey(s.Id)))
                {
                    started.Remove(s.Name);
                    var shown = s.Pid > 0 && ConsoleWindow.Show(s.Pid);
                    failed.Add(s.Name + " (meldt zich niet na " + o.TimeoutSeconds + " s; " +
                               (shown ? "het venster staat nu zichtbaar, wacht het op een vraag?" : "geen venster gevonden") + ")");
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
            Dictionary<string, AgentInfo> running;
            // term-list neemt geen lock, dus hier enkel tonen; de volgende schrijvende actie bewaart het.
            var sessions = Sync(result, out running, save: false);
            result["register"] = RegistryPath;
            result["sessies"] = sessions.Select(s =>
            {
                AgentInfo agent;
                var draait = running.TryGetValue(s.Id, out agent);
                var venster = "-";
                if (draait)
                {
                    var zichtbaar = ConsoleWindow.Visible(agent.Pid);
                    venster = zichtbaar == null ? "geen venster" : zichtbaar.Value ? "zichtbaar" : "verborgen";
                }
                return s.Name + " | " + (draait ? "draait" : "gestopt") + " | " + venster + " | " + s.Folder + " | " + s.Id;
            }).ToList();
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

        /// <summary>term-show / term-hide: het consolevenster van een draaiende sessie tonen of verbergen.</summary>
        public static void Visibility(Options o, Dictionary<string, object> result, bool show)
        {
            List<TerminalSession> sessions;
            var pid = Lookup(o, result, out sessions);
            if (pid == 0) return;
            result["actie"] = show ? "getoond" : "verborgen";
            if (o.DryRun) return;
            if (show ? ConsoleWindow.Show(pid) : ConsoleWindow.Hide(pid)) return;
            result["actie"] = "niets";
            result["reden"] = "geen consolevenster gevonden voor pid " + pid +
                "; draait de sessie in Windows Terminal, dan heeft ze geen eigen venster";
        }

        /// <summary>term-stop: de sessie sluiten. Ze blijft geregistreerd, dus term-restore haalt ze terug.</summary>
        public static void Stop(Options o, Dictionary<string, object> result)
        {
            List<TerminalSession> sessions;
            var pid = Lookup(o, result, out sessions);
            if (pid == 0) return;
            result["actie"] = "gestopt";
            result["reden"] = "blijft geregistreerd; term-restore haakt ze terug aan, term-forget haalt ze eruit";
            if (o.DryRun) return;
            result["hoe"] = ConsoleWindow.Stop(pid, o.TimeoutSeconds);
            var known = Find(sessions, o.Name);
            if (known != null) { known.Pid = 0; Save(sessions); }
        }

        /// <summary>De geregistreerde sessie opzoeken en haar pid teruggeven; 0 als ze niet draait.</summary>
        static int Lookup(Options o, Dictionary<string, object> result, out List<TerminalSession> sessions)
        {
            Dictionary<string, AgentInfo> running;
            sessions = Sync(result, out running, save: !o.DryRun);
            var known = Find(sessions, o.Name);
            if (known == null)
                throw new WakeException(ExitCodes.Arguments, "Geen terminalsessie geregistreerd met de naam " + o.Name + ".");
            result["sessie"] = known.Id;
            var pid = PidOf(known, running);
            if (pid == 0)
            {
                result["actie"] = "niets";
                result["reden"] = "deze terminalsessie draait niet";
                return 0;
            }
            result["pid"] = (long)pid;
            return pid;
        }

        /// <summary>
        /// De pid van een draaiende sessie: bij voorkeur die uit `claude agents --json`. Meldt de
        /// sessie zich daar niet, bijvoorbeeld omdat ze op een vraag wacht, dan die uit het register,
        /// maar enkel als het nog een draaiende claude.exe is: Windows hergebruikt pids.
        /// </summary>
        static int PidOf(TerminalSession s, Dictionary<string, AgentInfo> running)
        {
            AgentInfo agent;
            if (running.TryGetValue(s.Id, out agent) && agent.Pid > 0) return agent.Pid;
            return ConsoleWindow.IsClaude(s.Pid) ? s.Pid : 0;
        }

        /// <summary>Het register laden, kijken wat er draait, en verschoven sessie-IDs bijwerken.</summary>
        static List<TerminalSession> Sync(Dictionary<string, object> result, out Dictionary<string, AgentInfo> running, bool save)
        {
            var sessions = Load();
            running = Agents.Running();
            var healed = Heal(sessions, running);
            if (healed.Count == 0) return sessions;
            result["bijgewerkt"] = healed;
            if (save) Save(sessions);
            return sessions;
        }

        /// <summary>
        /// Een geregistreerde sessie kan onder een nieuw sessie-ID verder draaien: /clear geeft het
        /// proces een nieuw ID, terwijl het venster en de Remote Control-naam dezelfde blijven. Het
        /// register wees dan naar een ID dat niemand draait, zag de sessie als gestopt en zou er bij
        /// de volgende term-restore een tweede naast starten. Daarom: staat het ID van een naam niet
        /// bij de draaiende agents, maar draait er wel een claude.exe met `--remote-control "<naam>"`,
        /// dan neemt het register dat ID over.
        /// </summary>
        static List<string> Heal(List<TerminalSession> sessions, Dictionary<string, AgentInfo> running)
        {
            var changed = new List<string>();
            var missing = sessions.Where(s => !running.ContainsKey(s.Id)).ToList();
            if (missing.Count == 0) return changed;

            var names = Processes.RemoteControlNames();
            if (names.Count == 0) return changed;
            var byPid = new Dictionary<int, string>();
            foreach (var kv in running) byPid[kv.Value.Pid] = kv.Key;

            foreach (var s in missing)
            {
                foreach (var kv in names)
                {
                    if (!string.Equals(kv.Value, s.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    string id;
                    if (!byPid.TryGetValue(kv.Key, out id)) continue;        // proces meldt zich niet als agent
                    if (sessions.Any(other => other.Id == id)) continue;     // dit ID hoort al bij een andere naam
                    changed.Add(s.Name + ": " + s.Id + " wordt " + id);
                    s.Id = id;
                    s.Pid = kv.Key;
                    break;
                }
            }
            return changed;
        }

        // --- starten ---

        static void Launch(TerminalSession s, bool resume, string prompt, bool visible, string addDir = null)
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
            // Met de shell starten geeft een console-programma een eigen venster. Verborgen is dat
            // venster er wel, met een echte console erachter, maar zie je het niet en staat het niet
            // in de taakbalk: de interface rendert gewoon door. term-show haalt het terug.
            var process = Process.Start(new ProcessStartInfo(exe, commandLine)
            {
                UseShellExecute = true,
                WorkingDirectory = s.Folder,
                WindowStyle = visible ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
            });
            s.Pid = process != null ? process.Id : 0;
        }

        /// <summary>
        /// Wachten tot de sessie zich meldt bij Remote Control. Lukt dat niet, dan wacht het venster
        /// waarschijnlijk op een vraag; verborgen zou je die nooit zien, dus komt het dan in beeld.
        /// </summary>
        static void Ready(TerminalSession s, Options o, Dictionary<string, object> result)
        {
            var deadline = DateTime.UtcNow.AddSeconds(o.TimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(2000);
                if (Agents.Running().ContainsKey(s.Id))
                {
                    result["venster"] = o.Visible ? "zichtbaar" : "verborgen";
                    return;
                }
            }
            var shown = s.Pid > 0 && ConsoleWindow.Show(s.Pid);
            if (shown) result["venster"] = "zichtbaar gemaakt";
            throw new WakeException(ExitCodes.Timeout, "De terminalsessie " + s.Id + " meldt zich niet na " + o.TimeoutSeconds + " s. " +
                (shown ? "Haar venster staat nu zichtbaar: het wacht waarschijnlijk op een vraag (bv. \"Do you trust this folder?\"). Beantwoord die en draai term-restore."
                       : "Er is geen venster gevonden; kijk in Taakbeheer of claude.exe nog draait."));
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
