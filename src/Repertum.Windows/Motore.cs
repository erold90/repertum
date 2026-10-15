// Repertum per Windows - la parte di sistema: supporti, protezione da scrittura (criterio
// StorageDevicePolicies), controllo della sola lettura. Hash, allegati e verifica stanno nel Core
// (src/Repertum.Core), comune alle versioni per Windows, macOS e Linux.

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

    // ------------------------------------------------------------------ supporti

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
}
