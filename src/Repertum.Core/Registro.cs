// Repertum - il registro delle operazioni, con l'orario di ciascuna.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Repertum
{
    public class Registro
    {
        readonly List<string> righe = new List<string>();
        string file;
        public event Action<string> Nuova;
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public int Conteggio { get { lock (righe) return righe.Count; } }

        public void Scrivi(string testo)
        {
            var r = Formati.Data(DateTime.Now) + "  " + testo;
            lock (righe)
            {
                righe.Add(r);
                if (file != null) try { File.AppendAllText(file, r + "\r\n", Utf8Bom); } catch { }
            }
            var h = Nuova;
            if (h != null) h(r);
        }

        // Il LOG.txt di un'acquisizione: le righe d'avvio, quelle dal collegamento del supporto in poi,
        // e da li' in avanti ogni nuova riga fino a Chiudi().
        public void Apri(string percorso, int righeAvvio, int daRiga)
        {
            lock (righe)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Math.Min(righeAvvio, righe.Count); i++) sb.Append(righe[i]).Append("\r\n");
                for (int i = Math.Max(daRiga, righeAvvio); i < righe.Count; i++) sb.Append(righe[i]).Append("\r\n");
                File.WriteAllText(percorso, sb.ToString(), Utf8Bom);
                file = percorso;
            }
        }

        public void Chiudi() { lock (righe) file = null; }

        public string Tutto() { lock (righe) return string.Join("\r\n", righe); }
    }
}
