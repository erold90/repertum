// Repertum per macOS e Linux - avvio, opzioni da riga di comando, tema dell'interfaccia.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Repertum
{
    public class Opzioni
    {
        public string Anteprima;          // --anteprima <cartella>: schermate con dati finti, poi esce
        public string Istantanee;         // --istantanee <cartella>: registro e schermate del lavoro vero
        public string Registra;           // --registra <cartella>: fotogrammi per le GIF e frames.csv
        public string Icona;              // --icona <cartella>: immagini dell'icona, poi esce
        public string Verifica;           // Repertum <cartella>: si apre sulla verifica di quell'acquisizione
        public bool Automatico;           // --automatico: salva da solo, si chiude quando la chiavetta va via
        public bool TestDispositivi;      // --test-dispositivi: accetta immagini disco (hdiutil) e dispositivi loop

        public static Opzioni Da(string[] args)
        {
            var o = new Opzioni();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == "--anteprima" && i + 1 < args.Length) o.Anteprima = Path.GetFullPath(args[++i]);
                else if (a == "--istantanee" && i + 1 < args.Length) o.Istantanee = Path.GetFullPath(args[++i]);
                else if (a == "--registra" && i + 1 < args.Length) o.Registra = Path.GetFullPath(args[++i]);
                else if (a == "--icona" && i + 1 < args.Length) o.Icona = Path.GetFullPath(args[++i]);
                else if (a == "--automatico") o.Automatico = true;
                else if (a == "--test-dispositivi") o.TestDispositivi = true;
                else if (!a.StartsWith("-") && Directory.Exists(a)) o.Verifica = Path.GetFullPath(a);
            }
            return o;
        }
    }

    public static class Programma
    {
        public static Opzioni Opzioni;
        public static ISistema Sistema;

        [STAThread]
        public static int Main(string[] args)
        {
            // «libc» delle dichiarazioni native: libSystem su macOS, libc.so.6 su Linux
            NativeLibrary.SetDllImportResolver(typeof(Programma).Assembly, (nome, assembly, percorso) =>
                nome == "libc" ? NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6") : IntPtr.Zero);

            // Linux: lo stesso eseguibile, avviato con pkexec, fa da aiutante con i privilegi di root.
            if (args.Length > 0 && args[0] == "--helper") return Aiutante.Esegui();

            CultureInfo.DefaultThreadCurrentCulture = Formati.It;
            CultureInfo.DefaultThreadCurrentUICulture = Formati.It;
            Thread.CurrentThread.CurrentCulture = Formati.It;
            Thread.CurrentThread.CurrentUICulture = Formati.It;
            bool mac = OperatingSystem.IsMacOS();
            Piattaforma.Nome = mac ? "macOS" : "Linux";
            Piattaforma.EtichettaIdentificativo = mac ? "Identificativo macOS" : "Identificativo Linux";
            Piattaforma.Metodo = mac
                ? new[]
                {
                    "Metodo: hash calcolati con le librerie crittografiche di macOS (CommonCrypto)",
                    "in un'unica lettura di ciascun file; ogni copia e' stata riletta e il suo",
                    "SHA-256 confrontato con quello dell'originale."
                }
                : new[]
                {
                    "Metodo: hash calcolati con la libreria crittografica del sistema (OpenSSL)",
                    "in un'unica lettura di ciascun file; ogni copia e' stata riletta e il suo",
                    "SHA-256 confrontato con quello dell'originale."
                };

            Opzioni = Opzioni.Da(args);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Annota(e.ExceptionObject as Exception);
                // anche se tutto va storto, il blocco non deve restare acceso
                try { if (Sistema != null) Sistema.Chiudi(null); } catch { }
            };

            var app = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
                .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
                .LogToTrace();

            if (Opzioni.Icona != null)
            {
                app.SetupWithoutStarting();
                Foto.SalvaIcone(Opzioni.Icona);
                return 0;
            }

            if (Opzioni.Anteprima == null) Sistema = Sistemi.Crea();
            try { return app.StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose); }
            finally { try { if (Sistema != null) Sistema.Chiudi(null); } catch { } }
        }

        public static void Annota(Exception e)
        {
            if (e == null) return;
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "Repertum-errori.txt"), DateTime.Now + "  " + e + "\n"); } catch { }
        }
    }

    public class App : Application
    {
        public override void Initialize()
        {
            Name = "Repertum";
            RequestedThemeVariant = ThemeVariant.Light;
            var fluent = new FluentTheme { DensityStyle = DensityStyle.Compact };
            fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse("#005FB8") };
            Styles.Add(fluent);
            Styles.Add(new StyleInclude(new Uri("avares://Repertum/")) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
            Stile.Applica(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                d.MainWindow = new FinestraPrincipale(Programma.Opzioni, Programma.Sistema);
            Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                Programma.Annota(e.Exception);
                e.Handled = true;
                FinestraPrincipale.ErroreImprevisto(e.Exception);
            };
            base.OnFrameworkInitializationCompleted();
        }
    }
}
