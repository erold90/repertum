// Repertum - i file del supporto: elenco, categorie, hash di ciascuno.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Repertum
{
    public class VoceFile
    {
        public int N;
        public string Percorso;      // relativo alla radice, con il separatore del sistema
        public string Assoluto;
        public long Dimensione;
        public DateTime Modificato, Creato;
        public string Categoria;     // Video, DVR, Immagine, Altro
        public bool Selezionato = true;
        public string MD5, SHA1, SHA256, SHA512;
        public string Esito = "";
        public bool Copiato;
        public bool InElenco;

        public string Nome { get { return Path.GetFileName(Percorso); } }
        public string Cartella
        {
            get
            {
                var s = Formati.Separatore.ToString();
                var d = Path.GetDirectoryName(Percorso);
                return string.IsNullOrEmpty(d) ? s : s + d + s;
            }
        }
        public bool Multimediale { get { return Categoria != "Altro"; } }

        static readonly string[] Video = { ".mp4", ".avi", ".mov", ".mkv", ".asf", ".wmv", ".flv", ".mpg", ".mpeg", ".m4v", ".ts", ".m2ts", ".vob", ".3gp", ".webm" };
        static readonly string[] Dvr = { ".irf", ".dav", ".264", ".h264", ".bix", ".h26" };
        static readonly string[] Immagini = { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".raw", ".cr2", ".nef", ".dng", ".arw" };

        public static string CategoriaDi(string estensione)
        {
            var e = (estensione ?? "").ToLowerInvariant();
            if (Dvr.Contains(e)) return "DVR";
            if (Video.Contains(e)) return "Video";
            if (Immagini.Contains(e)) return "Immagine";
            return "Altro";
        }

        public string Hash(Algoritmi a)
        {
            switch (a)
            {
                case Algoritmi.MD5: return MD5;
                case Algoritmi.SHA1: return SHA1;
                case Algoritmi.SHA256: return SHA256;
                default: return SHA512;
            }
        }
    }

    public static class Elenco
    {
        // Tutti i file, anche nascosti e di sistema; le cartelle illeggibili finiscono negli errori.
        // Su macOS e Linux i collegamenti simbolici a cartelle non si seguono: potrebbero uscire dal supporto.
        public static List<VoceFile> Leggi(string radice, List<string> errori)
        {
            var voci = new List<VoceFile>();
            var sep = Formati.Separatore.ToString();
            if (!radice.EndsWith(sep)) radice += sep;
            var pila = new Stack<string>();
            pila.Push(radice);
            while (pila.Count > 0)
            {
                var dir = pila.Pop();
                try
                {
                    foreach (var f in new DirectoryInfo(dir).GetFiles())
                    {
                        voci.Add(new VoceFile
                        {
                            Percorso = f.FullName.Substring(radice.Length),
                            Assoluto = f.FullName,
                            Dimensione = f.Length,
                            Modificato = f.LastWriteTime,
                            Creato = f.CreationTime,
                            Categoria = VoceFile.CategoriaDi(f.Extension)
                        });
                    }
                    foreach (var d in new DirectoryInfo(dir).GetDirectories())
                    {
                        if (sep == "/" && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            errori.Add(d.FullName + " - collegamento simbolico, non seguito");
                            continue;
                        }
                        pila.Push(d.FullName + sep);
                    }
                }
                catch (Exception e) { errori.Add(dir + " - " + e.Message); }
            }
            voci.Sort((a, b) => string.Compare(a.Percorso, b.Percorso, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < voci.Count; i++) voci[i].N = i + 1;
            return voci;
        }
    }
}
