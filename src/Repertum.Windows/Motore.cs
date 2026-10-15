// Repertum - il motore: supporti, protezione da scrittura, hash, allegati.
// Stessa procedura e stessi file prodotti della versione per Mac (repertum / acquisisci.py)
// e dello script PowerShell: una cartella si verifica con l'uno o con l'altro.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Repertum
{
    static class Nativo
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string nome, uint accesso, uint condivisione, IntPtr sicurezza,
                                                        uint creazione, uint attributi, IntPtr modello);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint codice, IntPtr ingresso, uint dimIngresso,
                                                  IntPtr uscita, uint dimUscita, out uint restituiti, IntPtr sovrapposto);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetVolumeInformationW(string radice, StringBuilder nome, int dimNome, out uint seriale,
                                                        out uint lunghezzaMax, out uint flag, StringBuilder fs, int dimFs);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(out uint devInst, string id, int flag);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(out uint padre, uint devInst, int flag);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int lunghezza, int flag);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flag);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string app, string elenco);
    }

    static class Formati
    {
        public static readonly CultureInfo It = new CultureInfo("it-IT");

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
    }

    // ------------------------------------------------------------------ hash

    [Flags]
    public enum Algoritmi { Nessuno = 0, MD5 = 1, SHA1 = 2, SHA256 = 4, SHA512 = 8 }

    public class Impronte
    {
        public string MD5, SHA1, SHA256, SHA512;
    }

    public static class Calcolo
    {
        public static bool Md5Disponibile = true;
        public const int DimensioneBuffer = 4 * 1024 * 1024;

        // Le classi CryptoServiceProvider sono quelle ammesse anche con il criterio FIPS di Windows;
        // MD5 con FIPS e' vietato: in quel caso si prosegue senza.
        static HashAlgorithm Crea(Algoritmi a)
        {
            switch (a)
            {
                case Algoritmi.MD5: return new MD5CryptoServiceProvider();
                case Algoritmi.SHA1: return new SHA1CryptoServiceProvider();
                case Algoritmi.SHA256: return new SHA256CryptoServiceProvider();
                default: return new SHA512CryptoServiceProvider();
            }
        }

        // Una sola lettura del file: tutti gli hash richiesti e, se indicata, la copia.
        public static Impronte Leggi(string sorgente, string copia, Algoritmi richiesti, byte[] buf,
                                     Action<long> avanza, CancellationToken ct)
        {
            var calcoli = new List<KeyValuePair<Algoritmi, HashAlgorithm>>();
            try
            {
                foreach (var a in new[] { Algoritmi.MD5, Algoritmi.SHA1, Algoritmi.SHA256, Algoritmi.SHA512 })
                {
                    if ((richiesti & a) == 0) continue;
                    if (a == Algoritmi.MD5 && !Md5Disponibile) continue;
                    try { calcoli.Add(new KeyValuePair<Algoritmi, HashAlgorithm>(a, Crea(a))); }
                    catch (InvalidOperationException) { if (a == Algoritmi.MD5) Md5Disponibile = false; else throw; }
                }
                using (var ing = new FileStream(sorgente, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan))
                using (var usc = copia == null ? null : new FileStream(copia, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    int n;
                    while ((n = ing.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        foreach (var c in calcoli) c.Value.TransformBlock(buf, 0, n, null, 0);
                        if (usc != null) usc.Write(buf, 0, n);
                        if (avanza != null) avanza(n);
                    }
                    foreach (var c in calcoli) c.Value.TransformFinalBlock(buf, 0, 0);
                }
                var r = new Impronte();
                foreach (var c in calcoli)
                {
                    var h = Formati.Esa(c.Value.Hash);
                    if (c.Key == Algoritmi.MD5) r.MD5 = h;
                    else if (c.Key == Algoritmi.SHA1) r.SHA1 = h;
                    else if (c.Key == Algoritmi.SHA256) r.SHA256 = h;
                    else r.SHA512 = h;
                }
                return r;
            }
            finally
            {
                foreach (var c in calcoli) c.Value.Dispose();
            }
        }
    }

    // ------------------------------------------------------------------ file del supporto

    public class VoceFile
    {
        public int N;
        public string Percorso;      // relativo alla radice, separatore '\'
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
            get { var d = Path.GetDirectoryName(Percorso); return string.IsNullOrEmpty(d) ? "\\" : "\\" + d + "\\"; }
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
        public static List<VoceFile> Leggi(string radice, List<string> errori)
        {
            var voci = new List<VoceFile>();
            if (!radice.EndsWith("\\")) radice += "\\";
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
                    foreach (var d in new DirectoryInfo(dir).GetDirectories()) pila.Push(d.FullName + "\\");
                }
                catch (Exception e) { errori.Add(dir + " - " + e.Message); }
            }
            voci.Sort((a, b) => string.Compare(a.Percorso, b.Percorso, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < voci.Count; i++) voci[i].N = i + 1;
            return voci;
        }
    }

    // ------------------------------------------------------------------ supporti

    public class InfoSupporto
    {
        public string Lettera, Etichetta, FileSystem, SerialeVolume, Modello, Seriale, Interfaccia, PnpId, VidPid;
        public long CapacitaVolume, Occupato, CapacitaDispositivo;

        public List<KeyValuePair<string, string>> Righe()
        {
            var r = new List<KeyValuePair<string, string>>();
            Action<string, string> a = (k, v) => { if (!string.IsNullOrEmpty(v)) r.Add(new KeyValuePair<string, string>(k, v)); };
            a("Unità", Lettera);
            a("Modello", Modello);
            a("Numero di serie del dispositivo", Seriale);
            a("Produttore e prodotto USB (VID:PID)", VidPid);
            a("Interfaccia", Interfaccia);
            a("Capacità del dispositivo", CapacitaDispositivo > 0 ? Formati.Byte(CapacitaDispositivo) + " (" + Formati.ByteEsatti(CapacitaDispositivo) + ")" : null);
            a("Etichetta del volume", Etichetta);
            a("File system", FileSystem);
            a("Numero di serie del volume", SerialeVolume);
            a("Capacità del volume", CapacitaVolume > 0 ? Formati.Byte(CapacitaVolume) + " (" + Formati.ByteEsatti(CapacitaVolume) + ")" : null);
            a("Spazio occupato", Formati.Byte(Occupato));
            a("Identificativo Windows", PnpId);
            return r;
        }
    }

    public class Protezione
    {
        public bool Protetto;
        public bool Verificata;    // false = il controllo non ha potuto rispondere
        public string Dettaglio;
    }

    public static class Dischi
    {
        // Lettere delle unita' pronte (escluse rete e lettori ottici), con il tipo.
        public static Dictionary<string, DriveType> Pronte()
        {
            var d = new Dictionary<string, DriveType>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in DriveInfo.GetDrives())
            {
                try
                {
                    if (u.DriveType == DriveType.Network || u.DriveType == DriveType.CDRom) continue;
                    if (!u.IsReady || u.TotalSize <= 0) continue;
                    d[u.Name.Substring(0, 2).ToUpperInvariant()] = u.DriveType;
                }
                catch { }
            }
            return d;
        }

        public static InfoSupporto Descrivi(string lettera)
        {
            var i = new InfoSupporto { Lettera = lettera + "\\" };
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DeviceID='" + lettera + "'"))
                {
                    foreach (ManagementObject ld in s.Get())
                    {
                        i.Etichetta = ld["VolumeName"] as string;
                        i.FileSystem = ld["FileSystem"] as string;
                        i.SerialeVolume = ld["VolumeSerialNumber"] as string;
                        i.CapacitaVolume = Convert.ToInt64(ld["Size"] ?? 0L);
                        i.Occupato = i.CapacitaVolume - Convert.ToInt64(ld["FreeSpace"] ?? 0L);
                        foreach (ManagementObject parte in ld.GetRelated("Win32_DiskPartition"))
                        {
                            foreach (ManagementObject disco in parte.GetRelated("Win32_DiskDrive"))
                            {
                                i.Modello = (disco["Model"] as string ?? "").Trim();
                                i.Seriale = (disco["SerialNumber"] as string ?? "").Trim();
                                i.Interfaccia = disco["InterfaceType"] as string;
                                i.CapacitaDispositivo = Convert.ToInt64(disco["Size"] ?? 0L);
                                i.PnpId = disco["PNPDeviceID"] as string;
                                break;
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { i.Modello = "non identificato (" + e.Message + ")"; }

            // Il dispositivo USB "padre" porta VID, PID e il numero di serie USB vero e proprio.
            try
            {
                uint nodo, padre;
                if (!string.IsNullOrEmpty(i.PnpId) && Nativo.CM_Locate_DevNodeW(out nodo, i.PnpId, 0) == 0 &&
                    Nativo.CM_Get_Parent(out padre, nodo, 0) == 0)
                {
                    var sb = new StringBuilder(400);
                    if (Nativo.CM_Get_Device_IDW(padre, sb, sb.Capacity, 0) == 0)
                    {
                        var id = sb.ToString();
                        var m = System.Text.RegularExpressions.Regex.Match(id, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})\\(.+)$",
                                                                          System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            i.VidPid = m.Groups[1].Value + ":" + m.Groups[2].Value;
                            if (!m.Groups[3].Value.Contains("&")) i.Seriale = m.Groups[3].Value;
                        }
                    }
                }
            }
            catch { }
            return i;
        }

        // Controllo NON invasivo: si interroga il disco e il file system, senza mai scrivere sul supporto.
        public static Protezione Controlla(string lettera)
        {
            bool solaLettura = false;
            int disco = -1;
            try
            {
                var n = new StringBuilder(261);
                var f = new StringBuilder(261);
                uint s, l, flag;
                if (Nativo.GetVolumeInformationW(lettera + "\\", n, n.Capacity, out s, out l, out flag, f, f.Capacity))
                    solaLettura = (flag & 0x00080000) != 0;          // FILE_READ_ONLY_VOLUME
            }
            catch { }
            try
            {
                using (var h = Nativo.CreateFileW(@"\\.\" + lettera, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) disco = Marshal.GetLastWin32Error();
                    else
                    {
                        uint r;
                        disco = Nativo.DeviceIoControl(h, 0x00070024, IntPtr.Zero, 0, IntPtr.Zero, 0, out r, IntPtr.Zero)
                            ? 0 : Marshal.GetLastWin32Error();             // IOCTL_DISK_IS_WRITABLE
                    }
                }
            }
            catch { }

            var motivi = new List<string>();
            if (solaLettura) motivi.Add("il file system ha montato il volume in sola lettura");
            if (disco == 19) motivi.Add("il disco risponde «protetto da scrittura»");
            if (motivi.Count > 0) return new Protezione { Protetto = true, Verificata = true, Dettaglio = string.Join("; ", motivi) };
            if (disco == 0) return new Protezione { Protetto = false, Verificata = true, Dettaglio = "il disco accetta scritture" };
            return new Protezione { Protetto = false, Verificata = false, Dettaglio = "il disco non ha risposto al controllo (codice " + disco + ")" };
        }
    }

    // ------------------------------------------------------------------ protezione da scrittura

    // Criterio di Windows StorageDevicePolicies\WriteProtect: vale per i supporti USB collegati dopo
    // l'attivazione. Lo stato precedente si conserva in HKLM\SOFTWARE\Repertum, cosi' anche dopo
    // una chiusura anomala la riapertura e la chiusura del programma rimettono il PC com'era.
    public static class Blocco
    {
        const string Criterio = @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies";
        const string Memoria = @"SOFTWARE\Repertum";
        public static bool Attivo { get; private set; }
        public static bool SessionePrecedenteInterrotta { get; private set; }

        static RegistryKey Radice()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        }

        public static void Attiva()
        {
            using (var r = Radice())
            using (var m = r.CreateSubKey(Memoria))
            {
                SessionePrecedenteInterrotta = Convert.ToInt32(m.GetValue("BloccoAttivo", 0)) == 1;
                if (!SessionePrecedenteInterrotta)
                {
                    object prima = null;
                    using (var c = r.OpenSubKey(Criterio)) { if (c != null) prima = c.GetValue("WriteProtect"); }
                    m.SetValue("ValorePrecedente", prima == null ? -1 : Convert.ToInt32(prima), RegistryValueKind.DWord);
                }
                using (var c = r.CreateSubKey(Criterio)) c.SetValue("WriteProtect", 1, RegistryValueKind.DWord);
                m.SetValue("BloccoAttivo", 1, RegistryValueKind.DWord);
            }
            Attivo = true;
        }

        public static void Ripristina()
        {
            using (var r = Radice())
            using (var m = r.CreateSubKey(Memoria))
            {
                int prima = Convert.ToInt32(m.GetValue("ValorePrecedente", -1));
                using (var c = r.CreateSubKey(Criterio))
                {
                    if (prima < 0) c.DeleteValue("WriteProtect", false);
                    else c.SetValue("WriteProtect", prima, RegistryValueKind.DWord);
                }
                m.SetValue("BloccoAttivo", 0, RegistryValueKind.DWord);
            }
            Attivo = false;
        }
    }

    // ------------------------------------------------------------------ registro delle operazioni

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

    // ------------------------------------------------------------------ allegati

    public static class Allegati
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static readonly Algoritmi[] Tutti = { Algoritmi.MD5, Algoritmi.SHA1, Algoritmi.SHA256, Algoritmi.SHA512 };
        public static string Nome(Algoritmi a) { return a == Algoritmi.SHA1 ? "SHA-1" : a == Algoritmi.SHA256 ? "SHA-256" : a == Algoritmi.SHA512 ? "SHA-512" : "MD5"; }
        public static string Estensione(Algoritmi a) { return a == Algoritmi.SHA1 ? "sha1" : a == Algoritmi.SHA256 ? "sha256" : a == Algoritmi.SHA512 ? "sha512" : "md5"; }

        // Formato sha256sum / md5sum: lo leggono HashCheck, Get-FileHash, shasum e le altre versioni di Repertum.
        public static void Hash(string cartella, List<VoceFile> voci, Algoritmi alg)
        {
            foreach (var a in Tutti)
            {
                if ((alg & a) == 0) continue;
                var sb = new StringBuilder();
                foreach (var v in voci) { var h = v.Hash(a); if (v.InElenco && !string.IsNullOrEmpty(h)) sb.Append(h).Append("  ").Append(v.Percorso).Append('\n'); }
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
            r.Add("REPERTUM " + Programma.Versione + " - riepilogo");
            r.Add(new string('=', 70));
            r.Add("");
            riga("Computer", Environment.MachineName + ", utente " + Environment.UserName);
            riga("Programma", "Repertum per Windows " + Programma.Versione);
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
            r.Add("Metodo: hash calcolati con le librerie crittografiche di Windows");
            r.Add("(System.Security.Cryptography) in un'unica lettura di ciascun file; ogni copia");
            r.Add("e' stata riletta e il suo SHA-256 confrontato con quello dell'originale.");
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
            r.Add("File\\ (le copie, in sola lettura).");
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
            sb.Append("</table><p style=\"color:#555\">Generato da Repertum per Windows ").Append(Programma.Versione).Append(" il ")
              .Append(H(Formati.Data(DateTime.Now))).Append(".</p></body></html>");
            File.WriteAllText(Path.Combine(cartella, "RAPPORTO.html"), sb.ToString(), Utf8);
        }

        // HASH.sha256, HASH.md5 ecc.: righe "hash  nome" oppure "hash *nome".
        public static Dictionary<string, string> LeggiElenco(string percorso)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(percorso)) return d;
            foreach (var riga in File.ReadAllLines(percorso, Encoding.UTF8))
            {
                var m = System.Text.RegularExpressions.Regex.Match(riga, @"^\s*([0-9a-fA-F]{32,128}) [ *](.+?)\s*$");
                if (m.Success) d[m.Groups[2].Value] = m.Groups[1].Value.ToLowerInvariant();
            }
            return d;
        }
    }
}
