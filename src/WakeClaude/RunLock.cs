using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace WakeClaude
{
    /// <summary>
    /// Eén wake-claude die iets doet tegelijk. Het register en de toestand worden gelezen, aangepast
    /// en teruggeschreven, en de app-modi klikken en typen in één venster: twee runs tegelijk
    /// overschrijven elkaars registratie of typen in elkaars invoerveld. De lock blijft ook tijdens
    /// het wachten liggen, want anders zou een tweede run met dezelfde naam de sessie nog niet zien
    /// draaien en een tweede venster openen.
    ///
    /// Lezen hoeft niet te wachten: term-list en --dry-run nemen geen lock, en het register wordt
    /// altijd via een tijdelijk bestand vervangen, dus een lezer ziet de oude of de nieuwe inhoud.
    /// </summary>
    sealed class RunLock : IDisposable
    {
        static readonly string Bestand = Path.Combine(AppPaths.Eigen, "lock");
        static readonly string Eigenaar = Path.Combine(AppPaths.Eigen, "lock.owner");

        FileStream stream;

        RunLock(FileStream stream) { this.stream = stream; }

        /// <summary>Wacht tot de lock vrij is en neemt ze; faalt met exitcode 6 als dat te lang duurt.</summary>
        public static RunLock Acquire(Options o, Dictionary<string, object> result)
        {
            Directory.CreateDirectory(AppPaths.Eigen);
            var start = DateTime.UtcNow;
            var deadline = start.AddSeconds(o.LockTimeoutSeconds);
            bool gemeld = false;

            while (true)
            {
                try
                {
                    var stream = new FileStream(Bestand, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var slot = new RunLock(stream);
                    slot.NoteerEigenaar(o);
                    var gewacht = (int)Math.Round((DateTime.UtcNow - start).TotalSeconds);
                    if (gewacht > 0) result["gewacht"] = gewacht + " s op een andere wake-claude";
                    return slot;
                }
                catch (IOException)
                {
                    if (!gemeld)
                    {
                        Log.Info("Wachten op een andere wake-claude: " + HuidigeEigenaar());
                        gemeld = true;
                    }
                    if (DateTime.UtcNow >= deadline)
                        throw new WakeException(ExitCodes.Busy, "Er draait al een wake-claude (" + HuidigeEigenaar() +
                            ") en die is niet klaar binnen " + o.LockTimeoutSeconds + " s. Probeer later opnieuw of geef --lock-timeout mee.");
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>Wie de lock heeft, in een apart bestand: het lockbestand zelf is niet leesbaar.</summary>
        void NoteerEigenaar(Options o)
        {
            try
            {
                var p = Process.GetCurrentProcess();
                File.WriteAllText(Eigenaar, "pid " + p.Id + ", " + o.Mode.ToString().ToLowerInvariant() +
                    (o.Name != null ? " \"" + o.Name + "\"" : "") + ", gestart " +
                    DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Encoding.UTF8);
            }
            catch (IOException) { }                 // enkel voor de foutmelding; nooit het werk blokkeren
            catch (UnauthorizedAccessException) { }
        }

        static string HuidigeEigenaar()
        {
            try { return File.ReadAllText(Eigenaar, Encoding.UTF8).Trim(); }
            catch (Exception) { return "onbekende run"; }
        }

        public void Dispose()
        {
            if (stream == null) return;
            try { stream.Dispose(); }
            catch (IOException) { }
            stream = null;
            try { File.Delete(Eigenaar); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
