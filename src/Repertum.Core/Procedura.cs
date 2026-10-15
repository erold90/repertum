// Repertum - i tre lavori, uguali su ogni sistema: lettura con gli hash, copia con la verifica,
// verifica di una cartella salvata. L'interfaccia riceve l'avanzamento con le funzioni passate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Repertum
{
    public class EsitoVerifica
    {
        public int Totale, Integri, Alterati, Mancanti;
        public bool Interrotta;
    }

    public static class Procedura
    {
        // Lettura di ogni file del supporto con il calcolo degli hash. Vero se interrotta.
        public static bool CalcolaHash(List<VoceFile> lista, Algoritmi alg, Registro log, Action<long> avanza,
                                       Action<VoceFile> inizio, Action<VoceFile> fine, CancellationToken token)
        {
            var buf = new byte[Calcolo.DimensioneBuffer];
            foreach (var v in lista)
            {
                if (token.IsCancellationRequested) break;
                if (inizio != null) inizio(v);
                try
                {
                    var r = Calcolo.Leggi(v.Assoluto, null, alg, buf, avanza, token);
                    v.MD5 = r.MD5; v.SHA1 = r.SHA1; v.SHA256 = r.SHA256; v.SHA512 = r.SHA512;
                    v.Esito = "Letto";
                    log.Scrivi(r.SHA256 + "  " + v.Percorso + "  (" + Formati.ByteEsatti(v.Dimensione) + ")");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    v.Esito = "NON LEGGIBILE: " + e.Message;
                    log.Scrivi("ERRORE di lettura su " + v.Percorso + ": " + e.Message);
                }
                if (fine != null) fine(v);
            }
            return token.IsCancellationRequested;
        }

        // Copia dei file scelti in <cartella>/File, rilettura della copia e confronto dello SHA-256.
        // Le copie restano in sola lettura. Restituisce quante copie sono verificate.
        public static int CopiaEVerifica(List<VoceFile> scelti, string cartella, Registro log, Action<long> avanza,
                                         Action<VoceFile> inizio, Action<VoceFile> fine, CancellationToken token)
        {
            int buone = 0;
            var buf = new byte[Calcolo.DimensioneBuffer];
            var dirFile = Path.Combine(cartella, "File");
            foreach (var v in scelti)
            {
                if (token.IsCancellationRequested) break;
                if (inizio != null) inizio(v);
                var dest = Path.Combine(dirFile, v.Percorso);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    var r = Calcolo.Leggi(v.Assoluto, dest, Algoritmi.SHA256, buf, avanza, token);
                    File.SetCreationTime(dest, v.Creato);
                    File.SetLastWriteTime(dest, v.Modificato);
                    v.Copiato = true;
                    v.InElenco = true;
                    if (r.SHA256 != v.SHA256) v.Esito = "ORIGINALE CAMBIATO";
                    else
                    {
                        var c = Calcolo.Leggi(dest, null, Algoritmi.SHA256, buf, avanza, token);
                        if (c.SHA256 == v.SHA256) { v.Esito = "Copia verificata"; buone++; }
                        else v.Esito = "COPIA DIFFORME";
                    }
                    File.SetAttributes(dest, File.GetAttributes(dest) | FileAttributes.ReadOnly);
                    log.Scrivi(v.Esito + ": " + v.Percorso);
                }
                catch (OperationCanceledException)
                {
                    try { if (File.Exists(dest)) { File.SetAttributes(dest, FileAttributes.Normal); File.Delete(dest); } } catch { }
                    v.Copiato = false; v.InElenco = false;
                    break;
                }
                catch (Exception e)
                {
                    try { if (File.Exists(dest)) { File.SetAttributes(dest, FileAttributes.Normal); File.Delete(dest); } } catch { }
                    v.Copiato = false; v.InElenco = false;
                    v.Esito = "COPIA NON RIUSCITA: " + e.Message;
                    log.Scrivi("ERRORE nella copia di " + v.Percorso + ": " + e.Message);
                }
                if (fine != null) fine(v);
            }
            return buone;
        }

        // Byte da rileggere per verificare l'elenco (per la barra).
        public static long DaVerificare(List<KeyValuePair<string, string>> elenco, string baseDir)
        {
            long totale = 0;
            foreach (var kv in elenco)
            {
                var p = Path.Combine(baseDir, kv.Key);
                try { if (File.Exists(p)) totale += new FileInfo(p).Length; } catch { }
            }
            return Math.Max(1, totale);
        }

        // Ricalcolo degli hash di una cartella salvata. riga(indice, sha256 di adesso, esito, byte letti finora).
        public static EsitoVerifica Verifica(List<KeyValuePair<string, string>> elenco, Dictionary<string, string> md5, string baseDir,
                                             Action<int, string, string, long> riga, CancellationToken token)
        {
            var buf = new byte[Calcolo.DimensioneBuffer];
            var e = new EsitoVerifica { Totale = elenco.Count };
            long fatti = 0;
            for (int i = 0; i < elenco.Count; i++)
            {
                if (token.IsCancellationRequested) break;
                var kv = elenco[i];
                var p = Path.Combine(baseDir, kv.Key);
                string calcolato = "", esito;
                if (!File.Exists(p)) { esito = "MANCANTE"; e.Mancanti++; }
                else
                {
                    try
                    {
                        bool conMd5 = md5.ContainsKey(kv.Key);
                        var r = Calcolo.Leggi(p, null, conMd5 ? Algoritmi.SHA256 | Algoritmi.MD5 : Algoritmi.SHA256, buf, x => fatti += x, token);
                        calcolato = r.SHA256;
                        bool ok = r.SHA256 == kv.Value && (!conMd5 || r.MD5 == null || r.MD5 == md5[kv.Key]);
                        esito = ok ? "INTEGRO" : "ALTERATO";
                        if (ok) e.Integri++; else e.Alterati++;
                    }
                    catch (OperationCanceledException) { break; }
                    catch { esito = "ILLEGGIBILE"; e.Alterati++; }
                }
                if (riga != null) riga(i, calcolato, esito, fatti);
            }
            e.Interrotta = token.IsCancellationRequested;
            return e;
        }

        // Una riga in VERIFICHE.txt e una nel registro per ogni verifica fatta.
        public static void AnnotaVerifica(string cartella, string baseDir, EsitoVerifica e, Registro log)
        {
            try
            {
                File.AppendAllText(Path.Combine(cartella, "VERIFICHE.txt"),
                    Formati.Data(DateTime.Now) + "  verifica su " + baseDir + ": " + e.Integri + " integri, " + (e.Alterati + e.Mancanti) + " anomalie" + (e.Interrotta ? " (interrotta)" : "") + "\r\n",
                    new UTF8Encoding(true));
            }
            catch { }
            log.Scrivi("Verifica di " + cartella + " contro " + baseDir + ": " + e.Integri + " integri, " + e.Alterati + " alterati, " + e.Mancanti + " mancanti" + (e.Interrotta ? " (interrotta)" : "") + ".");
        }
    }
}
