using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace Repertum
{
    public class Opzioni
    {
        public string Anteprima;     // --anteprima <cartella>: schermate con dati finti, poi esce
        public string Istantanee;    // --istantanee <cartella>: schermate a ogni passaggio del lavoro vero
        public string Verifica;      // Repertum.exe <cartella>: si apre sulla verifica di quell'acquisizione

        public static Opzioni Da(string[] args)
        {
            var o = new Opzioni();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--anteprima" && i + 1 < args.Length) o.Anteprima = args[++i];
                else if (args[i] == "--istantanee" && i + 1 < args.Length) o.Istantanee = args[++i];
                else if (System.IO.Directory.Exists(args[i])) o.Verifica = System.IO.Path.GetFullPath(args[i]);
            }
            return o;
        }
    }

    static class Programma
    {
        public const string Versione = "1.1";

        [STAThread]
        static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = Formati.It;
            Thread.CurrentThread.CurrentUICulture = Formati.It;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Errore(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // anche se tutto va storto, la protezione non deve restare accesa
                try { if (Blocco.Attivo) Blocco.Ripristina(); } catch { }
            };
            Application.Run(new FormPrincipale(Opzioni.Da(args)));
        }

        static bool erroreAperto;

        static void Errore(Exception e)
        {
            if (erroreAperto) return;          // mai una cascata di finestre d'errore
            erroreAperto = true;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Repertum-errori.txt"), DateTime.Now + "  " + e + "\r\n"); } catch { }
            try
            {
                MessageBox.Show("Si è verificato un errore imprevisto:\n\n" + e.Message, "Repertum",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { erroreAperto = false; }
        }
    }

    static class Foto
    {
        // Fotografia della finestra cosi' come appare (PrintWindow), per anteprime e collaudi.
        public static void Salva(Control c, string file)
        {
            c.Refresh();
            Application.DoEvents();
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                bool ok;
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    ok = Nativo.PrintWindow(c.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                if (!ok) c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
                bmp.Save(file, ImageFormat.Png);
            }
        }
    }
}
