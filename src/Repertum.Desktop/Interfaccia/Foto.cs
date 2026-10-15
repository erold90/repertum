// Repertum per macOS e Linux - fotografie della finestra (anteprima, istantanee) e registrazione
// per le GIF. Si disegna la finestra dall'interno (RenderTargetBitmap): nessun permesso di
// registrazione dello schermo, nessun puntatore nei PNG.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Repertum
{
    public static class Foto
    {
        public static RenderTargetBitmap Cattura(Visual v, double scala)
        {
            var dim = v.Bounds.Size;
            var px = new PixelSize(Math.Max(1, (int)Math.Round(dim.Width * scala)), Math.Max(1, (int)Math.Round(dim.Height * scala)));
            var rtb = new RenderTargetBitmap(px, new Vector(96 * scala, 96 * scala));
            rtb.Render(v);
            return rtb;
        }

        public static void Salva(Visual v, string file, double scala)
        {
            using (var rtb = Cattura(v, scala)) rtb.Save(file);
        }

        // L'icona del programma come immagine quadrata.
        public static RenderTargetBitmap Icona(int lato)
        {
            var rtb = new RenderTargetBitmap(new PixelSize(lato, lato), new Vector(96, 96));
            using (var g = rtb.CreateDrawingContext()) Icone.Disegna(g, Repertum.Icona.App, new Rect(0, 0, lato, lato).Deflate(lato / 64.0));
            return rtb;
        }

        // --icona <cartella>: le immagini per Repertum.icns (macOS) e repertum.png (Linux).
        public static void SalvaIcone(string dir)
        {
            var set = Path.Combine(dir, "Repertum.iconset");
            Directory.CreateDirectory(set);
            foreach (var n in new[] { 16, 32, 128, 256, 512 })
            {
                using (var a = Icona(n)) a.Save(Path.Combine(set, "icon_" + n + "x" + n + ".png"));
                using (var b = Icona(n * 2)) b.Save(Path.Combine(set, "icon_" + n + "x" + n + "@2x.png"));
            }
            using (var p = Icona(256)) p.Save(Path.Combine(dir, "repertum.png"));
        }
    }

    // --registra <cartella>: un PNG ogni ~120 ms (f00001.png, f00002.png...) e frames.csv con il
    // puntatore nelle coordinate del PNG. Il puntatore non si disegna: lo disegna lo script delle GIF.
    public sealed class Registratore
    {
        public const int Intervallo = 120;
        readonly Window finestra;
        readonly string dir;
        readonly DispatcherTimer timer;
        readonly Stopwatch tempo = Stopwatch.StartNew();
        readonly StreamWriter csv;
        readonly object chiave = new object();
        int numero, inSospeso;
        bool clic, fermo;

        // Posizione del puntatore nella finestra (punti logici); null = puntatore fuori.
        public Point? Puntatore;
        public bool Sintetico;      // modalita' automatica: lo muove il programma

        public Registratore(Window w, string cartella)
        {
            finestra = w;
            dir = cartella;
            Directory.CreateDirectory(dir);
            csv = new StreamWriter(Path.Combine(dir, "frames.csv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            csv.WriteLine("file;t_ms;x;y;click");
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(Intervallo), DispatcherPriority.Background, (s, e) => Fotogramma());
            w.AddHandler(InputElement.PointerMovedEvent, (s, e) => { if (!Sintetico) Puntatore = e.GetPosition(finestra); }, RoutingStrategies.Tunnel, true);
            w.AddHandler(InputElement.PointerPressedEvent, (s, e) => { if (!Sintetico) { Puntatore = e.GetPosition(finestra); clic = true; } }, RoutingStrategies.Tunnel, true);
            w.PointerExited += (s, e) => { if (!Sintetico) Puntatore = null; };
        }

        public void Avvia() { timer.Start(); Fotogramma(); }

        public void Clic() { clic = true; }

        void Fotogramma()
        {
            if (fermo || finestra.Bounds.Width < 10) return;
            RenderTargetBitmap rtb;
            try { rtb = Foto.Cattura(finestra, 1.0); }
            catch { return; }
            numero++;
            var nome = "f" + numero.ToString("00000") + ".png";
            var p = Puntatore;
            string x = "", y = "";
            if (p.HasValue && p.Value.X >= 0 && p.Value.Y >= 0 && p.Value.X < rtb.PixelSize.Width && p.Value.Y < rtb.PixelSize.Height)
            {
                x = ((int)Math.Round(p.Value.X)).ToString(CultureInfo.InvariantCulture);
                y = ((int)Math.Round(p.Value.Y)).ToString(CultureInfo.InvariantCulture);
            }
            csv.WriteLine(nome + ";" + tempo.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ";" + x + ";" + y + ";" + (clic ? "1" : "0"));
            clic = false;
            var file = Path.Combine(dir, nome);
            Interlocked.Increment(ref inSospeso);
            Task.Run(() =>
            {
                try { rtb.Save(file); } catch { }
                finally { rtb.Dispose(); Interlocked.Decrement(ref inSospeso); }
            });
        }

        // Muove il puntatore sintetico fino al punto indicato in circa 0,8 secondi, con partenza e
        // arrivo morbidi, poi «clicca».
        public async Task VaiEClicca(Point arrivo, int ms = 800)
        {
            Sintetico = true;
            var partenza = Puntatore ?? new Point(finestra.Bounds.Width * 0.6, finestra.Bounds.Height * 0.55);
            var t = Stopwatch.StartNew();
            while (t.ElapsedMilliseconds < ms)
            {
                double k = t.ElapsedMilliseconds / (double)ms;
                k = k * k * (3 - 2 * k);
                Puntatore = new Point(partenza.X + (arrivo.X - partenza.X) * k, partenza.Y + (arrivo.Y - partenza.Y) * k);
                await Task.Delay(15);
            }
            Puntatore = arrivo;
            await Task.Delay(Intervallo + 30);
            Clic();
            await Task.Delay(Intervallo + 30);
        }

        public void Ferma()
        {
            if (fermo) return;
            Fotogramma();
            fermo = true;
            timer.Stop();
            for (int i = 0; i < 200 && Volatile.Read(ref inSospeso) > 0; i++) Thread.Sleep(25);
            lock (chiave) { csv.Flush(); csv.Dispose(); }
        }
    }
}
