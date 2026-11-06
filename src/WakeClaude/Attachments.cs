using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WakeClaude
{
    /// <summary>Eén bijlage in wording: ofwel een bestand op schijf, ofwel base64 uit een transcript.</summary>
    class Bron
    {
        public string Naam;
        public string Pad;
        public string Base64;
    }

    /// <summary>De klaargezette bijlagen en de map waarin ze staan.</summary>
    class AttachmentSet
    {
        public string Dir;
        public List<string> Files = new List<string>();
    }

    /// <summary>
    /// Bijlagen voor een nieuwe terminalsessie. De CLI neemt geen afbeeldingen aan op de
    /// commandoregel, maar een sessie leest wel bestanden van schijf: daarom komen de paden in de
    /// prompt en leest de sessie ze met de Read tool.
    ///
    /// Alles wordt hier meteen weggeschreven, vóór het starten. Dat is het punt: de sessie die
    /// wake-claude aanroept mag daarna /clear doen of haar scratchpad kwijtspelen; de bijlagen staan
    /// dan al in een eigen map onder ~/.wake-claude en hangen nergens meer van af.
    /// </summary>
    static class Attachments
    {
        static readonly string Root = Path.Combine(AppPaths.Eigen, "attachments");
        const int Max = 20;
        const int BewaarDagen = 30;

        /// <summary>Zet de gevraagde bijlagen klaar, of geeft null als er geen gevraagd zijn.</summary>
        public static AttachmentSet Collect(Options o)
        {
            if (o.Images.Count == 0 && !o.PasteImages) return null;

            var bronnen = new List<Bron>();
            foreach (var pad in o.Images)
            {
                string vol;
                try { vol = Path.GetFullPath(pad); }
                catch (ArgumentException) { throw new WakeException(ExitCodes.Arguments, "Ongeldig pad bij --image: " + pad); }
                if (!File.Exists(vol)) throw new WakeException(ExitCodes.Arguments, "Bijlage bestaat niet: " + vol);
                bronnen.Add(new Bron { Naam = Path.GetFileName(vol), Pad = vol });
            }

            if (o.PasteImages)
            {
                var geplakt = UitTranscript(o.SessionId);
                for (int i = 0; i < geplakt.Count; i++)
                    bronnen.Add(new Bron { Naam = "geplakt-" + (i + 1) + Extensie(geplakt[i].Key), Base64 = geplakt[i].Value });
            }

            if (bronnen.Count > Max)
                throw new WakeException(ExitCodes.Arguments, "Te veel bijlagen (" + bronnen.Count + "); maximaal " + Max + ".");

            var set = new AttachmentSet { Dir = NieuweMap(o.DryRun) };
            for (int i = 0; i < bronnen.Count; i++)
            {
                var doel = Path.Combine(set.Dir, (i + 1) + "-" + Veilig(bronnen[i].Naam));
                if (!o.DryRun)
                {
                    if (bronnen[i].Pad != null) File.Copy(bronnen[i].Pad, doel, true);
                    else File.WriteAllBytes(doel, Convert.FromBase64String(bronnen[i].Base64));
                }
                set.Files.Add(doel);
            }
            if (!o.DryRun) Opruimen();
            return set;
        }

        /// <summary>De paden onderaan de prompt zetten; zonder bijlagen blijft de prompt zoals hij was.</summary>
        public static string Decorate(string prompt, AttachmentSet set)
        {
            if (set == null || set.Files.Count == 0) return prompt;
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(prompt)) sb.Append(prompt.Trim()).Append("\n\n");
            sb.Append(set.Files.Count == 1 ? "Bijlage bij deze opdracht" : "Bijlagen bij deze opdracht");
            sb.Append(" (lees ze met de Read tool):\n");
            foreach (var f in set.Files) sb.Append(f).Append('\n');
            return sb.ToString().TrimEnd();
        }

        // --- geplakte afbeeldingen uit het transcript van de aanroepende sessie ---

        /// <summary>De afbeeldingen van het laatste bericht dat er bevat, als (media_type, base64).</summary>
        static List<KeyValuePair<string, string>> UitTranscript(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new WakeException(ExitCodes.Arguments, "--paste-images werkt enkel vanuit een Claude-sessie: CLAUDE_CODE_SESSION_ID ontbreekt. Geef de bestanden anders met --image mee.");
            var transcript = Transcript(sessionId);
            if (transcript == null)
                throw new WakeException(ExitCodes.Arguments, "Geen transcript gevonden voor sessie " + sessionId + ".");

            var laatste = new List<KeyValuePair<string, string>>();
            foreach (var regel in File.ReadLines(transcript, Encoding.UTF8))
            {
                // Enkel regels met een afbeelding ontleden: een transcript kan tientallen MB groot zijn.
                if (regel.IndexOf("\"type\":\"image\"", StringComparison.Ordinal) < 0) continue;
                IDictionary<string, object> rij;
                try { rij = Json.Parse(regel) as IDictionary<string, object>; }
                catch (Exception) { continue; }
                if (rij == null || Json.Str(rij, "type") != "user" || Json.Bool(rij, "isSidechain")) continue;
                var bericht = Veld(rij, "message") as IDictionary<string, object>;
                if (bericht == null || Json.Str(bericht, "role") != "user") continue;
                var inhoud = Veld(bericht, "content") as object[];
                if (inhoud == null) continue;

                var reeks = new List<KeyValuePair<string, string>>();
                foreach (var blok in inhoud.OfType<IDictionary<string, object>>())
                {
                    if (Json.Str(blok, "type") != "image") continue;
                    var bron = Veld(blok, "source") as IDictionary<string, object>;
                    if (bron == null || Json.Str(bron, "type") != "base64") continue;
                    var data = Json.Str(bron, "data");
                    if (string.IsNullOrEmpty(data)) continue;
                    reeks.Add(new KeyValuePair<string, string>(Json.Str(bron, "media_type"), data));
                }
                if (reeks.Count > 0) laatste = reeks;   // telkens de nieuwste reeks onthouden
            }

            if (laatste.Count == 0)
                throw new WakeException(ExitCodes.Arguments, "Geen geplakte afbeeldingen gevonden in het transcript van deze sessie.");
            return laatste;
        }

        static string Transcript(string sessionId)
        {
            var projects = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".claude\projects");
            try
            {
                return Directory.Exists(projects)
                    ? Directory.EnumerateFiles(projects, sessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault()
                    : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static object Veld(IDictionary<string, object> o, string sleutel)
        {
            object v;
            return o.TryGetValue(sleutel, out v) ? v : null;
        }

        static string Extensie(string mediaType)
        {
            switch ((mediaType ?? "").ToLowerInvariant())
            {
                case "image/jpeg": return ".jpg";
                case "image/gif": return ".gif";
                case "image/webp": return ".webp";
                default: return ".png";
            }
        }

        // --- map en opkuis ---

        static string NieuweMap(bool dryRun)
        {
            var stam = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            var map = stam;
            for (int n = 2; Directory.Exists(map); n++) map = stam + "-" + n;
            if (!dryRun) Directory.CreateDirectory(map);
            return map;
        }

        static string Veilig(string naam)
        {
            var ongeldig = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in naam) sb.Append(Array.IndexOf(ongeldig, c) >= 0 ? '_' : c);
            var s = sb.ToString().Trim();
            if (s.Length == 0) return "bijlage";
            return s.Length > 80 ? s.Substring(s.Length - 80) : s;
        }

        /// <summary>Oude bijlagemappen weggooien; dit mag het starten nooit doen mislukken.</summary>
        static void Opruimen()
        {
            try
            {
                if (!Directory.Exists(Root)) return;
                var grens = DateTime.UtcNow.AddDays(-BewaarDagen);
                foreach (var map in Directory.EnumerateDirectories(Root).ToList())
                {
                    try { if (Directory.GetLastWriteTimeUtc(map) < grens) Directory.Delete(map, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { }
        }
    }
}
