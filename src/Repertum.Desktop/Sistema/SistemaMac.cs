// Repertum per macOS - blocco della scrittura con DiskArbitration, senza privilegi di amministratore,
// come l'app open source «Disk Arbitrator»:
//  1. un callback di approvazione rifiuta il montaggio automatico dei dischi esterni comparsi dopo l'avvio;
//  2. Repertum monta lui stesso i volumi con DADiskMountWithArguments e le opzioni rdonly, noowners,
//     lasciando passare solo i montaggi chiesti da lui;
//  3. statfs (MNT_RDONLY) sul punto di montaggio conferma la sola lettura.
// La sessione DiskArbitration vive su un CFRunLoop in un filo dedicato. Se il programma muore,
// diskarbitrationd toglie da solo i callback: il Mac torna come prima.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Repertum
{
    static unsafe class Mac
    {
        const string DAF = "/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration";
        const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string IOK = "/System/Library/Frameworks/IOKit.framework/IOKit";
        const string LibC = "/usr/lib/libSystem.dylib";

        [DllImport(DAF)] public static extern IntPtr DASessionCreate(IntPtr allocator);
        [DllImport(DAF)] public static extern void DASessionScheduleWithRunLoop(IntPtr session, IntPtr runLoop, IntPtr mode);
        [DllImport(DAF)] public static extern void DASessionUnscheduleFromRunLoop(IntPtr session, IntPtr runLoop, IntPtr mode);
        [DllImport(DAF)] public static extern void DARegisterDiskAppearedCallback(IntPtr session, IntPtr match, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern void DARegisterDiskDisappearedCallback(IntPtr session, IntPtr match, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern void DARegisterDiskDescriptionChangedCallback(IntPtr session, IntPtr match, IntPtr watch, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern void DARegisterDiskMountApprovalCallback(IntPtr session, IntPtr match, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern void DAUnregisterCallback(IntPtr session, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern void DAUnregisterApprovalCallback(IntPtr session, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern IntPtr DADiskCopyDescription(IntPtr disk);
        [DllImport(DAF)] public static extern IntPtr DADiskGetBSDName(IntPtr disk);
        [DllImport(DAF)] public static extern IntPtr DADiskCreateFromBSDName(IntPtr allocator, IntPtr session, string name);
        [DllImport(DAF)] public static extern IntPtr DADiskCopyWholeDisk(IntPtr disk);
        [DllImport(DAF)] public static extern uint DADiskCopyIOMedia(IntPtr disk);
        [DllImport(DAF)] public static extern void DADiskMountWithArguments(IntPtr disk, IntPtr path, uint options, IntPtr callback, IntPtr context, IntPtr[] arguments);
        [DllImport(DAF)] public static extern void DADiskUnmount(IntPtr disk, uint options, IntPtr callback, IntPtr context);
        [DllImport(DAF)] public static extern IntPtr DADissenterCreate(IntPtr allocator, int status, IntPtr text);
        [DllImport(DAF)] public static extern int DADissenterGetStatus(IntPtr dissenter);
        [DllImport(DAF)] public static extern IntPtr DADissenterGetStatusString(IntPtr dissenter);

        [DllImport(CF)] public static extern IntPtr CFRunLoopGetCurrent();
        [DllImport(CF)] public static extern void CFRunLoopRun();
        [DllImport(CF)] public static extern void CFRunLoopStop(IntPtr rl);
        [DllImport(CF)] public static extern void CFRunLoopWakeUp(IntPtr rl);
        [DllImport(CF)] public static extern double CFAbsoluteTimeGetCurrent();
        [DllImport(CF)] public static extern IntPtr CFRunLoopTimerCreate(IntPtr alloc, double fireDate, double interval, ulong flags, IntPtr order, IntPtr callout, IntPtr context);
        [DllImport(CF)] public static extern void CFRunLoopAddTimer(IntPtr rl, IntPtr timer, IntPtr mode);
        [DllImport(CF)] public static extern void CFRunLoopTimerInvalidate(IntPtr timer);
        [DllImport(CF)] public static extern void CFRelease(IntPtr cf);
        [DllImport(CF)] public static extern IntPtr CFRetain(IntPtr cf);
        [DllImport(CF)] public static extern IntPtr CFGetTypeID(IntPtr cf);
        [DllImport(CF)] public static extern IntPtr CFStringGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFBooleanGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFNumberGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFURLGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFUUIDGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFDataGetTypeID();
        [DllImport(CF)] public static extern IntPtr CFStringGetLength(IntPtr s);
        [DllImport(CF)] public static extern void CFStringGetCharacters(IntPtr s, CFRange range, char* buffer);
        [DllImport(CF)] public static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, char* chars, IntPtr n);
        [DllImport(CF)] public static extern byte CFNumberGetValue(IntPtr n, int type, out long value);
        [DllImport(CF)] public static extern byte CFBooleanGetValue(IntPtr b);
        [DllImport(CF)] public static extern IntPtr CFURLCopyFileSystemPath(IntPtr url, int style);
        [DllImport(CF)] public static extern IntPtr CFUUIDCreateString(IntPtr alloc, IntPtr uuid);
        [DllImport(CF)] public static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
        [DllImport(CF)] public static extern IntPtr CFDataGetLength(IntPtr d);
        [DllImport(CF)] public static extern byte* CFDataGetBytePtr(IntPtr d);

        [DllImport(IOK)] public static extern IntPtr IORegistryEntrySearchCFProperty(uint entry, string plane, IntPtr key, IntPtr allocator, uint options);
        [DllImport(IOK)] public static extern int IOObjectRelease(uint obj);

        [DllImport(LibC, EntryPoint = "statfs$INODE64", SetLastError = true)] static extern int statfs_x64(string path, byte* buf);
        [DllImport(LibC, EntryPoint = "statfs", SetLastError = true)] static extern int statfs_arm64(string path, byte* buf);

        [StructLayout(LayoutKind.Sequential)]
        public struct CFRange { public IntPtr Inizio, Lunghezza; public CFRange(long a, long b) { Inizio = (IntPtr)a; Lunghezza = (IntPtr)b; } }

        public const int ReturnNotPermitted = unchecked((int)0xF8DA0008);
        public const uint MNT_RDONLY = 0x1;

        static readonly IntPtr libDA = NativeLibrary.Load(DAF);
        static readonly IntPtr libCF = NativeLibrary.Load(CF);

        public static IntPtr Costante(string nome, bool daCF = false)
        {
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(daCF ? libCF : libDA, nome));
        }

        public static bool Esiste(string funzione)
        {
            IntPtr p;
            return NativeLibrary.TryGetExport(libDA, funzione, out p);
        }

        public static IntPtr Stringa(string s)
        {
            fixed (char* c = s) return CFStringCreateWithCharacters(IntPtr.Zero, c, (IntPtr)s.Length);
        }

        public static string Testo(IntPtr cf)
        {
            if (cf == IntPtr.Zero || CFGetTypeID(cf) != CFStringGetTypeID()) return null;
            long n = (long)CFStringGetLength(cf);
            if (n <= 0) return "";
            var buf = new char[n];
            fixed (char* p = buf) CFStringGetCharacters(cf, new CFRange(0, n), p);
            return new string(buf);
        }

        public static string Valore(IntPtr dict, IntPtr chiave)
        {
            var v = CFDictionaryGetValue(dict, chiave);
            if (v == IntPtr.Zero) return null;
            var t = CFGetTypeID(v);
            if (t == CFStringGetTypeID()) return Testo(v);
            if (t == CFURLGetTypeID())
            {
                var s = CFURLCopyFileSystemPath(v, 0);
                try { return Testo(s); } finally { if (s != IntPtr.Zero) CFRelease(s); }
            }
            if (t == CFUUIDGetTypeID())
            {
                var s = CFUUIDCreateString(IntPtr.Zero, v);
                try { return Testo(s); } finally { if (s != IntPtr.Zero) CFRelease(s); }
            }
            return null;
        }

        public static bool? Booleano(IntPtr dict, IntPtr chiave)
        {
            var v = CFDictionaryGetValue(dict, chiave);
            if (v == IntPtr.Zero || CFGetTypeID(v) != CFBooleanGetTypeID()) return null;
            return CFBooleanGetValue(v) != 0;
        }

        public static long Numero(IntPtr cf)
        {
            long n;
            if (cf == IntPtr.Zero || CFGetTypeID(cf) != CFNumberGetTypeID()) return -1;
            return CFNumberGetValue(cf, 4, out n) != 0 ? n : -1;   // kCFNumberSInt64Type
        }

        // Una proprieta' cercata risalendo l'albero IOKit dal supporto (VID, PID, numero di serie USB).
        public static string Proprieta(uint media, string nome, bool esadecimale = false)
        {
            if (media == 0) return null;
            var k = Stringa(nome);
            try
            {
                var v = IORegistryEntrySearchCFProperty(media, "IOService", k, IntPtr.Zero, 3);   // ricorsivo, verso i genitori
                if (v == IntPtr.Zero) return null;
                try
                {
                    var t = CFGetTypeID(v);
                    if (t == CFStringGetTypeID()) return Testo(v);
                    if (t == CFNumberGetTypeID()) { var n = Numero(v); return n < 0 ? null : esadecimale ? n.ToString("X4") : n.ToString(); }
                    return null;
                }
                finally { CFRelease(v); }
            }
            finally { CFRelease(k); }
        }

        public class Fs { public uint Flag; public ulong Blocchi, Liberi; public uint Blocco; public string Tipo, Su, Da; }

        public static Fs StatFs(string percorso)
        {
            var buf = stackalloc byte[4096];
            int r = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? statfs_x64(percorso, buf) : statfs_arm64(percorso, buf);
            if (r != 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new Fs
            {
                Blocco = *(uint*)buf,
                Blocchi = *(ulong*)(buf + 8),
                Liberi = *(ulong*)(buf + 16),
                Flag = *(uint*)(buf + 64),
                Tipo = Marshal.PtrToStringUTF8((IntPtr)(buf + 72)),
                Su = Marshal.PtrToStringUTF8((IntPtr)(buf + 88)),
                Da = Marshal.PtrToStringUTF8((IntPtr)(buf + 1112))
            };
        }
    }

    public sealed class SistemaMac : ISistema
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DiscoCb(IntPtr disk, IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ApprovazioneCb(IntPtr disk, IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void EsitoCb(IntPtr disk, IntPtr dissenter, IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CambioCb(IntPtr disk, IntPtr chiavi, IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InattivoCb(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void TimerCb(IntPtr timer, IntPtr info);
        [DllImport("/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration")]
        static extern void DARegisterIdleCallback(IntPtr session, IntPtr callback, IntPtr context);

        sealed class Disco
        {
            public string Bsd, Intero, Protocollo, Tipo, Etichetta, Percorso;
            public bool Bersaglio, Immagine, Rimovibile, Montabile, Interno, Preesistente;
            public bool InAttesa, IdleVisto, Noowners = true, Rifiutato;
            public int Tentativi, PrimaRiga = -1;
            public DateTime Apparso;
        }

        // i delegate restano vivi quanto il processo: diskarbitrationd li chiama dal filo della sessione
        readonly DiscoCb cbApparso, cbScomparso;
        readonly ApprovazioneCb cbApprovazione;
        readonly EsitoCb cbMontato, cbSmontato;
        readonly CambioCb cbCambio;
        readonly InattivoCb cbInattivo;
        readonly TimerCb cbTimer;
        IntPtr pApparso, pScomparso, pApprovazione, pMontato, pSmontato, pCambio, pInattivo, pTimer;

        readonly object chiave = new object();
        readonly Dictionary<string, Disco> dischi = new Dictionary<string, Disco>();
        readonly Dictionary<string, Volume> pronti = new Dictionary<string, Volume>();
        readonly HashSet<string> preesistenti = new HashSet<string>();
        readonly ConcurrentQueue<Action> lavori = new ConcurrentQueue<Action>();
        readonly ManualResetEventSlim avviata = new ManualResetEventSlim(), enumerati = new ManualResetEventSlim(), fermata = new ManualResetEventSlim();
        Thread filo;
        IntPtr sessione, ciclo, timer, modo;
        IntPtr kVolumeKind, kVolumeMountable, kVolumeName, kVolumeNetwork, kVolumePath, kVolumeType, kVolumeUUID,
               kMediaBSDName, kMediaRemovable, kMediaWhole, kMediaWritable, kMediaSize, kDeviceInternal, kDeviceModel,
               kDevicePath, kDeviceProtocol, kDeviceVendor;
        bool prova, enumerazione = true, attivo, idle;
        string montaggioInCorso, problema, erroreAvvio;
        DateTime avvio;
        Registro log;
        int smontaggiInCorso;

        public SistemaMac()
        {
            cbApparso = Apparso; cbScomparso = Scomparso; cbApprovazione = Approvazione; cbMontato = Montato;
            cbSmontato = Smontato; cbCambio = Cambio; cbInattivo = Inattivo; cbTimer = Battito;
        }

        public string Nome { get { return "macOS"; } }
        public string Computer { get { return "il Mac"; } }
        public bool Attivo { get { return attivo; } }
        public string UltimoProblema { get { lock (chiave) return problema; } }

        void Scrivi(string r) { var l = log; if (l != null) try { l.Scrivi(r); } catch { } }

        public string Attiva(bool prova, Registro log)
        {
            this.prova = prova;
            this.log = log;
            avvio = DateTime.Now;
            filo = new Thread(Ciclo) { IsBackground = true, Name = "DiskArbitration" };
            filo.Start();
            if (!avviata.Wait(10000)) throw new Exception("DiskArbitration non risponde");
            if (erroreAvvio != null) throw new Exception(erroreAvvio);
            enumerati.Wait(6000);       // i dischi gia' collegati arrivano subito dopo la registrazione
            attivo = true;
            return "DiskArbitration: montaggio automatico dei dischi esterni bloccato, montaggio in sola lettura con rdonly, noowners";
        }

        void Ciclo()
        {
            try
            {
                ciclo = Mac.CFRetain(Mac.CFRunLoopGetCurrent());
                modo = Mac.Costante("kCFRunLoopDefaultMode", true);
                kVolumeKind = Mac.Costante("kDADiskDescriptionVolumeKindKey");
                kVolumeMountable = Mac.Costante("kDADiskDescriptionVolumeMountableKey");
                kVolumeName = Mac.Costante("kDADiskDescriptionVolumeNameKey");
                kVolumeNetwork = Mac.Costante("kDADiskDescriptionVolumeNetworkKey");
                kVolumePath = Mac.Costante("kDADiskDescriptionVolumePathKey");
                kVolumeType = Mac.Costante("kDADiskDescriptionVolumeTypeKey");
                kVolumeUUID = Mac.Costante("kDADiskDescriptionVolumeUUIDKey");
                kMediaBSDName = Mac.Costante("kDADiskDescriptionMediaBSDNameKey");
                kMediaRemovable = Mac.Costante("kDADiskDescriptionMediaRemovableKey");
                kMediaWhole = Mac.Costante("kDADiskDescriptionMediaWholeKey");
                kMediaWritable = Mac.Costante("kDADiskDescriptionMediaWritableKey");
                kMediaSize = Mac.Costante("kDADiskDescriptionMediaSizeKey");
                kDeviceInternal = Mac.Costante("kDADiskDescriptionDeviceInternalKey");
                kDeviceModel = Mac.Costante("kDADiskDescriptionDeviceModelKey");
                kDevicePath = Mac.Costante("kDADiskDescriptionDevicePathKey");
                kDeviceProtocol = Mac.Costante("kDADiskDescriptionDeviceProtocolKey");
                kDeviceVendor = Mac.Costante("kDADiskDescriptionDeviceVendorKey");

                sessione = Mac.DASessionCreate(IntPtr.Zero);
                if (sessione == IntPtr.Zero) { erroreAvvio = "sessione DiskArbitration non disponibile"; avviata.Set(); return; }
                pApparso = Marshal.GetFunctionPointerForDelegate(cbApparso);
                pScomparso = Marshal.GetFunctionPointerForDelegate(cbScomparso);
                pApprovazione = Marshal.GetFunctionPointerForDelegate(cbApprovazione);
                pMontato = Marshal.GetFunctionPointerForDelegate(cbMontato);
                pSmontato = Marshal.GetFunctionPointerForDelegate(cbSmontato);
                pCambio = Marshal.GetFunctionPointerForDelegate(cbCambio);
                pInattivo = Marshal.GetFunctionPointerForDelegate(cbInattivo);
                pTimer = Marshal.GetFunctionPointerForDelegate(cbTimer);

                // prima l'approvazione, poi il resto: nessun montaggio deve sfuggire
                Mac.DARegisterDiskMountApprovalCallback(sessione, IntPtr.Zero, pApprovazione, IntPtr.Zero);
                Mac.DARegisterDiskAppearedCallback(sessione, IntPtr.Zero, pApparso, IntPtr.Zero);
                Mac.DARegisterDiskDisappearedCallback(sessione, IntPtr.Zero, pScomparso, IntPtr.Zero);
                Mac.DARegisterDiskDescriptionChangedCallback(sessione, IntPtr.Zero, IntPtr.Zero, pCambio, IntPtr.Zero);
                if (Mac.Esiste("DARegisterIdleCallback")) DARegisterIdleCallback(sessione, pInattivo, IntPtr.Zero);
                Mac.DASessionScheduleWithRunLoop(sessione, ciclo, modo);
                timer = Mac.CFRunLoopTimerCreate(IntPtr.Zero, Mac.CFAbsoluteTimeGetCurrent() + 0.1, 0.1, 0, IntPtr.Zero, pTimer, IntPtr.Zero);
                Mac.CFRunLoopAddTimer(ciclo, timer, modo);
                avviata.Set();
                Mac.CFRunLoopRun();
            }
            catch (Exception e)
            {
                erroreAvvio = e.Message;
                avviata.Set();
            }
            finally { fermata.Set(); }
        }

        // ------------------------------------------------------------------ descrizione dei dischi

        Disco Leggi(IntPtr disk)
        {
            var d = new Disco();
            var desc = Mac.DADiskCopyDescription(disk);
            if (desc == IntPtr.Zero) return null;
            try
            {
                d.Bsd = Mac.Valore(desc, kMediaBSDName) ?? Marshal.PtrToStringUTF8(Mac.DADiskGetBSDName(disk));
                if (string.IsNullOrEmpty(d.Bsd)) return null;
                d.Protocollo = Mac.Valore(desc, kDeviceProtocol) ?? "";
                d.Interno = Mac.Booleano(desc, kDeviceInternal) ?? false;
                d.Rimovibile = Mac.Booleano(desc, kMediaRemovable) ?? false;
                d.Montabile = Mac.Booleano(desc, kVolumeMountable) ?? false;
                d.Tipo = Mac.Valore(desc, kVolumeKind);
                d.Etichetta = Mac.Valore(desc, kVolumeName);
                d.Percorso = Mac.Valore(desc, kVolumePath);
                bool rete = Mac.Booleano(desc, kVolumeNetwork) ?? false;
                d.Immagine = d.Protocollo == "Disk Image" || d.Protocollo == "Virtual Interface";
                // dischi esterni o supporti rimovibili (anche schede SD nel lettore interno); le immagini
                // disco solo con --test-dispositivi
                d.Bersaglio = !rete && (!d.Interno || d.Rimovibile) && (!d.Immagine || prova);
                var intero = Mac.DADiskCopyWholeDisk(disk);
                if (intero != IntPtr.Zero)
                {
                    d.Intero = Marshal.PtrToStringUTF8(Mac.DADiskGetBSDName(intero));
                    Mac.CFRelease(intero);
                }
                if (string.IsNullOrEmpty(d.Intero)) d.Intero = d.Bsd;
            }
            finally { Mac.CFRelease(desc); }
            return d;
        }

        static string Bsd(IntPtr disk)
        {
            var p = Mac.DADiskGetBSDName(disk);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
        }

        void Apparso(IntPtr disk, IntPtr ctx)
        {
            try
            {
                var d = Leggi(disk);
                if (d == null || !d.Bersaglio) return;
                bool nuovo;
                lock (chiave)
                {
                    Disco prima;
                    if (dischi.TryGetValue(d.Bsd, out prima)) { d.Rifiutato = prima.Rifiutato; }
                    dischi[d.Bsd] = d;
                    nuovo = !enumerazione;
                    if (!nuovo)
                    {
                        d.Preesistente = true;
                        if (d.Rimovibile && !d.Immagine) preesistenti.Add(d.Intero);
                    }
                    else if (d.Montabile && d.Percorso == null)
                    {
                        d.InAttesa = true;
                        d.Apparso = DateTime.Now;
                    }
                }
                if (nuovo && log != null) d.PrimaRiga = log.Conteggio;
                if (nuovo)
                    Scrivi("Comparso /dev/" + d.Bsd + " (" + (d.Protocollo.Length > 0 ? d.Protocollo : "protocollo sconosciuto") +
                           (d.Tipo != null ? ", " + d.Tipo : "") + (d.Etichetta != null ? " «" + d.Etichetta + "»" : "") + ")" +
                           (d.Percorso != null ? ": ATTENZIONE, è già montato in " + d.Percorso : "") + ".");
            }
            catch (Exception e) { Programma.Annota(e); }
        }

        void Scomparso(IntPtr disk, IntPtr ctx)
        {
            try
            {
                var b = Bsd(disk);
                if (b == null) return;
                bool era;
                lock (chiave)
                {
                    era = dischi.Remove(b);
                    pronti.Remove(b);
                }
                if (era && !enumerazione) Scrivi("Scomparso /dev/" + b + ".");
            }
            catch (Exception e) { Programma.Annota(e); }
        }

        void Cambio(IntPtr disk, IntPtr chiavi, IntPtr ctx)
        {
            try
            {
                var b = Bsd(disk);
                if (b == null) return;
                string percorso;
                var desc = Mac.DADiskCopyDescription(disk);
                if (desc == IntPtr.Zero) return;
                try { percorso = Mac.Valore(desc, kVolumePath); } finally { Mac.CFRelease(desc); }
                Volume v;
                Disco d;
                lock (chiave)
                {
                    if (dischi.TryGetValue(b, out d)) d.Percorso = percorso;
                    if (percorso == null && pronti.TryGetValue(b, out v)) pronti.Remove(b);
                    else v = null;
                }
                if (v != null) Scrivi("Il volume /dev/" + b + " non è più montato.");
            }
            catch (Exception e) { Programma.Annota(e); }
        }

        void Inattivo(IntPtr ctx)
        {
            lock (chiave)
            {
                idle = true;
                foreach (var d in dischi.Values) if (d.InAttesa) d.IdleVisto = true;
                if (enumerazione) { enumerazione = false; enumerati.Set(); }
            }
        }

        // ------------------------------------------------------------------ approvazione e montaggio

        IntPtr Approvazione(IntPtr disk, IntPtr ctx)
        {
            try
            {
                var b = Bsd(disk);
                if (b == null) return IntPtr.Zero;
                Disco d;
                lock (chiave)
                {
                    if (b == montaggioInCorso) return IntPtr.Zero;        // il montaggio in sola lettura chiesto da Repertum
                    dischi.TryGetValue(b, out d);
                }
                if (d == null) d = Leggi(disk);                           // l'approvazione puo' arrivare prima di «comparso»
                if (d == null || !d.Bersaglio || (d.Immagine && d.Preesistente)) return IntPtr.Zero;
                bool primo;
                lock (chiave) { primo = !d.Rifiutato; d.Rifiutato = true; }
                if (primo) Scrivi("Montaggio automatico di /dev/" + b + " rifiutato: lo monta Repertum in sola lettura.");
                var t = Mac.Stringa("Repertum: il supporto si monta solo in sola lettura");
                try { return Mac.DADissenterCreate(IntPtr.Zero, Mac.ReturnNotPermitted, t); }
                finally { Mac.CFRelease(t); }
            }
            catch (Exception e) { Programma.Annota(e); return IntPtr.Zero; }
        }

        // Ogni 100 ms sul filo della sessione: lavori chiesti dagli altri fili e montaggi in attesa.
        void Battito(IntPtr t, IntPtr info)
        {
            try
            {
                Action a;
                while (lavori.TryDequeue(out a)) { try { a(); } catch (Exception e) { Programma.Annota(e); } }
                Disco prossimo = null;
                lock (chiave)
                {
                    if (enumerazione && (DateTime.Now - avvio).TotalSeconds > 3) { enumerazione = false; enumerati.Set(); }
                    if (montaggioInCorso != null || !attivo) return;
                    var ora = DateTime.Now;
                    // si monta solo dopo che DiskArbitration ha finito con il disco (montaggio automatico gia' rifiutato)
                    prossimo = dischi.Values.FirstOrDefault(d => d.InAttesa &&
                        ((d.IdleVisto || !idle) && (ora - d.Apparso).TotalMilliseconds >= 700 || (ora - d.Apparso).TotalSeconds >= 5));
                    if (prossimo == null) return;
                    prossimo.InAttesa = false;
                    prossimo.Tentativi++;
                    montaggioInCorso = prossimo.Bsd;
                }
                Monta(prossimo);
            }
            catch (Exception e) { Programma.Annota(e); }
        }

        void Monta(Disco d)
        {
            var disk = Mac.DADiskCreateFromBSDName(IntPtr.Zero, sessione, d.Bsd);
            if (disk == IntPtr.Zero) { lock (chiave) montaggioInCorso = null; return; }
            var a1 = Mac.Stringa("rdonly");
            var a2 = Mac.Stringa("noowners");
            try
            {
                var arg = d.Noowners ? new[] { a1, a2, IntPtr.Zero } : new[] { a1, IntPtr.Zero };
                Scrivi("Montaggio in sola lettura di /dev/" + d.Bsd + " (" + (d.Noowners ? "rdonly, noowners" : "rdonly") + ").");
                Mac.DADiskMountWithArguments(disk, IntPtr.Zero, 0, pMontato, IntPtr.Zero, arg);
            }
            finally { Mac.CFRelease(a1); Mac.CFRelease(a2); Mac.CFRelease(disk); }
        }

        void Montato(IntPtr disk, IntPtr dissenter, IntPtr ctx)
        {
            try
            {
                var b = Bsd(disk);
                Disco d;
                lock (chiave) { montaggioInCorso = null; dischi.TryGetValue(b ?? "", out d); }
                if (b == null || d == null) return;
                if (dissenter != IntPtr.Zero)
                {
                    var motivo = Mac.Testo(Mac.DADissenterGetStatusString(dissenter));
                    var codice = Mac.DADissenterGetStatus(dissenter);
                    if (d.Noowners && d.Tentativi == 1)
                    {
                        lock (chiave) { d.Noowners = false; d.InAttesa = true; d.IdleVisto = true; d.Apparso = DateTime.MinValue; }
                        Scrivi("Montaggio con noowners non riuscito (" + (motivo ?? "codice 0x" + codice.ToString("X8")) + "): si riprova con rdonly.");
                        return;
                    }
                    var p = "La chiavetta /dev/" + b + " non si può montare in sola lettura: " + (motivo ?? "codice 0x" + codice.ToString("X8")) + ".";
                    lock (chiave) problema = p;
                    Scrivi(p);
                    return;
                }
                var v = Descrivi(disk, d);
                Mac.Fs fs = null;
                try { fs = Mac.StatFs(v.Radice); } catch { }
                bool rdonly = fs != null && (fs.Flag & Mac.MNT_RDONLY) != 0;
                lock (chiave) { pronti[b] = v; problema = null; }
                Scrivi("Volume /dev/" + b + " montato da Repertum in " + v.Radice + (rdonly ? ", in sola lettura (MNT_RDONLY)." : ": ATTENZIONE, NON risulta in sola lettura."));
            }
            catch (Exception e) { Programma.Annota(e); }
        }

        Volume Descrivi(IntPtr disk, Disco d)
        {
            var v = new Volume { Id = d.Bsd, Disco = d.Intero, Dispositivo = "/dev/" + d.Bsd, DaRepertum = true, Montato = DateTime.Now, Opzioni = d.Noowners ? "rdonly, noowners" : "rdonly", PrimaRiga = d.PrimaRiga };
            var i = v.Info;
            i.Lettera = v.Dispositivo;
            var desc = Mac.DADiskCopyDescription(disk);
            if (desc != IntPtr.Zero)
            {
                try
                {
                    v.Radice = Mac.Valore(desc, kVolumePath);
                    i.Montaggio = v.Radice;
                    i.Modello = ((Mac.Valore(desc, kDeviceVendor) ?? "").Trim() + " " + (Mac.Valore(desc, kDeviceModel) ?? "").Trim()).Trim();
                    i.Interfaccia = Mac.Valore(desc, kDeviceProtocol);
                    i.Etichetta = Mac.Valore(desc, kVolumeName);
                    i.FileSystem = Mac.Valore(desc, kVolumeType) ?? Mac.Valore(desc, kVolumeKind);
                    i.SerialeVolume = Mac.Valore(desc, kVolumeUUID);
                    i.PnpId = Mac.Valore(desc, kDevicePath);
                }
                finally { Mac.CFRelease(desc); }
            }
            var intero = Mac.DADiskCopyWholeDisk(disk);
            if (intero != IntPtr.Zero)
            {
                var dw = Mac.DADiskCopyDescription(intero);
                if (dw != IntPtr.Zero) { i.CapacitaDispositivo = Math.Max(0, Mac.Numero(Mac.CFDictionaryGetValue(dw, kMediaSize))); Mac.CFRelease(dw); }
                Mac.CFRelease(intero);
            }
            var media = Mac.DADiskCopyIOMedia(disk);
            if (media != 0)
            {
                try
                {
                    var vid = Mac.Proprieta(media, "idVendor", true);
                    var pid = Mac.Proprieta(media, "idProduct", true);
                    if (vid != null && pid != null) i.VidPid = vid + ":" + pid;
                    i.Seriale = Mac.Proprieta(media, "USB Serial Number") ?? Mac.Proprieta(media, "kUSBSerialNumberString");
                    if (string.IsNullOrEmpty(i.Seriale)) i.Seriale = (Mac.Proprieta(media, "Serial Number") ?? "").Trim();
                }
                finally { Mac.IOObjectRelease(media); }
            }
            return v;
        }

        // ------------------------------------------------------------------ per l'interfaccia

        public List<string> Preesistenti() { lock (chiave) return preesistenti.OrderBy(x => x).ToList(); }

        public HashSet<string> Esterni() { lock (chiave) return new HashSet<string>(dischi.Values.Select(d => d.Intero)); }

        public Dictionary<string, Volume> Pronti() { lock (chiave) return new Dictionary<string, Volume>(pronti); }

        public InfoSupporto Descrivi(Volume v)
        {
            var i = v.Info;
            var c = new InfoSupporto
            {
                Lettera = i.Lettera, Montaggio = i.Montaggio, Modello = i.Modello, Seriale = i.Seriale, VidPid = i.VidPid, Interfaccia = i.Interfaccia,
                CapacitaDispositivo = i.CapacitaDispositivo, Etichetta = i.Etichetta, FileSystem = i.FileSystem, SerialeVolume = i.SerialeVolume, PnpId = i.PnpId
            };
            try
            {
                var fs = Mac.StatFs(v.Radice);
                c.CapacitaVolume = (long)(fs.Blocchi * fs.Blocco);
                c.Occupato = (long)((fs.Blocchi - fs.Liberi) * fs.Blocco);
            }
            catch { }
            return c;
        }

        public Protezione Controlla(Volume v)
        {
            Mac.Fs fs;
            try { fs = Mac.StatFs(v.Radice); }
            catch (Exception e) { return new Protezione { Protetto = false, Verificata = false, Dettaglio = "il volume non ha risposto al controllo (" + e.Message + ")" }; }
            var motivi = new List<string>();
            if (fs.Da != v.Dispositivo) return new Protezione { Protetto = false, Verificata = false, Dettaglio = "in " + v.Radice + " è montato " + fs.Da + ", non " + v.Dispositivo };
            if ((fs.Flag & Mac.MNT_RDONLY) != 0) motivi.Add("il volume è montato in sola lettura (MNT_RDONLY" + (v.DaRepertum ? ", opzioni " + v.Opzioni : "") + ")");
            if (motivi.Count > 0) return new Protezione { Protetto = true, Verificata = true, Dettaglio = string.Join("; ", motivi) };
            return new Protezione { Protetto = false, Verificata = true, Dettaglio = "il volume è montato in scrittura" };
        }

        public List<string> RigheProtezione(DateTime oraBlocco, DateTime oraCollegamento, Volume v)
        {
            return new List<string>
            {
                "Montaggio automatico dei dischi esterni bloccato con DiskArbitration alle " + Formati.Data(oraBlocco) + ",",
                "prima del collegamento della chiavetta (" + Formati.Data(oraCollegamento) + "); volume montato da Repertum",
                "in sola lettura (opzioni " + v.Opzioni + ")."
            };
        }

        // ------------------------------------------------------------------ chiusura

        void Smontato(IntPtr disk, IntPtr dissenter, IntPtr ctx)
        {
            var b = Bsd(disk);
            if (dissenter == IntPtr.Zero) Scrivi("Volume /dev/" + b + " smontato.");
            else Scrivi("Il volume /dev/" + b + " non si smonta: " + (Mac.Testo(Mac.DADissenterGetStatusString(dissenter)) ?? "occupato") + ".");
            Interlocked.Decrement(ref smontaggiInCorso);
        }

        public void Chiudi(Registro registro)
        {
            if (registro != null) log = registro;
            if (filo == null || fermata.IsSet) { attivo = false; return; }
            var finito = new ManualResetEventSlim();
            lavori.Enqueue(() =>
            {
                // i volumi montati da Repertum si smontano finche' l'approvazione e' ancora attiva
                List<Volume> da;
                lock (chiave) { da = pronti.Values.Where(x => x.DaRepertum).ToList(); }
                foreach (var v in da)
                {
                    var disk = Mac.DADiskCreateFromBSDName(IntPtr.Zero, sessione, v.Id);
                    if (disk == IntPtr.Zero) continue;
                    Interlocked.Increment(ref smontaggiInCorso);
                    Mac.DADiskUnmount(disk, 0, pSmontato, IntPtr.Zero);
                    Mac.CFRelease(disk);
                }
                finito.Set();
            });
            Mac.CFRunLoopWakeUp(ciclo);
            finito.Wait(3000);
            for (int i = 0; i < 100 && Volatile.Read(ref smontaggiInCorso) > 0; i++) Thread.Sleep(50);
            var tolto = new ManualResetEventSlim();
            lavori.Enqueue(() =>
            {
                Mac.DAUnregisterApprovalCallback(sessione, pApprovazione, IntPtr.Zero);
                Mac.DAUnregisterCallback(sessione, pApparso, IntPtr.Zero);
                Mac.DAUnregisterCallback(sessione, pScomparso, IntPtr.Zero);
                Mac.DAUnregisterCallback(sessione, pCambio, IntPtr.Zero);
                if (Mac.Esiste("DARegisterIdleCallback")) Mac.DAUnregisterCallback(sessione, pInattivo, IntPtr.Zero);
                Mac.CFRunLoopTimerInvalidate(timer);
                Mac.DASessionUnscheduleFromRunLoop(sessione, ciclo, modo);
                Mac.CFRunLoopStop(ciclo);
                tolto.Set();
            });
            Mac.CFRunLoopWakeUp(ciclo);
            tolto.Wait(3000);
            fermata.Wait(3000);
            attivo = false;
            Scrivi("Callback di DiskArbitration rimossi.");
        }

        public void Dispose() { Chiudi(null); }
    }
}
