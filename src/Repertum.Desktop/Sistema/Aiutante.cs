// Repertum per Linux - l'aiutante con i privilegi di root. E' lo stesso eseguibile avviato con
// «pkexec Repertum --helper» una sola volta all'apertura; parla con l'interfaccia a righe JSON su
// stdin/stdout. Se l'interfaccia muore stdin si chiude e l'aiutante rimette tutto com'era.
//
// Cosa fa, e niente altro:
//  - installa /run/udev/rules.d/00-repertum.rules: i dischi USB collegati da adesso non si montano
//    da soli (UDISKS_IGNORE, UDISKS_AUTO=0) e diventano subito di sola lettura (blockdev --setro);
//  - monta in sola lettura, sotto /run/repertum, il volume della chiavetta che l'interfaccia indica;
//  - alla fine smonta, cancella la regola e ricarica udev.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Repertum
{
    public static class Aiutante
    {
        [DllImport("libc", SetLastError = true)] static extern uint geteuid();

        public static int Esegui()
        {
            if (geteuid() != 0) { Console.Error.WriteLine("Repertum --helper: servono i privilegi di root."); return 2; }
            var a = new Amministratore();
            Action fine = () => { try { a.Fine(); } catch { } };
            var segnali = new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP, PosixSignal.SIGQUIT }
                .Select(s => PosixSignalRegistration.Create(s, c => { c.Cancel = true; fine(); Environment.Exit(0); })).ToList();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => fine();
            var uscita = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var entrata = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            string riga;
            while ((riga = entrata.ReadLine()) != null)
            {
                if (riga.Trim().Length == 0) continue;
                Dictionary<string, string> risposta;
                string cmd = "";
                try
                {
                    var r = Json.Leggi(riga);
                    r.TryGetValue("cmd", out cmd);
                    switch (cmd)
                    {
                        case "avvia": risposta = a.Avvia(uint.Parse(r["uid"]), uint.Parse(r["gid"]), r.ContainsKey("prova") && r["prova"] == "true"); break;
                        case "monta": risposta = a.Monta(r["disco"]); break;
                        case "smonta": risposta = a.Smonta(r["volume"]); break;
                        case "fine": a.Fine(); risposta = Amministratore.Ok(); break;
                        default: risposta = Amministratore.Errore("comando sconosciuto"); break;
                    }
                }
                catch (Exception e) { risposta = Amministratore.Errore(e.Message); }
                uscita.WriteLine(Json.Scrivi(risposta));
                if (cmd == "fine") break;
            }
            fine();
            foreach (var s in segnali) s.Dispose();
            return 0;
        }
    }

    public static class Json
    {
        public static Dictionary<string, string> Leggi(string riga)
        {
            var d = new Dictionary<string, string>();
            using (var doc = JsonDocument.Parse(riga))
                foreach (var p in doc.RootElement.EnumerateObject())
                    d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
            return d;
        }

        public static string Scrivi(Dictionary<string, string> d)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    foreach (var kv in d) w.WriteString(kv.Key, kv.Value ?? "");
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    // Le operazioni da root. Le usa l'aiutante (pkexec) oppure, se l'interfaccia e' gia' root, l'interfaccia stessa.
    public sealed class Amministratore
    {
        public const string Regola = "/run/udev/rules.d/00-repertum.rules";
        public const string Base = "/run/repertum";
        uint uid, gid;
        bool prova, avviato;
        readonly Dictionary<string, string> montati = new Dictionary<string, string>();
        readonly object chiave = new object();

        public static Dictionary<string, string> Ok() { return new Dictionary<string, string> { { "ok", "true" } }; }
        public static Dictionary<string, string> Errore(string m) { return new Dictionary<string, string> { { "ok", "false" }, { "errore", m } }; }

        static string Programma(string nome)
        {
            foreach (var d in new[] { "/usr/sbin", "/sbin", "/usr/bin", "/bin" })
            {
                var p = Path.Combine(d, nome);
                if (File.Exists(p)) return p;
            }
            return nome;
        }

        // Esegue un programma senza shell; restituisce il codice d'uscita e quello che ha scritto.
        public static int Esegui(string programma, out string testo, int ms, params string[] argomenti)
        {
            var psi = new ProcessStartInfo(Programma(programma)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in argomenti) psi.ArgumentList.Add(a);
            psi.Environment["LC_ALL"] = "C";
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(true); } catch { } testo = "tempo scaduto"; return -1; }
                p.WaitForExit();
                testo = (o.Result + e.Result).Trim();
                return p.ExitCode;
            }
        }

        static int Esegui(string programma, params string[] argomenti) { string t; return Esegui(programma, out t, 30000, argomenti); }

        public Dictionary<string, string> Avvia(uint uid, uint gid, bool prova)
        {
            lock (chiave)
            {
                this.uid = uid; this.gid = gid; this.prova = prova;
                Directory.CreateDirectory(Path.GetDirectoryName(Regola));
                var blockdev = Programma("blockdev");
                var sb = new StringBuilder();
                sb.Append("# Repertum: regola temporanea, la toglie Repertum alla chiusura (e sparisce al riavvio).\n");
                sb.Append("# I dischi USB collegati mentre Repertum e' aperto non si montano da soli e restano in sola lettura.\n");
                sb.Append("ACTION==\"remove\", GOTO=\"repertum_fine\"\n");
                sb.Append("SUBSYSTEM!=\"block\", GOTO=\"repertum_fine\"\n");
                sb.Append("SUBSYSTEMS==\"usb\", GOTO=\"repertum_blocca\"\n");
                sb.Append("ENV{ID_BUS}==\"usb\", GOTO=\"repertum_blocca\"\n");
                if (prova) sb.Append("KERNEL==\"loop*\", GOTO=\"repertum_blocca\"\n");
                sb.Append("GOTO=\"repertum_fine\"\n");
                sb.Append("LABEL=\"repertum_blocca\"\n");
                sb.Append("ENV{UDISKS_IGNORE}=\"1\", ENV{UDISKS_AUTO}=\"0\"\n");
                sb.Append("RUN+=\"" + blockdev + " --setro $devnode\"\n");
                sb.Append("LABEL=\"repertum_fine\"\n");
                File.WriteAllText(Regola, sb.ToString(), new UTF8Encoding(false));
                avviato = true;
                string t;
                int r = Esegui("udevadm", out t, 30000, "control", "--reload");
                if (r != 0) return Errore("udevadm control --reload: " + t);
                Directory.CreateDirectory(Base);
                File.SetUnixFileMode(Base, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                var d = Ok();
                d["regola"] = Regola;
                return d;
            }
        }

        static string Leggi(string f) { try { return File.ReadAllText(f).Trim(); } catch { return null; } }

        public static bool Usb(string disco)
        {
            try
            {
                var t = new DirectoryInfo("/sys/block/" + disco).ResolveLinkTarget(true);
                return t != null && t.FullName.Contains("/usb");
            }
            catch { return false; }
        }

        static List<string> Partizioni(string disco)
        {
            var l = new List<string>();
            try
            {
                foreach (var d in Directory.GetDirectories("/sys/block/" + disco))
                {
                    var n = Path.GetFileName(d);
                    if (n.StartsWith(disco) && File.Exists(Path.Combine(d, "partition"))) l.Add(n);
                }
            }
            catch { }
            return l.OrderBy(n => { int k; return int.TryParse(Leggi("/sys/block/" + disco + "/" + n + "/partition"), out k) ? k : 999; }).ToList();
        }

        static Dictionary<string, string> Blkid(string dev)
        {
            var d = new Dictionary<string, string>();
            string t;
            if (Esegui("blkid", out t, 30000, "-p", "-o", "export", dev) != 0) return d;
            foreach (var r in t.Split('\n'))
            {
                int i = r.IndexOf('=');
                if (i > 0) d[r.Substring(0, i).Trim()] = r.Substring(i + 1).Trim();
            }
            return d;
        }

        List<(string tipo, string opzioni)> Modi(string tipo)
        {
            string u = ",uid=" + uid + ",gid=" + gid;
            const string sicuro = ",nosuid,nodev,noexec";
            switch (tipo)
            {
                case "vfat": case "msdos": return new List<(string, string)> { ("vfat", "ro" + sicuro + u + ",utf8,shortname=mixed"), ("vfat", "ro" + sicuro + u) };
                case "exfat": return new List<(string, string)> { ("exfat", "ro" + sicuro + u + ",iocharset=utf8"), ("exfat", "ro" + sicuro + u) };
                case "ntfs": case "ntfs3": return new List<(string, string)> { ("ntfs3", "ro" + sicuro + u + ",iocharset=utf8"), ("ntfs", "ro" + sicuro + u) };
                case "hfsplus": case "hfs": return new List<(string, string)> { (tipo, "ro" + sicuro + u) };
                case "iso9660": case "udf": return new List<(string, string)> { (tipo, "ro" + sicuro + u) };
                case "ext2": case "ext3": case "ext4": return new List<(string, string)> { ("ext4", "ro,noload" + sicuro), (tipo, "ro" + sicuro) };
                case "xfs": return new List<(string, string)> { ("xfs", "ro,norecovery" + sicuro) };
                case "btrfs": return new List<(string, string)> { ("btrfs", "ro,rescue=nologreplay" + sicuro), ("btrfs", "ro,nologreplay" + sicuro) };
                case "f2fs": return new List<(string, string)> { ("f2fs", "ro,norecovery" + sicuro) };
                default: return new List<(string, string)> { (tipo, "ro" + sicuro) };
            }
        }

        public Dictionary<string, string> Monta(string disco)
        {
            lock (chiave)
            {
                if (!avviato) return Errore("aiutante non avviato");
                if (!Regex.IsMatch(disco ?? "", "^[a-z][a-z0-9]{1,31}$") || !Directory.Exists("/sys/block/" + disco)) return Errore("disco sconosciuto: " + disco);
                bool loop = disco.StartsWith("loop");
                if (!(Usb(disco) || (prova && loop))) return Errore("/dev/" + disco + " non è un supporto USB");
                string t;
                Esegui("udevadm", out t, 15000, "settle", "--timeout=10");
                var parti = Partizioni(disco);
                // prima di tutto la sola lettura del dispositivo, poi il resto
                foreach (var d in new[] { disco }.Concat(parti))
                {
                    if (Esegui("blockdev", out t, 15000, "--setro", "/dev/" + d) != 0) return Errore("blockdev --setro /dev/" + d + ": " + t);
                }
                if (Leggi("/sys/block/" + disco + "/ro") != "1") return Errore("il kernel non conferma la sola lettura di /dev/" + disco);
                var errori = new List<string>();
                foreach (var v in parti.Count > 0 ? parti.Concat(new[] { disco }) : new[] { disco })
                {
                    var b = Blkid("/dev/" + v);
                    string tipo;
                    if (!b.TryGetValue("TYPE", out tipo) || tipo.Length == 0) continue;
                    if (tipo == "swap" || tipo.StartsWith("crypto_") || tipo == "LVM2_member" || tipo.EndsWith("_raid_member")) { errori.Add(tipo + " non supportato"); continue; }
                    var punto = Path.Combine(Base, v);
                    Directory.CreateDirectory(punto);
                    foreach (var m in Modi(tipo))
                    {
                        if (Esegui("mount", out t, 60000, "-t", m.tipo, "-o", m.opzioni, "/dev/" + v, punto) != 0) { errori.Add("mount -t " + m.tipo + ": " + t); continue; }
                        if (!SolaLettura(punto))
                        {
                            Esegui("umount", out t, 30000, punto);
                            errori.Add("il volume risultava montato in scrittura");
                            continue;
                        }
                        montati[v] = punto;
                        var d = Ok();
                        d["volume"] = v;
                        d["punto"] = punto;
                        d["tipo"] = tipo;
                        d["versione"] = b.ContainsKey("VERSION") ? b["VERSION"] : "";
                        d["opzioni"] = m.opzioni;
                        d["etichetta"] = b.ContainsKey("LABEL") ? b["LABEL"] : "";
                        d["uuid"] = b.ContainsKey("UUID") ? b["UUID"] : "";
                        return d;
                    }
                    try { Directory.Delete(punto); } catch { }
                }
                return Errore(errori.Count > 0 ? string.Join("; ", errori) : "nessun file system riconosciuto su /dev/" + disco);
            }
        }

        public static bool SolaLettura(string punto)
        {
            ulong flag;
            return Linux.StatVfs(punto, out flag) && (flag & 1) != 0;   // ST_RDONLY
        }

        public Dictionary<string, string> Smonta(string volume)
        {
            lock (chiave)
            {
                string punto;
                if (!montati.TryGetValue(volume ?? "", out punto)) return Ok();
                Togli(punto);
                montati.Remove(volume);
                return Ok();
            }
        }

        static void Togli(string punto)
        {
            string t;
            if (Esegui("umount", out t, 30000, punto) != 0) Esegui("umount", out t, 30000, "-l", punto);
            try { Directory.Delete(punto); } catch { }
        }

        public void Fine()
        {
            lock (chiave)
            {
                foreach (var p in montati.Values.ToList()) Togli(p);
                montati.Clear();
                if (!avviato) return;
                avviato = false;
                try { File.Delete(Regola); } catch { }
                string t;
                Esegui("udevadm", out t, 30000, "control", "--reload");
                try { if (Directory.Exists(Base) && !Directory.EnumerateFileSystemEntries(Base).Any()) Directory.Delete(Base); } catch { }
            }
        }
    }

    public static unsafe class Linux
    {
        [DllImport("libc", SetLastError = true)] static extern int statvfs(string percorso, byte* buf);

        public static bool StatVfs(string percorso, out ulong flag)
        {
            ulong a, b, c;
            return StatVfs(percorso, out flag, out a, out b, out c);
        }

        // struct statvfs di glibc a 64 bit: f_bsize 0, f_frsize 8, f_blocks 16, f_bfree 24, f_flag 72
        public static bool StatVfs(string percorso, out ulong flag, out ulong frammento, out ulong blocchi, out ulong liberi)
        {
            var buf = stackalloc byte[512];
            flag = frammento = blocchi = liberi = 0;
            if (statvfs(percorso, buf) != 0) return false;
            frammento = *(ulong*)(buf + 8);
            blocchi = *(ulong*)(buf + 16);
            liberi = *(ulong*)(buf + 24);
            flag = *(ulong*)(buf + 72);
            return true;
        }
    }
}
