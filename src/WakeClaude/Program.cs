using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WakeClaude
{
    enum Mode { Resume, Fresh, New }

    /// <summary>Wat te doen met een gestopte sessie die groter is dan --max-context.</summary>
    enum TooLarge { Skip, Replace, Revive }

    static class ExitCodes
    {
        public const int Ok = 0, Other = 1, Arguments = 2, App = 3, Ui = 4, Timeout = 5;
    }

    class WakeException : Exception
    {
        public readonly int ExitCode;
        public WakeException(int exitCode, string message) : base(message) { ExitCode = exitCode; }
    }

    class Options
    {
        public Mode Mode;
        public string Name;
        public string Folder = @"D:\";
        public string Prompt;
        public int TimeoutSeconds = 60;
        public long MaxContext = 200000;
        public TooLarge TooLarge = TooLarge.Skip;
        public int OnlyIfIdleMinutes;
        public int RetryAfterMinutes = 60;
        public bool DryRun;
        public bool Json;
    }

    /// <summary>Onthoudt mislukte pogingen, zodat een geplande taak niet blijft hameren.</summary>
    static class State
    {
        static readonly string File = System.IO.Path.Combine(AppPaths.Eigen, "state.json");

        public static string Path { get { return File; } }

        static IDictionary<string, object> Read()
        {
            try { return Json.Parse(System.IO.File.ReadAllText(File)) as IDictionary<string, object> ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        public static DateTime? LastFailure(string key)
        {
            object v;
            DateTime t;
            if (Read().TryGetValue(key, out v) && v != null &&
                DateTime.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return t;
            return null;
        }

        public static void Record(string key, bool failed)
        {
            try
            {
                var state = Read();
                if (failed) state[key] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                else state.Remove(key);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
                System.IO.File.WriteAllText(File, Json.Write(state));
            }
            catch { } // de toestand bijhouden mag het wekken nooit doen mislukken
        }
    }

    static class Log
    {
        static readonly string File = System.IO.Path.Combine(AppPaths.Eigen, "wake-claude.log");

        public static void Info(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message;
            Console.Error.WriteLine(line);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
                System.IO.File.AppendAllText(File, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { } // loggen mag het wekken nooit doen mislukken
        }
    }

    static class Program
    {
        const string Usage =
@"wake-claude.exe resume <naam> [opties]   actief: niets; gestopt: heractiveren; niet gevonden: nieuw
wake-claude.exe fresh  <naam> [opties]   actief: niets; anders nieuw, gestopte met die naam archiveren
wake-claude.exe new   [<naam>] [opties]  altijd nieuw

Opties:
  --folder <pad>          map voor een nieuwe sessie (standaard D:\)
  --prompt ""<tekst>""      eerste bericht voor de sessie
  --timeout <seconden>    hoe lang wachten tot de sessie bereikbaar is (standaard 60)
  --max-context <tokens>  resume: grens waarboven een gestopte sessie te duur is om te wekken (standaard 200000)
  --if-too-large <keuze>  wat dan te doen: skip (standaard), replace of revive
  --only-if-idle <min>    niets doen als er recenter invoer van muis of toetsenbord was
  --retry-after <min>     na een mislukking dezelfde opdracht zo lang met rust laten (standaard 60)
  --dry-run               tonen wat er zou gebeuren, zonder iets te doen
  --json                  resultaat als JSON

Exitcodes: 0 gelukt, 1 andere fout, 2 argumenten, 3 app start niet, 4 stap in de interface mislukt, 5 timeout";

        static int Main(string[] args)
        {
            // Als geplande taak is er geen console; dan mislukt het zetten van de codering.
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            AppWindow.SetProcessDPIAware();

            Options options;
            try { options = Parse(args); }
            catch (WakeException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
                return e.ExitCode;
            }
            if (options == null) { Console.WriteLine(Usage); return ExitCodes.Ok; }

            var result = new Dictionary<string, object>();
            result["modus"] = options.Mode.ToString().ToLowerInvariant();
            result["naam"] = options.Name;
            if (options.DryRun) result["dryRun"] = true;

            int exitCode;
            try
            {
                Log.Info("Start: " + string.Join(" ", args.Select(a => a.Contains(" ") ? "\"" + a + "\"" : a)));
                Run(options, result);
                exitCode = ExitCodes.Ok;
            }
            catch (WakeException e)
            {
                result["fout"] = e.Message;
                exitCode = e.ExitCode;
            }
            catch (Exception e)
            {
                result["fout"] = e.ToString();
                exitCode = ExitCodes.Other;
            }
            result["exitcode"] = exitCode;
            // Overslaan is geen poging: de wachttijd na een mislukking mag er niet door wegvallen.
            object actie;
            if (!options.DryRun && !(result.TryGetValue("actie", out actie) && (string)actie == "overgeslagen"))
                State.Record(Key(options), exitCode != ExitCodes.Ok);
            Log.Info("Resultaat: " + Json.Write(result));

            if (options.Json) Console.WriteLine(Json.Write(result));
            else Console.WriteLine(string.Join(Environment.NewLine, result.Where(kv => kv.Value != null).Select(kv => kv.Key + ": " + Format(kv.Value))));
            return exitCode;
        }

        static string Format(object value)
        {
            var list = value as IEnumerable<string>;
            return list != null ? string.Join(", ", list) : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static Options Parse(string[] args)
        {
            if (args.Length == 0 || args.Any(a => a == "-h" || a == "--help" || a == "/?")) return null;

            var o = new Options();
            switch (args[0].ToLowerInvariant())
            {
                case "resume": o.Mode = Mode.Resume; break;
                case "fresh": o.Mode = Mode.Fresh; break;
                case "new": o.Mode = Mode.New; break;
                default: throw new WakeException(ExitCodes.Arguments, "Onbekende modus: " + args[0]);
            }

            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                Func<string> value = () =>
                {
                    if (i + 1 >= args.Length) throw new WakeException(ExitCodes.Arguments, a + " verwacht een waarde.");
                    return args[++i];
                };
                switch (a)
                {
                    case "--folder": o.Folder = value(); break;
                    case "--prompt": o.Prompt = value(); break;
                    case "--timeout": o.TimeoutSeconds = PositiveInt(a, value()); break;
                    case "--max-context": o.MaxContext = PositiveInt(a, value()); break;
                    case "--only-if-idle": o.OnlyIfIdleMinutes = PositiveInt(a, value()); break;
                    case "--retry-after": o.RetryAfterMinutes = PositiveInt(a, value(), 0); break;   // 0 = geen wachttijd
                    case "--if-too-large":
                        var keuze = value();
                        switch (keuze.ToLowerInvariant())
                        {
                            case "skip": o.TooLarge = TooLarge.Skip; break;
                            case "replace": o.TooLarge = TooLarge.Replace; break;
                            case "revive": o.TooLarge = TooLarge.Revive; break;
                            default: throw new WakeException(ExitCodes.Arguments, "--if-too-large verwacht skip, replace of revive.");
                        }
                        break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--json": o.Json = true; break;
                    default:
                        if (a.StartsWith("--")) throw new WakeException(ExitCodes.Arguments, "Onbekende optie: " + a);
                        if (o.Name != null) throw new WakeException(ExitCodes.Arguments, "Meer dan één naam opgegeven: " + a);
                        o.Name = a.Trim();
                        break;
                }
            }

            if (o.Name == "") o.Name = null;
            if (o.Name == null && o.Mode != Mode.New)
                throw new WakeException(ExitCodes.Arguments, "Een naam is verplicht bij " + args[0] + ".");
            if (o.Name != null && (o.Name.Length > 200 || o.Name.Contains("\"") || o.Name.Contains("\n")))
                throw new WakeException(ExitCodes.Arguments, "Ongeldige naam: maximaal 200 tekens, zonder aanhalingstekens of regeleinden.");
            if (!Directory.Exists(o.Folder))
                throw new WakeException(ExitCodes.Arguments, "Map bestaat niet: " + o.Folder);
            return o;
        }

        static int PositiveInt(string option, string text, int minimum = 1)
        {
            int n;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < minimum)
                throw new WakeException(ExitCodes.Arguments, option + " verwacht een getal vanaf " + minimum + ".");
            return n;
        }

        static void Run(Options o, Dictionary<string, object> result)
        {
            if (o.Mode == Mode.New)
            {
                if (Skip(o, result)) return;
                CreateNew(o, new List<string>(), "modus new", result);
                return;
            }

            var running = Agents.Running();
            var rcLog = AppLog.Load();
            var matches = Register.Load()
                .Where(s => !s.IsArchived && string.Equals((s.Title ?? "").Trim(), o.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => Math.Max(s.LastActivityAt, s.CreatedAt))
                .ToList();

            Func<Session, bool> isActive = s =>
            {
                long startedAt;
                return s.CliSessionId != null && running.TryGetValue(s.CliSessionId, out startedAt) && rcLog.ActiveSince(s.LocalId, startedAt);
            };

            var active = matches.FirstOrDefault(isActive);
            if (active != null)
            {
                result["actie"] = "niets";
                result["sessie"] = active.LocalId;
                result["reden"] = "actieve, bereikbare sessie met deze naam gevonden";
                return;
            }

            if (o.Mode == Mode.Resume && matches.Count > 0)
            {
                var target = matches[0];
                var context = Transcript.ContextTokens(target.CliSessionId);
                result["contextTokens"] = context;
                var teGroot = "gestopte sessie is te groot om te heractiveren (" + context + " > " + o.MaxContext + " tokens)";

                if (context > o.MaxContext && o.TooLarge == TooLarge.Skip)
                {
                    result["actie"] = "overgeslagen";
                    result["sessie"] = target.LocalId;
                    result["reden"] = teGroot + "; --if-too-large replace vervangt ze, revive wekt ze toch";
                    return;
                }
                if (Skip(o, result)) return;
                if (context <= o.MaxContext || o.TooLarge == TooLarge.Revive)
                {
                    Revive(o, target, result);
                    return;
                }
                CreateNew(o, matches.Select(s => s.LocalId).ToList(), teGroot, result);
                return;
            }

            if (Skip(o, result)) return;
            CreateNew(o, matches.Select(s => s.LocalId).ToList(),
                matches.Count == 0 ? "geen sessie met deze naam gevonden" : "gestopte sessie met deze naam wordt vervangen", result);
        }

        /// <summary>
        /// Redenen om niets te doen, ook al is er werk: iemand zit aan de computer, of een eerdere
        /// poging mislukte kort geleden. Beide zouden een geplande taak anders om de tien minuten
        /// opnieuw laten klikken en typen.
        /// </summary>
        static bool Skip(Options o, Dictionary<string, object> result)
        {
            if (o.DryRun) return false;

            if (o.OnlyIfIdleMinutes > 0)
            {
                var idle = AppWindow.UserIdle();
                if (idle < TimeSpan.FromMinutes(o.OnlyIfIdleMinutes))
                {
                    result["actie"] = "overgeslagen";
                    result["reden"] = "iemand gebruikt de computer (" + (int)idle.TotalSeconds + " s geleden invoer)";
                    return true;
                }
            }

            var failed = State.LastFailure(Key(o));
            Log.Info("toestand: " + State.Path + " => " + (failed.HasValue ? failed.Value.ToString("HH:mm:ss") : "geen mislukking"));
            if (failed.HasValue)
            {
                var ago = DateTime.Now - failed.Value;
                if (ago < TimeSpan.FromMinutes(o.RetryAfterMinutes))
                {
                    result["actie"] = "overgeslagen";
                    result["reden"] = "vorige poging mislukte " + (int)ago.TotalMinutes + " min geleden; opnieuw na " + o.RetryAfterMinutes + " min";
                    return true;
                }
            }
            return false;
        }

        static string Key(Options o) { return o.Mode.ToString().ToLowerInvariant() + "|" + (o.Name ?? ""); }

        // --- nieuwe sessie ---

        static void CreateNew(Options o, List<string> archive, string reason, Dictionary<string, object> result)
        {
            result["actie"] = "nieuw";
            result["reden"] = reason;
            if (archive.Count > 0) result["gearchiveerd"] = archive;

            var prompt = BuildPrompt(o.Name, archive, o.Prompt);
            if (o.DryRun) { result["prompt"] = prompt; return; }

            var handle = AppWindow.EnsureRunning(o.TimeoutSeconds);
            var before = new HashSet<string>(Register.Load().Select(s => s.LocalId));
            var t0 = Time.NowMs();

            AppWindow.OpenLink("claude://code/new?folder=" + Uri.EscapeDataString(o.Folder) +
                               "&q=" + Uri.EscapeDataString(prompt) + "&source=desktop_action");
            // Het nieuwe-sessiescherm zet in het log de focus op "geen sessie". Zonder dat scherm
            // zou Enter een concept in een bestaande sessie kunnen versturen.
            if (!WaitUntil(15, () => AppLog.Load().CurrentFocus() == null))
                throw new WakeException(ExitCodes.Ui, "De app toonde het scherm voor een nieuwe sessie niet.");
            Thread.Sleep(2000);

            // De link vult de prompt enkel in; versturen gebeurt vanuit het invoerveld. Een Enter
            // te vroeg na het laden doet niets, dus opnieuw zolang er geen sessie is en het scherm er nog staat.
            AppWindow.ClickComposer(handle);
            Session created = null;
            Func<Session> findCreated = () => Register.Load()
                .Where(s => !before.Contains(s.LocalId) && s.CreatedAt >= t0 - 5000)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            for (int attempt = 1; attempt <= 4 && created == null; attempt++)
            {
                if (AppLog.Load().CurrentFocus() != null) break;
                Thread.Sleep(1000);
                Log.Info("Enter, poging " + attempt);
                AppWindow.PressEnter(handle);
                WaitUntil(6, () => (created = findCreated()) != null);
            }
            if (created == null && !WaitUntil(10, () => (created = findCreated()) != null))
                throw new WakeException(ExitCodes.Ui, "Na Enter verscheen geen nieuwe sessie. Stond er een pop-up over het invoerveld?");

            result["sessie"] = created.LocalId;
            Log.Info("Nieuwe sessie: " + created.LocalId);

            string waiting = null;
            var ok = WaitUntil(o.TimeoutSeconds, () =>
            {
                var sessions = Register.Load();
                var self = sessions.FirstOrDefault(s => s.LocalId == created.LocalId);
                if (self == null) { waiting = "de sessie staat niet meer in het register"; return false; }
                if (o.Name != null && !string.Equals((self.Title ?? "").Trim(), o.Name, StringComparison.OrdinalIgnoreCase))
                { waiting = "naam is nog \"" + self.Title + "\""; return false; }
                var notArchived = archive.Where(id => sessions.Any(s => s.LocalId == id && !s.IsArchived)).ToList();
                if (notArchived.Count > 0) { waiting = "nog niet gearchiveerd: " + string.Join(", ", notArchived); return false; }
                if (!AppLog.Load().ActiveSince(created.LocalId, t0)) { waiting = "Remote Control nog niet verbonden"; return false; }
                return true;
            });
            if (!ok) throw new WakeException(ExitCodes.Timeout, "Sessie " + created.LocalId + " is niet klaar binnen " + o.TimeoutSeconds + " s: " + waiting + ".");
        }

        static string BuildPrompt(string name, List<string> archive, string prompt)
        {
            var steps = new List<string>();
            if (name != null)
                steps.Add("Hernoem deze sessie naar \"" + name + "\" met set_session_title (session_id \"self\").");
            if (archive.Count > 0)
                steps.Add("Archiveer met archive_session deze vervangen sessies: " + string.Join(", ", archive) + ".");

            var task = string.IsNullOrWhiteSpace(prompt) ? "Antwoord daarna enkel met: ok" : "Daarna de opdracht:\n" + prompt.Trim();
            if (steps.Count == 0) return string.IsNullOrWhiteSpace(prompt) ? "Antwoord enkel met: ok" : prompt.Trim();

            var sb = new StringBuilder("[wake-claude] Doe eerst dit, zonder uitleg:\n");
            for (int i = 0; i < steps.Count; i++) sb.Append(i + 1).Append(". ").Append(steps[i]).Append('\n');
            sb.Append(task);
            return sb.ToString();
        }

        // --- bestaande sessie heractiveren ---

        static void Revive(Options o, Session target, Dictionary<string, object> result)
        {
            result["actie"] = "heractiveerd";
            result["sessie"] = target.LocalId;
            result["reden"] = "gestopte sessie met deze naam gevonden";

            var message = string.IsNullOrWhiteSpace(o.Prompt) ? "Antwoord enkel met: ok" : o.Prompt.Trim();
            if (o.DryRun) { result["prompt"] = message; return; }

            var handle = AppWindow.EnsureRunning(o.TimeoutSeconds);

            // De continue-link start het proces enkel als de app van een andere sessie komt.
            var other = Register.Load()
                .Where(s => s.LocalId != target.LocalId && !s.IsArchived)
                .OrderByDescending(s => s.LastFocusedAt)
                .FirstOrDefault();
            if (other == null) throw new WakeException(ExitCodes.Ui, "Geen andere sessie gevonden om eerst naartoe te navigeren.");
            AppWindow.OpenLink("claude://claude.ai/epitaxy/" + other.LocalId);
            Thread.Sleep(2500);

            var t0 = Time.NowMs();
            AppWindow.OpenLink("claude://code/continue?session=" + Uri.EscapeDataString(target.LocalId) + "&source=desktop_action");

            if (!WaitUntil(15, () => AppLog.Load().CurrentFocus() == target.LocalId))
                throw new WakeException(ExitCodes.Ui, "De app opende de sessie niet.");
            Thread.Sleep(2000);

            // Nooit typen in een andere sessie: de doelsessie moet nog steeds de laatst geopende zijn.
            // Het proces van een door de app gepauzeerde sessie start pas met het bericht zelf.
            if (AppLog.Load().CurrentFocus() != target.LocalId)
                throw new WakeException(ExitCodes.Ui, "Een andere sessie kwam in beeld; er is niets getypt.");

            AppWindow.ClickComposer(handle);
            AppWindow.TypeText(handle, message);
            AppWindow.PressEnter(handle);

            if (!WaitUntil(o.TimeoutSeconds, () => AppLog.Load().ActiveSince(target.LocalId, t0)))
                throw new WakeException(ExitCodes.Timeout, "Remote Control verbond niet binnen " + o.TimeoutSeconds + " s.");
        }

        static bool WaitUntil(int seconds, Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                if (condition()) return true;
                if (DateTime.UtcNow >= deadline) return false;
                Thread.Sleep(2000);
            }
        }
    }
}
