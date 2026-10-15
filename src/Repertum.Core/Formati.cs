// Repertum - formattazione di numeri, date, dimensioni, nomi di cartella e percorsi.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Repertum
{
    public static class Formati
    {
        public static readonly CultureInfo It = Italiano();

        // Senza le tabelle ICU (Linux in modalita' invariante) "it-IT" non esiste: si costruisce
        // a mano quello che serve, cioe' il punto per le migliaia e la virgola per i decimali.
        static CultureInfo Italiano()
        {
            try
            {
                var c = new CultureInfo("it-IT");
                if (1234.5.ToString("N1", c) == "1.234,5") return c;
            }
            catch (Exception) { }
            var k = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            k.NumberFormat.NumberGroupSeparator = ".";
            k.NumberFormat.NumberDecimalSeparator = ",";
            k.NumberFormat.PercentGroupSeparator = ".";
            k.NumberFormat.PercentDecimalSeparator = ",";
            k.DateTimeFormat.DateSeparator = "/";
            k.DateTimeFormat.TimeSeparator = ":";
            return k;
        }

        public static string Byte(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("N2", It) + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("N1", It) + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("N0", It) + " KB";
            return b.ToString("N0", It) + " byte";
        }

        public static string ByteEsatti(long b) { return b.ToString("N0", It) + " byte"; }
        public static string Data(DateTime d) { return d.ToString("dd/MM/yyyy HH:mm:ss", It); }
        public static string Durata(TimeSpan t) { return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds); }

        public static string Esa(byte[] h)
        {
            var sb = new StringBuilder(h.Length * 2);
            foreach (var x in h) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public static string NomeSicuro(string s)
        {
            var sb = new StringBuilder();
            bool trattino = false;
            foreach (char c in s ?? "")
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); trattino = false; }
                else if (!trattino && sb.Length > 0) { sb.Append('-'); trattino = true; }
            }
            var t = sb.ToString().Trim('-');
            if (t.Length > 60) t = t.Substring(0, 60).Trim('-');
            return t.Length == 0 ? "acquisizione" : t;
        }

        // Separatore delle cartelle del sistema: '\' su Windows, '/' su macOS e Linux.
        public static readonly char Separatore = Path.DirectorySeparatorChar;

        // Nei file HASH.* il separatore e' sempre '/', come in sha256sum e md5sum.
        public static string PercorsoHash(string percorso)
        {
            return (percorso ?? "").Replace('\\', '/');
        }

        // Da un percorso letto in un file HASH.* (con '/' oppure con '\', come scriveva la 1.1)
        // al percorso con il separatore del sistema.
        public static string PercorsoNativo(string percorso)
        {
            var p = percorso ?? "";
            return Separatore == '\\' ? p.Replace('/', '\\') : p.Replace('\\', '/');
        }
    }
}
