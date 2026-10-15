// Repertum per Linux - l'interfaccia (utente normale) guarda /sys/block ogni mezzo secondo e chiede
// all'aiutante privilegiato (pkexec Repertum --helper) di montare in sola lettura i dischi USB
// comparsi dopo l'avvio. Il blocco vero lo fanno la regola udev dell'aiutante e blockdev --setro.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Repertum
{
    interface IAmministratore
    {
        Dictionary<string, string> Chiedi(Dictionary<string, string> richiesta, int ms);
        void Chiudi();
    }

    // Se l'interfaccia e' gia' root (sudo) lavora nel processo stesso.
    sealed class AmministratoreLocale : IAmministratore
    {
        readonly Amministratore a = new Amministratore();

        public Dictionary<string, string> Chiedi(Dictionary<string, string> r, int ms)
        {
            try
            {
                switch (r["cmd"])
                {
                    case "avvia": return a.Avvia(uint.Parse(r["uid"]), uint.Parse(r["gid"]), r["prova"] == "true");
                    case "monta": return a.Monta(r["disco"]);
                    case "smonta": return a.Smonta(r["volume"]);
                    case "fine": a.Fine(); return Amministratore.Ok();
                }
            }
            catch (Exception e) { return Amministratore.Errore(e.Message); }
            return Amministratore.Errore("comando sconosciuto");
        }

        public void Chiudi() { a.Fine(); }
    }

    // L'aiutante in un processo a parte, avviato con pkexec: righe JSON su stdin/stdout.
    sealed class AmministratoreRemoto : IAmministratore
    {
        readonly Process p;
        readonly BlockingCollection<string> risposte = new BlockingCollection<string>();
        readonly object chiave = new object();
        readonly Action<string> log;

        public AmministratoreRemoto(Action<string> log)
        {
            this.log = log;
            var pkexec = new[] { "/usr/bin/pkexec", "/bin/pkexec", "/usr/local/bin/pkexec" }.FirstOrDefault(File.Exists);
            if (pkexec == null) throw new Exception("pkexec non è installato: avvia Repertum con sudo");
            var psi = new ProcessStartInfo(pkexec)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
            };
            psi.ArgumentList.Add(Environment.ProcessPath);
            psi.ArgumentList.Add("--helper");
            p = Process.Start(psi);
            p.StandardInput.AutoFlush = true;
            p.StandardInput.NewLine = "\n";
            new Thread(() =>
            {
                string r;
                try { while ((r = p.StandardOutput.ReadLine()) != null) risposte.Add(r); } catch { }
                risposte.CompleteAdding();
            }) { IsBackground = true, Name = "aiutante-uscita" }.Start();
            new Thread(() =>
            {
                string r;
                try { while ((r = p.StandardError.ReadLine()) != null) if (r.Trim().Length > 0 && log != null) log("Aiutante: " + r.Trim()); } catch { }
            }) { IsBackground = true, Name = "aiutante-errori" }.Start();
        }

        public Dictionary<string, string> Chiedi(Dictionary<string, string> richiesta, int ms)
        {
            lock (chiave)
            {
                try { p.StandardInput.WriteLine(Json.Scrivi(richiesta)); }
                catch (Exception) { return Amministratore.Errore(Motivo()); }
                string r;
                try
                {
                    if (risposte.TryTake(out r, ms)) return Json.Leggi(r);
                }
                catch (InvalidOperationException) { }
                return Amministratore.Errore(p.HasExited ? Motivo() : "l'aiutante non risponde");
            }
        }

        string Motivo()
        {
            try
            {
                if (!p.HasExited) p.WaitForExit(2000);
                if (!p.HasExited) return "l'aiutante non risponde";
                switch (p.ExitCode)
                {
                    case 126: return "autorizzazione annullata (pkexec)";
                    case 127: return "autorizzazione negata (pkexec)";
                    default: return "l'aiutante si è chiuso (codice " + p.ExitCode + ")";
                }
            }
            catch { return "l'aiutante si è chiuso"; }
        }

        public void Chiudi()
        {
            try
            {
                if (!p.HasExited)
                {
                    Chiedi(new Dictionary<string, string> { { "cmd", "fine" } }, 30000);
                    try { p.StandardInput.Close(); } catch { }
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } }
                }
            }
            catch { }
        }
    }

    public sealed class SistemaLinux : ISistema
    {
        sealed class Disco
        {
            public DateTime Primo;
            public bool Tentato;
            public string Volume;
            public int PrimaRiga = -1;
        }

        readonly object chiave = new object();
        readonly Dictionary<string, Disco> visti = new Dictionary<string, Disco>();
        readonly Dictionary<string, Volume> pronti = new Dictionary<string, Volume>();
        readonly Dictionary<string, Dictionary<string, string>> risposte = new Dictionary<string, Dictionary<string, string>>();
        HashSet<string> iniziali = new HashSet<string>(), esterni = new HashSet<string>();
        List<string> preesistenti = new List<string>();
        IAmministratore amm;
        Thread sentinella;
        volatile bool fine;
        bool prova, attivo;
        string problema;
        Registro log;

        public string Nome { get { return "Linux"; } }
        public string Computer { get { return "il computer"; } }
        public bool Attivo { get { return attivo; } }
        public string UltimoProblema { get { lock (chiave) return problema; } }

        void Scrivi(string r) { var l = log; if (l != null) try { l.Scrivi(r); } catch { } }

        static string Leggi(string f) { try { return File.ReadAllText(f).Trim(); } catch { return null; } }

        static bool Rimovibile(string d) { return Leggi("/sys/block/" + d + "/removable") == "1"; }

        static long Settori(string d) { long n; return long.TryParse(Leggi("/sys/block/" + d + "/size"), out n) ? n : 0; }

        // Dispositivo loop collegato a un'immagine (non i pacchetti snap) e non in chiusura.
        static bool LoopDiProva(string d)
        {
            if (!d.StartsWith("loop")) return false;
            var f = Leggi("/sys/block/" + d + "/loop/backing_file");
            if (string.IsNullOrEmpty(f) || f.EndsWith(".snap") || f.Contains("/snapd/")) return false;
            if (Leggi("/sys/block/" + d + "/loop/autoclear") == "1") return false;   // «losetup -d» su un loop montato: e' come scollegarlo
            return Settori(d) > 0;
        }

        HashSet<string> Candidati()
        {
            var s = new HashSet<string>();
            try
            {
                foreach (var p in Directory.GetFileSystemEntries("/sys/block"))
                {
                    var d = Path.GetFileName(p);
                    if (Amministratore.Usb(d) && Settori(d) > 0) s.Add(d);
                    else if (prova && LoopDiProva(d)) s.Add(d);
                }
            }
            catch { }
            return s;
        }

        public string Attiva(bool prova, Registro log)
        {
            this.prova = prova;
            this.log = log;
            var ora = Candidati();
            lock (chiave)
            {
                iniziali = new HashSet<string>(ora);
                esterni = new HashSet<string>(ora);
                preesistenti = ora.Where(d => !d.StartsWith("loop") && Rimovibile(d)).OrderBy(d => d).ToList();
            }
            sentinella = new Thread(Guarda) { IsBackground = true, Name = "dischi" };
            try
            {
                amm = Utente.Radice ? (IAmministratore)new AmministratoreLocale() : new AmministratoreRemoto(Scrivi);
                var r = amm.Chiedi(new Dictionary<string, string> { { "cmd", "avvia" }, { "uid", Utente.Uid.ToString() }, { "gid", Utente.Gid.ToString() }, { "prova", prova ? "true" : "false" } }, 300000);
                if (r.GetValueOrDefault("ok") != "true") { var e = r.GetValueOrDefault("errore", "errore sconosciuto"); amm.Chiudi(); amm = null; throw new Exception(e); }
                attivo = true;
                return "regola udev " + r.GetValueOrDefault("regola", Amministratore.Regola) + ": niente montaggio automatico e blockdev --setro per i dischi USB" +
                       (prova ? " e i dispositivi loop" : "") + (Utente.Radice ? "" : "; aiutante avviato con pkexec");
            }
            finally { sentinella.Start(); }
        }

        // ------------------------------------------------------------------ sentinella dei dischi

        void Guarda()
        {
            while (!fine)
            {
                try { Giro(); } catch (Exception e) { Programma.Annota(e); }
                for (int i = 0; i < 5 && !fine; i++) Thread.Sleep(100);
            }
        }

        void Giro()
        {
            var ora = Candidati();
            var montaggi = Montaggi();
            List<string> nuovi = new List<string>();
            List<(string disco, Disco d)> spariti;
            lock (chiave)
            {
                esterni = ora;
                iniziali.IntersectWith(ora);          // un disco preesistente tolto e ricollegato diventa nuovo
                spariti = visti.Where(kv => !ora.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();
                foreach (var s in spariti) visti.Remove(s.disco);
                foreach (var d in ora)
                {
                    if (iniziali.Contains(d)) continue;
                    if (!visti.ContainsKey(d)) { visti[d] = new Disco { Primo = DateTime.Now, PrimaRiga = log != null ? log.Conteggio : -1 }; nuovi.Add(d); }
                }
                // un volume smontato da altri non e' piu' pronto
                foreach (var v in pronti.Values.ToList())
                    if (!montaggi.Any(m => m.punto == v.Radice)) { pronti.Remove(v.Id); Scrivi("Il volume " + v.Dispositivo + " non è più montato."); }
            }
            foreach (var d in nuovi) Scrivi("Comparso /dev/" + d + " (" + (d.StartsWith("loop") ? "dispositivo loop, " + Leggi("/sys/block/" + d + "/loop/backing_file") : "USB") + ").");
            foreach (var s in spariti)
            {
                Scrivi("Scomparso /dev/" + s.disco + ".");
                if (s.d.Volume != null)
                {
                    lock (chiave) pronti.Remove(s.d.Volume);
                    if (amm != null) amm.Chiedi(new Dictionary<string, string> { { "cmd", "smonta" }, { "volume", s.d.Volume } }, 60000);
                }
            }

            List<string> daMontare;
            lock (chiave)
                daMontare = visti.Where(kv => !kv.Value.Tentato && (DateTime.Now - kv.Value.Primo).TotalMilliseconds >= 1200).Select(kv => kv.Key).ToList();
            foreach (var d in daMontare)
            {
                if (fine) return;
                if (!attivo)
                {
                    // blocco non attivo: si usano i volumi montati da altri (es. dal desktop), segnalandoli
                    var m = montaggi.FirstOrDefault(x => x.sorgente.StartsWith("/dev/" + d));
                    if (m.punto == null) continue;
                    var v0 = new Volume { Id = Path.GetFileName(m.sorgente), Disco = d, Dispositivo = m.sorgente, Radice = m.punto, DaRepertum = false, Montato = DateTime.Now, Opzioni = m.opzioni, PrimaRiga = visti[d].PrimaRiga };
                    v0.Info = Descrizione(v0, new Dictionary<string, string> { { "tipo", m.tipo } });
                    lock (chiave) { visti[d].Tentato = true; visti[d].Volume = v0.Id; pronti[v0.Id] = v0; }
                    Scrivi("ATTENZIONE: " + m.sorgente + " è montato da altri in " + m.punto + " (" + m.opzioni + ").");
                    continue;
                }
                lock (chiave) visti[d].Tentato = true;
                Scrivi("Montaggio in sola lettura di /dev/" + d + ".");
                var r = amm.Chiedi(new Dictionary<string, string> { { "cmd", "monta" }, { "disco", d } }, 180000);
                if (r.GetValueOrDefault("ok") != "true")
                {
                    var p = "La chiavetta /dev/" + d + " non si può montare in sola lettura: " + r.GetValueOrDefault("errore", "errore sconosciuto") + ".";
                    lock (chiave) problema = p;
                    Scrivi(p);
                    continue;
                }
                var id = r["volume"];
                int prima;
                lock (chiave) prima = visti.ContainsKey(d) ? visti[d].PrimaRiga : -1;
                var v = new Volume { Id = id, Disco = d, Dispositivo = "/dev/" + id, Radice = r["punto"], Opzioni = r["opzioni"], DaRepertum = true, Montato = DateTime.Now, PrimaRiga = prima };
                v.Info = Descrizione(v, r);
                lock (chiave)
                {
                    Disco x;
                    if (visti.TryGetValue(d, out x)) x.Volume = id;
                    pronti[id] = v;
                    problema = null;
                }
                Scrivi("Volume " + v.Dispositivo + " montato da Repertum in " + v.Radice + " in sola lettura (" + v.Opzioni + ").");
            }
        }

        // /proc/self/mountinfo: punto di montaggio, sorgente, tipo, opzioni.
        static List<(string punto, string sorgente, string tipo, string opzioni)> Montaggi()
        {
            var l = new List<(string, string, string, string)>();
            try
            {
                foreach (var r in File.ReadAllLines("/proc/self/mountinfo"))
                {
                    var c = r.Split(' ');
                    int t = Array.IndexOf(c, "-");
                    if (c.Length < 7 || t < 0 || t + 2 >= c.Length) continue;
                    l.Add((Sblocca(c[4]), Sblocca(c[t + 2]), c[t + 1], c[5]));
                }
            }
            catch { }
            return l;
        }

        static string Sblocca(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 3 < s.Length && char.IsDigit(s[i + 1]))
                {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 3), 8));
                    i += 3;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // Proprieta' di udev del disco (lettura libera in /run/udev/data).
        static Dictionary<string, string> Udev(string disco)
        {
            var d = new Dictionary<string, string>();
            var n = Leggi("/sys/block/" + disco + "/dev");
            if (n == null) return d;
            try
            {
                foreach (var r in File.ReadAllLines("/run/udev/data/b" + n))
                {
                    if (!r.StartsWith("E:")) continue;
                    int i = r.IndexOf('=');
                    if (i > 2) d[r.Substring(2, i - 2)] = r.Substring(i + 1);
                }
            }
            catch { }
            return d;
        }

        static string Decodifica(string s)
        {
            // udev scrive gli spazi come \x20 nei valori *_ENC
            if (s == null) return null;
            return System.Text.RegularExpressions.Regex.Replace(s, @"\\x([0-9a-fA-F]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString()).Trim();
        }

        InfoSupporto Descrizione(Volume v, Dictionary<string, string> r)
        {
            var u = Udev(v.Disco);
            string x;
            var i = new InfoSupporto { Lettera = v.Dispositivo, Montaggio = v.Radice };
            var produttore = Decodifica(u.GetValueOrDefault("ID_VENDOR_ENC")) ?? (u.TryGetValue("ID_VENDOR", out x) ? x.Replace('_', ' ') : Leggi("/sys/block/" + v.Disco + "/device/vendor"));
            var modello = Decodifica(u.GetValueOrDefault("ID_MODEL_ENC")) ?? (u.TryGetValue("ID_MODEL", out x) ? x.Replace('_', ' ') : Leggi("/sys/block/" + v.Disco + "/device/model"));
            i.Modello = ((produttore ?? "") + " " + (modello ?? "")).Trim();
            if (v.Disco.StartsWith("loop")) i.Modello = "Dispositivo loop (" + Path.GetFileName(Leggi("/sys/block/" + v.Disco + "/loop/backing_file") ?? "") + ")";
            i.Seriale = u.GetValueOrDefault("ID_SERIAL_SHORT");
            if (u.ContainsKey("ID_VENDOR_ID") && u.ContainsKey("ID_MODEL_ID")) i.VidPid = u["ID_VENDOR_ID"].ToUpperInvariant() + ":" + u["ID_MODEL_ID"].ToUpperInvariant();
            i.Interfaccia = u.TryGetValue("ID_BUS", out x) ? x.ToUpperInvariant() : v.Disco.StartsWith("loop") ? "loop" : null;
            i.CapacitaDispositivo = Settori(v.Disco) * 512;
            i.Etichetta = r.GetValueOrDefault("etichetta");
            if (string.IsNullOrEmpty(i.Etichetta)) i.Etichetta = null;
            var tipo = r.GetValueOrDefault("tipo");
            var ver = r.GetValueOrDefault("versione");
            i.FileSystem = string.IsNullOrEmpty(ver) || ver == tipo ? tipo : tipo + " (" + ver + ")";
            i.SerialeVolume = r.GetValueOrDefault("uuid");
            i.PnpId = u.GetValueOrDefault("ID_PATH");
            if (string.IsNullOrEmpty(i.PnpId))
                try { var t = new DirectoryInfo("/sys/block/" + v.Disco).ResolveLinkTarget(true); i.PnpId = t != null ? t.FullName : null; } catch { }
            return i;
        }

        // ------------------------------------------------------------------ per l'interfaccia

        public List<string> Preesistenti() { lock (chiave) return preesistenti.ToList(); }

        public HashSet<string> Esterni() { lock (chiave) return new HashSet<string>(esterni); }

        public Dictionary<string, Volume> Pronti() { lock (chiave) return new Dictionary<string, Volume>(pronti); }

        public InfoSupporto Descrivi(Volume v)
        {
            var i = v.Info;
            var c = new InfoSupporto
            {
                Lettera = i.Lettera, Montaggio = i.Montaggio, Modello = i.Modello, Seriale = i.Seriale, VidPid = i.VidPid, Interfaccia = i.Interfaccia,
                CapacitaDispositivo = i.CapacitaDispositivo, Etichetta = i.Etichetta, FileSystem = i.FileSystem, SerialeVolume = i.SerialeVolume, PnpId = i.PnpId
            };
            ulong flag, fr, bl, li;
            if (Linux.StatVfs(v.Radice, out flag, out fr, out bl, out li))
            {
                c.CapacitaVolume = (long)(bl * fr);
                c.Occupato = (long)((bl - li) * fr);
            }
            return c;
        }

        public Protezione Controlla(Volume v)
        {
            var motivi = new List<string>();
            var parte = v.Id != v.Disco ? v.Id : null;
            bool roDisco = Leggi("/sys/block/" + v.Disco + "/ro") == "1";
            bool roParte = parte == null || Leggi("/sys/block/" + v.Disco + "/" + parte + "/ro") == "1";
            ulong flag;
            if (!Linux.StatVfs(v.Radice, out flag))
                return new Protezione { Protetto = false, Verificata = false, Dettaglio = "il volume non ha risposto al controllo" };
            var m = Montaggi().LastOrDefault(x => x.punto == v.Radice);
            if (m.sorgente != null && m.sorgente != v.Dispositivo)
                return new Protezione { Protetto = false, Verificata = false, Dettaglio = "in " + v.Radice + " è montato " + m.sorgente + ", non " + v.Dispositivo };
            if (roDisco && roParte) motivi.Add("il disco è in sola lettura per il kernel (/sys/block/" + v.Disco + "/ro = 1" + (parte != null ? ", anche la partizione " + parte : "") + ")");
            if ((flag & 1) != 0) motivi.Add("il volume è montato in sola lettura (ST_RDONLY" + (v.DaRepertum ? ", opzioni " + v.Opzioni : "") + ")");
            if (motivi.Count > 0) return new Protezione { Protetto = true, Verificata = true, Dettaglio = string.Join("; ", motivi) };
            return new Protezione { Protetto = false, Verificata = true, Dettaglio = "il disco e il volume accettano scritture" };
        }

        public List<string> RigheProtezione(DateTime oraBlocco, DateTime oraCollegamento, Volume v)
        {
            return new List<string>
            {
                "Regola udev di Repertum attiva dalle " + Formati.Data(oraBlocco) + ", prima del collegamento",
                "della chiavetta (" + Formati.Data(oraCollegamento) + "): disco in sola lettura per il kernel",
                "(blockdev --setro), niente montaggio automatico; volume montato da Repertum in",
                "sola lettura (" + v.Opzioni + ")."
            };
        }

        public void Chiudi(Registro registro)
        {
            if (registro != null) log = registro;
            fine = true;
            if (sentinella != null && sentinella.IsAlive && Thread.CurrentThread != sentinella) sentinella.Join(5000);
            var a = amm;
            amm = null;
            if (a != null)
            {
                a.Chiudi();
                Scrivi("Volumi smontati, regola udev " + Amministratore.Regola + " rimossa.");
            }
            attivo = false;
        }

        public void Dispose() { Chiudi(null); }
    }
}
