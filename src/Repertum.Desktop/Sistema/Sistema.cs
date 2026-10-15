// Repertum per macOS e Linux - cio' che l'interfaccia chiede al sistema: blocco della scrittura,
// supporti collegati, volumi montati in sola lettura, descrizione e controllo della protezione.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Repertum
{
    // Un volume del supporto, pronto da leggere.
    public class Volume
    {
        public string Id;            // "disk4s1" (macOS), "sdb1" (Linux)
        public string Disco;         // "disk4", "sdb"
        public string Dispositivo;   // "/dev/disk4s1"
        public string Radice;        // punto di montaggio
        public string Opzioni;       // opzioni del montaggio in sola lettura
        public bool DaRepertum;      // false: montato da altri (blocco non attivo)
        public DateTime Montato;
        public int PrimaRiga = -1;  // prima riga del registro che riguarda questo supporto (per il LOG.txt)
        public InfoSupporto Info = new InfoSupporto();
    }

    public interface ISistema : IDisposable
    {
        string Nome { get; }                 // "macOS", "Linux"
        string Computer { get; }             // "il Mac", "il computer"
        bool Attivo { get; }                 // blocco della scrittura attivo

        // Attiva il blocco (puo' attendere la password dell'amministratore). Restituisce la descrizione
        // per il registro; se non riesce lancia un'eccezione con il motivo.
        string Attiva(bool prova, Registro log);

        // Dischi esterni rimovibili presenti all'avvio (da far scollegare) e quelli presenti adesso.
        List<string> Preesistenti();
        HashSet<string> Esterni();

        // Volumi pronti da leggere, per Id.
        Dictionary<string, Volume> Pronti();

        // Ultimo problema con un supporto appena collegato (es. file system non riconosciuto).
        string UltimoProblema { get; }

        InfoSupporto Descrivi(Volume v);
        Protezione Controlla(Volume v);
        List<string> RigheProtezione(DateTime oraBlocco, DateTime oraCollegamento, Volume v);

        // Toglie il blocco, smonta quello che ha montato. Si puo' chiamare piu' volte.
        void Chiudi(Registro log);
    }

    public static class Sistemi
    {
        public static ISistema Crea()
        {
            if (OperatingSystem.IsMacOS()) return new SistemaMac();
            return new SistemaLinux();
        }

        // Cartella predefinita delle acquisizioni: ~/Repertum, nella casa dell'utente vero anche se
        // il programma e' stato avviato con sudo.
        public static string Casa()
        {
            if (OperatingSystem.IsLinux() && Utente.Radice && Utente.Nome != null)
            {
                var h = Utente.CasaDi(Utente.Nome);
                if (h != null) return h;
            }
            var c = Environment.GetEnvironmentVariable("HOME");
            return string.IsNullOrEmpty(c) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : c;
        }

        public static string Impostazioni()
        {
            var casa = Casa();
            if (OperatingSystem.IsMacOS()) return Path.Combine(casa, "Library", "Application Support", "Repertum", "impostazioni.txt");
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(string.IsNullOrEmpty(xdg) || Utente.Radice ? Path.Combine(casa, ".config") : xdg, "repertum", "impostazioni.txt");
        }

        public static void ApriCartella(string c)
        {
            try
            {
                Directory.CreateDirectory(c);
                var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(c);
                Process.Start(psi);
            }
            catch { }
        }
    }

    // Chi usa il programma: serve su Linux per montare i volumi a nome suo e, se si e' partiti con sudo,
    // per restituirgli la proprieta' delle cartelle salvate.
    public static class Utente
    {
        [DllImport("libc", SetLastError = true)] static extern uint geteuid();
        [DllImport("libc", SetLastError = true)] static extern uint getuid();
        [DllImport("libc", SetLastError = true)] static extern uint getgid();
        [DllImport("libc", SetLastError = true)] static extern int lchown(string percorso, uint uid, uint gid);

        public static readonly bool Radice = geteuid() == 0;
        public static readonly uint Uid = Radice ? (Numero("SUDO_UID") ?? Numero("PKEXEC_UID") ?? 0u) : getuid();
        public static readonly uint Gid = Radice ? (Numero("SUDO_GID") ?? Gruppo(Uid) ?? 0u) : getgid();
        public static readonly string Nome = Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName;

        static uint? Numero(string variabile)
        {
            uint n;
            var v = Environment.GetEnvironmentVariable(variabile);
            return !string.IsNullOrEmpty(v) && uint.TryParse(v, out n) ? n : (uint?)null;
        }

        static string[] Riga(Func<string[], bool> criterio)
        {
            try
            {
                foreach (var r in File.ReadAllLines("/etc/passwd"))
                {
                    var c = r.Split(':');
                    if (c.Length >= 7 && criterio(c)) return c;
                }
            }
            catch { }
            return null;
        }

        static uint? Gruppo(uint uid)
        {
            uint g;
            var c = Riga(x => x[2] == uid.ToString());
            return c != null && uint.TryParse(c[3], out g) ? g : (uint?)null;
        }

        public static string CasaDi(string nome)
        {
            var c = Riga(x => x[0] == nome);
            return c != null ? c[5] : null;
        }

        // Se il programma gira come root per conto di un utente, le cartelle create tornano sue.
        public static void Restituisci(string percorso)
        {
            if (!Radice || Uid == 0) return;
            try
            {
                lchown(percorso, Uid, Gid);
                if (Directory.Exists(percorso))
                    foreach (var f in Directory.EnumerateFileSystemEntries(percorso, "*", SearchOption.AllDirectories)) lchown(f, Uid, Gid);
            }
            catch { }
        }
    }
}
