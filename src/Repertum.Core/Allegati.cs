// Repertum - i file prodotti accanto alle copie: HASH.sha256, HASH.md5, INVENTARIO.csv,
// RIEPILOGO.txt, RAPPORTO.html (il LOG.txt lo scrive il Registro). Stesso formato su ogni sistema.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Repertum
{
    public static class Allegati
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static readonly Algoritmi[] Tutti = { Algoritmi.MD5, Algoritmi.SHA1, Algoritmi.SHA256, Algoritmi.SHA512 };
        public static string Nome(Algoritmi a) { return a == Algoritmi.SHA1 ? "SHA-1" : a == Algoritmi.SHA256 ? "SHA-256" : a == Algoritmi.SHA512 ? "SHA-512" : "MD5"; }
        public static string Estensione(Algoritmi a) { return a == Algoritmi.SHA1 ? "sha1" : a == Algoritmi.SHA256 ? "sha256" : a == Algoritmi.SHA512 ? "sha512" : "md5"; }

        static string Programma() { return "Repertum per " + Piattaforma.Nome + " " + Versione.Breve; }

        // Formato sha256sum / md5sum, separatore '/': lo leggono sha256sum -c, shasum -c, Get-FileHash,
        // HashCheck e le altre versioni di Repertum.
        public static void Hash(string cartella, List<VoceFile> voci, Algoritmi alg)
        {
            foreach (var a in Tutti)
            {
                if ((alg & a) == 0) continue;
                var sb = new StringBuilder();
                foreach (var v in voci) { var h = v.Hash(a); if (v.InElenco && !string.IsNullOrEmpty(h)) sb.Append(h).Append("  ").Append(Formati.PercorsoHash(v.Percorso)).Append('\n'); }
                if (sb.Length > 0) File.WriteAllText(Path.Combine(cartella, "HASH." + Estensione(a)), sb.ToString(), Utf8);
            }
        }

        static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static void Inventario(string cartella, List<VoceFile> voci, Algoritmi alg)
        {
            var usati = Tutti.Where(a => (alg & a) != 0).ToList();
            var sb = new StringBuilder();
            sb.Append("N.;Percorso sul supporto;Dimensione (byte)");
            foreach (var a in usati) sb.Append(';').Append(Nome(a));
            sb.Append(";Acquisito;Ultima modifica;Creazione;Esito\r\n");
            foreach (var v in voci)
            {
                sb.Append(v.N).Append(';').Append(Csv(v.Percorso)).Append(';').Append(v.Dimensione);
                foreach (var a in usati) sb.Append(';').Append(v.Hash(a) ?? "");
                sb.Append(';').Append(v.Copiato ? "si" : "no").Append(';').Append(Formati.Data(v.Modificato))
                  .Append(';').Append(Formati.Data(v.Creato)).Append(';').Append(Csv(v.Esito)).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(cartella, "INVENTARIO.csv"), sb.ToString(), Utf8Bom);
        }

        public static void Riepilogo(string cartella, InfoSupporto s, List<string> protezione,
                                     List<VoceFile> voci, Algoritmi alg, List<KeyValuePair<string, string>> tempi)
        {
            var r = new List<string>();
            Action<string, string> riga = (k, v) => { if (!string.IsNullOrEmpty(v)) r.Add(string.Format("{0,-30} {1}", k + ":", v)); };
            r.Add("REPERTUM " + Versione.Breve + " - riepilogo");
            r.Add(new string('=', 70));
            r.Add("");
            riga("Computer", Environment.MachineName + ", utente " + Environment.UserName);
            riga("Programma", Programma());
            foreach (var t in tempi) riga(t.Key, t.Value);
            r.Add("");
            r.Add("CHIAVETTA");
            r.Add(new string('-', 70));
            foreach (var kv in s.Righe()) r.Add(string.Format("{0,-38} {1}", kv.Key + ":", kv.Value));
            r.Add("");
            r.Add("PROTEZIONE DA SCRITTURA");
            r.Add(new string('-', 70));
            r.AddRange(protezione);
            r.Add("");
            var letti = voci.Where(v => v.SHA256 != null).ToList();
            var copiati = voci.Where(v => v.Copiato).ToList();
            r.Add("CONTENUTO E HASH");
            r.Add(new string('-', 70));
            r.Add(string.Format("File sulla chiavetta:         {0} ({1})", voci.Count, Formati.Byte(voci.Sum(v => v.Dimensione))));
            r.Add(string.Format("Hash calcolati:               {0}", letti.Count));
            r.Add(string.Format("Algoritmi:                    {0}", string.Join(", ", Tutti.Where(a => (alg & a) != 0).Select(Nome))));
            r.Add(string.Format("File copiati e verificati:    {0} su {1} ({2})", copiati.Count(v => v.Esito == "Copia verificata"), copiati.Count, Formati.Byte(copiati.Sum(v => v.Dimensione))));
            if (letti.Count < voci.Count) r.Add(string.Format("File NON leggibili:           {0} (vedi INVENTARIO.csv)", voci.Count - letti.Count));
            r.AddRange(Piattaforma.Metodo);
            r.Add("");
            r.Add("ELENCO DEGLI HASH");
            r.Add(new string('-', 70));
            foreach (var v in voci.Where(x => x.InElenco))
            {
                r.Add(string.Format("{0}. {1}  ({2})", v.N, v.Percorso, Formati.ByteEsatti(v.Dimensione)));
                foreach (var a in Tutti) { var h = v.Hash(a); if ((alg & a) != 0 && h != null) r.Add(string.Format("   {0,-8} {1}", Nome(a), h)); }
            }
            r.Add("");
            r.Add("Nella cartella: HASH.sha256 e HASH.md5, INVENTARIO.csv (tutti i file della");
            r.Add("chiavetta), RAPPORTO.html (da stampare), LOG.txt (ogni operazione con l'orario),");
            r.Add("File" + Formati.Separatore + " (le copie, in sola lettura).");
            File.WriteAllText(Path.Combine(cartella, "RIEPILOGO.txt"), string.Join("\r\n", r) + "\r\n", Utf8Bom);
        }

        static string H(string s) { return System.Net.WebUtility.HtmlEncode(s ?? ""); }

        public static void Rapporto(string cartella, InfoSupporto s, List<string> protezione,
                                    List<VoceFile> voci, Algoritmi alg, List<KeyValuePair<string, string>> tempi)
        {
            var usati = Tutti.Where(a => (alg & a) != 0).ToList();
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>Repertum - ").Append(H(Path.GetFileName(cartella))).Append("</title>");
            sb.Append("<style>body{font:13px Tahoma,Verdana,sans-serif;margin:24px;color:#000}h1{font-size:18px;border-bottom:2px solid #003399;color:#003399}");
            sb.Append("h2{font-size:14px;background:#E6EBF5;padding:4px 6px;border:1px solid #9AA9C9}table{border-collapse:collapse;margin-bottom:14px}");
            sb.Append("td,th{border:1px solid #9AA9C9;padding:3px 6px;vertical-align:top}th{background:#D9E1F2;text-align:left}");
            sb.Append(".h{font-family:Consolas,'Courier New',monospace;font-size:12px}.ok{color:#006600;font-weight:bold}.ko{color:#CC0000;font-weight:bold}</style></head><body>");
            sb.Append("<h1>Repertum - riepilogo</h1><table>");
            Action<string, string> riga = (k, v) => { if (!string.IsNullOrEmpty(v)) sb.Append("<tr><th>").Append(H(k)).Append("</th><td>").Append(H(v)).Append("</td></tr>"); };
            riga("Computer", Environment.MachineName + ", utente " + Environment.UserName);
            foreach (var t in tempi) riga(t.Key, t.Value);
            sb.Append("</table><h2>Chiavetta</h2><table>");
            foreach (var kv in s.Righe()) riga(kv.Key, kv.Value);
            sb.Append("</table><h2>Protezione da scrittura</h2><p>").Append(H(string.Join(" ", protezione))).Append("</p>");
            sb.Append("<h2>File e hash</h2><table><tr><th>N.</th><th>File</th><th>Dimensione</th><th>Ultima modifica</th>");
            foreach (var a in usati) sb.Append("<th>").Append(Nome(a)).Append("</th>");
            sb.Append("<th>Esito</th></tr>");
            foreach (var v in voci)
            {
                sb.Append("<tr><td>").Append(v.N).Append("</td><td>").Append(H(v.Percorso)).Append("</td><td>").Append(H(Formati.ByteEsatti(v.Dimensione)))
                  .Append("</td><td>").Append(H(Formati.Data(v.Modificato))).Append("</td>");
                foreach (var a in usati) sb.Append("<td class=\"h\">").Append(H(v.Hash(a))).Append("</td>");
                var cls = v.Esito == "Copia verificata" ? "ok" : (v.Esito.StartsWith("COPIA") || v.Esito.StartsWith("NON") || v.Esito.StartsWith("ORIGINALE") ? "ko" : "");
                sb.Append("<td class=\"").Append(cls).Append("\">").Append(H(v.Esito)).Append("</td></tr>");
            }
            sb.Append("</table><p style=\"color:#555\">Generato da ").Append(H(Programma())).Append(" il ")
              .Append(H(Formati.Data(DateTime.Now))).Append(".</p></body></html>");
            File.WriteAllText(Path.Combine(cartella, "RAPPORTO.html"), sb.ToString(), Utf8);
        }

        // Tutti gli allegati di un'acquisizione, con la riga di registro finale.
        public static void Scrivi(string cartella, InfoSupporto s, List<string> protezione, List<VoceFile> voci,
                                  Algoritmi alg, List<KeyValuePair<string, string>> tempi, bool conCopia, Registro log)
        {
            try
            {
                Hash(cartella, voci, alg);
                Inventario(cartella, voci, alg);
                Riepilogo(cartella, s ?? new InfoSupporto(), protezione, voci, alg, tempi);
                Rapporto(cartella, s ?? new InfoSupporto(), protezione, voci, alg, tempi);
                log.Scrivi("File prodotti: HASH.sha256, HASH.md5, INVENTARIO.csv, RIEPILOGO.txt, RAPPORTO.html" + (conCopia ? ", File" + Formati.Separatore : "") + ".");
            }
            catch (Exception e) { log.Scrivi("ERRORE nella scrittura dei file di riepilogo: " + e.Message); }
        }

        // Gli orari del riepilogo, nell'ordine in cui sono avvenuti.
        public static List<KeyValuePair<string, string>> Tempi(DateTime oraBlocco, DateTime oraCollegamento, DateTime inizioCalcolo,
                                                               DateTime fineCalcolo, bool conCopia, DateTime inizio, DateTime fine)
        {
            var tempi = new List<KeyValuePair<string, string>>();
            Action<string, DateTime> t = (k, d) => { if (d != default(DateTime)) tempi.Add(new KeyValuePair<string, string>(k, Formati.Data(d))); };
            t("Sola lettura attivata alle", oraBlocco);
            t("Chiavetta collegata alle", oraCollegamento);
            t("Calcolo degli hash", inizioCalcolo);
            t("Fine del calcolo", fineCalcolo);
            t(conCopia ? "Inizio della copia" : "Salvataggio", inizio);
            t("Fine", fine);
            return tempi;
        }

        // HASH.sha256, HASH.md5 ecc.: righe "hash  nome" oppure "hash *nome", con '/' oppure '\'
        // (la 1.1 per Windows scriveva '\'). I nomi tornano con il separatore del sistema.
        public static Dictionary<string, string> LeggiElenco(string percorso)
        {
            var d = new Dictionary<string, string>(Formati.Separatore == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (!File.Exists(percorso)) return d;
            foreach (var riga in File.ReadAllLines(percorso, Encoding.UTF8))
            {
                var m = System.Text.RegularExpressions.Regex.Match(riga, @"^\s*(\\?)([0-9a-fA-F]{32,128}) [ *](.+?)\s*$");
                if (!m.Success) continue;
                var nome = m.Groups[3].Value;
                // sha256sum premette '\' alle righe con nomi "speciali" e ne protegge i caratteri
                if (m.Groups[1].Value.Length > 0) nome = nome.Replace("\\\\", "\u0001").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\u0001", "\\");
                d[Formati.PercorsoNativo(nome)] = m.Groups[2].Value.ToLowerInvariant();
            }
            return d;
        }
    }
}
