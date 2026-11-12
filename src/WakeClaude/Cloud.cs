using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace WakeClaude
{
    /// <summary>
    /// Het OAuth-token dat Claude Code zelf gebruikt, uit ~\.claude\.credentials.json. wake-claude
    /// leest het alleen; vernieuwen laat het aan de CLI over. Is het verlopen, dan geeft dit null en
    /// slaat de aanroeper zijn ronde over: beter niets doen dan sessies stoppen op verouderde info.
    /// </summary>
    static class Credentials
    {
        public static string AccessToken(out string reason)
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
            IDictionary<string, object> root;
            try
            {
                string text;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                    text = reader.ReadToEnd();
                root = Json.Parse(text) as IDictionary<string, object>;
            }
            catch (IOException) { reason = "kon " + file + " niet lezen"; return null; }
            catch (UnauthorizedAccessException) { reason = "kon " + file + " niet lezen"; return null; }
            catch (ArgumentException) { reason = "kon " + file + " niet lezen"; return null; }

            object oauth;
            if (root == null || !root.TryGetValue("claudeAiOauth", out oauth) || !(oauth is IDictionary<string, object>))
            { reason = "geen claudeAiOauth in " + file; return null; }

            var o = (IDictionary<string, object>)oauth;
            var token = Json.Str(o, "accessToken");
            if (string.IsNullOrEmpty(token)) { reason = "geen accessToken in " + file; return null; }

            var expires = Json.Long(o, "expiresAt");
            if (expires > 0 && expires <= Time.NowMs())
            {
                reason = "het token is verlopen sinds " + Time.Local(expires).ToString("HH:mm:ss") +
                         "; start of gebruik een Claude-sessie, dan vernieuwt de CLI het zelf";
                return null;
            }
            reason = null;
            return token;
        }
    }

    /// <summary>Een sessie zoals de accountbrede API ze kent (claude.ai, gsm, elk toestel).</summary>
    class CloudSession
    {
        public string Id;
        public string Title;
        public string Status;
    }

    /// <summary>
    /// De sessie-API van Claude Code: /v1/code/sessions. Die kent elke sessie van het account, van
    /// welk toestel ook, en - anders dan de lokale bestanden - ook of ze gearchiveerd is. Archiveren
    /// gebeurt immers aan de serverkant: op schijf is er op deze machine niets van te zien.
    /// Let op: dit is geen gedocumenteerde API. Breekt ze, dan stopt term-prune met een fout en laat
    /// het alle sessies met rust; dat is de veilige kant.
    /// </summary>
    static class CodeSessions
    {
        static string BaseUrl
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("WAKE_CLAUDE_API");
                return string.IsNullOrWhiteSpace(env) ? "https://api.anthropic.com" : env.TrimEnd('/');
            }
        }

        /// <summary>Alle gearchiveerde sessies van het account, op id.</summary>
        public static Dictionary<string, CloudSession> Archived(string token)
        {
            var result = new Dictionary<string, CloudSession>(StringComparer.Ordinal);
            var url = BaseUrl + "/v1/code/sessions?limit=100&statuses=archived";
            for (int page = 0; page < 50 && url != null; page++)
            {
                var body = Get(token, url);
                var root = Json.Parse(body) as IDictionary<string, object>;
                object data;
                if (root == null || !root.TryGetValue("data", out data) || !(data is object[]))
                    throw new WakeException(ExitCodes.Other, "De sessie-API gaf geen lijst terug (" + url + ").");

                foreach (var item in ((object[])data).OfType<IDictionary<string, object>>())
                {
                    var id = Json.Str(item, "id");
                    if (id == null) continue;
                    result[id] = new CloudSession { Id = id, Title = Json.Str(item, "title"), Status = Json.Str(item, "status") };
                }

                var cursor = Json.Str(root, "next_cursor");
                url = string.IsNullOrEmpty(cursor)
                    ? null
                    : BaseUrl + "/v1/code/sessions?limit=100&statuses=archived&cursor=" + Uri.EscapeDataString(cursor);
            }
            return result;
        }

        /// <summary>Een sessie archiveren, net zoals wegklikken uit de lijst op gelijk welk toestel.</summary>
        public static void Archive(string token, string id)
        {
            var status = Post(token, BaseUrl + "/v1/code/sessions/" + Uri.EscapeDataString(id) + "/archive");
            // 409: ze was al gearchiveerd. Dat is geen fout, dat is de gewenste eindtoestand.
            if (status != HttpStatusCode.OK && status != HttpStatusCode.Conflict)
                throw new WakeException(ExitCodes.Other, "Archiveren van " + id + " mislukte (HTTP " + (int)status + ").");
        }

        /// <summary>Het omgekeerde: ze komt terug in de lijst en term-prune laat ze weer met rust.</summary>
        public static void Unarchive(string token, string id)
        {
            var status = Post(token, BaseUrl + "/v1/code/sessions/" + Uri.EscapeDataString(id) + "/unarchive");
            if (status != HttpStatusCode.OK && status != HttpStatusCode.Conflict)
                throw new WakeException(ExitCodes.Other, "Terugzetten van " + id + " mislukte (HTTP " + (int)status + ").");
        }

        // --- HTTP ---

        static HttpWebRequest Build(string token, string url, string method)
        {
            // .NET Framework kiest anders nog oude TLS-versies; api.anthropic.com wil minstens 1.2.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (NotSupportedException) { }
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.UserAgent = "wake-claude";
            request.Accept = "application/json";
            request.Headers["Authorization"] = "Bearer " + token;
            request.Headers["anthropic-version"] = "2023-06-01";
            return request;
        }

        static string Get(string token, string url)
        {
            try
            {
                using (var response = (HttpWebResponse)Build(token, url, "GET").GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException e) { throw new WakeException(ExitCodes.Other, "Sessie-API onbereikbaar: " + Describe(e)); }
        }

        static HttpStatusCode Post(string token, string url)
        {
            var request = Build(token, url, "POST");
            request.ContentType = "application/json";
            request.ContentLength = 2;
            try
            {
                using (var stream = request.GetRequestStream()) stream.Write(new byte[] { (byte)'{', (byte)'}' }, 0, 2);
                using (var response = (HttpWebResponse)request.GetResponse()) return response.StatusCode;
            }
            catch (WebException e)
            {
                var http = e.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.Conflict) return HttpStatusCode.Conflict;
                throw new WakeException(ExitCodes.Other, "Sessie-API onbereikbaar: " + Describe(e));
            }
        }

        static string Describe(WebException e)
        {
            var http = e.Response as HttpWebResponse;
            if (http == null) return e.Message;
            string body = "";
            try
            {
                using (var reader = new StreamReader(http.GetResponseStream(), Encoding.UTF8))
                    body = reader.ReadToEnd();
            }
            catch (IOException) { }
            if (body.Length > 300) body = body.Substring(0, 300) + "…";
            return "HTTP " + (int)http.StatusCode + " " + body;
        }
    }

    static class Moment
    {
        public static string Now() { return DateTime.Now.ToString("s", CultureInfo.InvariantCulture); }

        /// <summary>Of dit tijdstip al voorbij is; een lege of onleesbare waarde telt als voorbij.</summary>
        public static bool Passed(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            DateTime t;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return true;
            return DateTime.Now >= t;
        }
    }
}
