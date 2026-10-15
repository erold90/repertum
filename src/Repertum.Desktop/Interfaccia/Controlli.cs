// Repertum per macOS e Linux - i pezzi dell'interfaccia: intestazione, pulsanti con icona,
// schede, finestre di avviso, righe delle tabelle. Stesso aspetto della versione Windows.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Repertum
{
    public static class Colori
    {
        public static IBrush Da(int r, int g, int b) { return new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)); }

        public static readonly IBrush Verde = Da(0, 128, 0);
        public static readonly IBrush Rosso = Da(200, 0, 0);
        public static readonly IBrush Blu = Da(0, 51, 153);
        public static readonly IBrush Grigio = Da(110, 110, 110);
        public static readonly IBrush Arancio = Da(190, 100, 0);
        public static readonly IBrush Nero = Brushes.Black;
        public static readonly IBrush Testo = Da(64, 64, 64);
        public static readonly IBrush Spento = Da(140, 140, 140);
        public static readonly IBrush Finestra = Da(240, 240, 240);
        public static readonly IBrush Pagina = Da(249, 249, 249);
        public static readonly IBrush BordoScheda = Da(229, 229, 229);
        public static readonly IBrush SchedaSpenta = Da(243, 243, 243);
        public static readonly IBrush BordoGruppo = Da(220, 220, 220);
        public static readonly IBrush BordoScuro = Da(160, 160, 160);
        public static readonly IBrush Righe = Da(200, 200, 200);
        public static readonly IBrush Barra = Da(0, 120, 212);
        public static readonly IBrush Collegamento = Da(0, 102, 204);
    }

    // Intestazione bianca in alto, come le procedure guidate di Windows: icona, titolo, una riga di spiegazione.
    public class Banner : Decorator
    {
        readonly IconaVista icona = new IconaVista { Width = 50, Height = 50, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock titolo = new TextBlock { FontSize = 17.5, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock sotto = new TextBlock { FontSize = 12.7, Foreground = Colori.Da(70, 70, 70), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        readonly IconaVista lucchetto = new IconaVista { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock destra = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 18, 0) };
        readonly StackPanel pannelloDestra;

        public Banner()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var testi = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            testi.Children.Add(titolo);
            testi.Children.Add(sotto);
            pannelloDestra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            pannelloDestra.Children.Add(lucchetto);
            pannelloDestra.Children.Add(destra);
            Grid.SetColumn(testi, 1);
            Grid.SetColumn(pannelloDestra, 2);
            g.Children.Add(icona);
            g.Children.Add(testi);
            g.Children.Add(pannelloDestra);
            Child = g;
            ClipToBounds = true;
        }

        public void Imposta(Icona i, string t, string s, IBrush c)
        {
            icona.Icona = i;
            titolo.Text = t ?? "";
            titolo.Foreground = c;
            Sotto = s;
            AutomationProperties.SetName(this, t ?? "");
        }

        public string Titolo { get { return titolo.Text; } }
        public string Sotto
        {
            get { return sotto.Text; }
            set { sotto.Text = value ?? ""; sotto.IsVisible = !string.IsNullOrEmpty(value); }
        }

        public void Protezione(string t, IBrush c, bool chiusa)
        {
            destra.Text = t ?? "";
            destra.Foreground = c;
            lucchetto.Icona = chiusa ? Icona.Lucchetto : Icona.LucchettoAperto;
            pannelloDestra.IsVisible = !string.IsNullOrEmpty(t);
        }

        // Fondo bianco con il bordo incassato (grigio in alto e a sinistra).
        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            g.FillRectangle(Brushes.White, r);
            var scuro = new Pen(Colori.BordoScuro, 1);
            g.DrawLine(scuro, new Point(0, 0.5), new Point(r.Width, 0.5));
            g.DrawLine(scuro, new Point(0.5, 0), new Point(0.5, r.Height));
        }
    }

    // Pulsante con l'icona a sinistra del testo.
    public class PulsanteIcona : Button
    {
        readonly IconaVista vista = new IconaVista { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock testo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        protected override Type StyleKeyOverride { get { return typeof(Button); } }

        public PulsanteIcona(string t, Icona i, double larghezza, double altezza, bool grande = false)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(vista);
            p.Children.Add(testo);
            Content = p;
            Width = larghezza;
            Height = altezza;
            HorizontalContentAlignment = HorizontalAlignment.Left;
            VerticalContentAlignment = VerticalAlignment.Center;
            Padding = new Thickness(9, 0, 6, 0);
            Margin = new Thickness(0, 4, 8, 4);
            if (grande) { testo.FontSize = 14; testo.FontWeight = FontWeight.Bold; }
            Testo = t;
            Icona = i;
        }

        public string Testo
        {
            get { return testo.Text; }
            set { testo.Text = value; AutomationProperties.SetName(this, value); }
        }

        public Icona Icona { get { return vista.Icona; } set { vista.Icona = value; } }
    }

    // Due schede in stile Windows: linguette piccole con il bordo, quella scelta unita alla pagina.
    public class Schede : Grid
    {
        readonly StackPanel linguette = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, -1), ZIndex = 1 };
        readonly Border pagina = new Border { Background = Colori.Pagina, BorderBrush = Colori.BordoScheda, BorderThickness = new Thickness(1), Padding = new Thickness(8) };
        readonly Panel contenuti = new Panel();
        readonly List<Border> testate = new List<Border>();
        readonly List<Control> pagine = new List<Control>();
        public int Scelta { get; private set; }
        public event Action Cambiata;

        public Schede()
        {
            RowDefinitions = new RowDefinitions("Auto,*");
            pagina.Child = contenuti;
            Grid.SetRow(pagina, 1);
            Children.Add(linguette);
            Children.Add(pagina);
        }

        public void Aggiungi(string titolo, Control contenuto)
        {
            int n = testate.Count;
            var t = new Border
            {
                BorderBrush = Colori.BordoScheda,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Child = new TextBlock { Text = titolo, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                Cursor = new Cursor(StandardCursorType.Arrow)
            };
            t.PointerPressed += (s, e) => Scegli(n);
            AutomationProperties.SetName(t, titolo);
            testate.Add(t);
            pagine.Add(contenuto);
            linguette.Children.Add(t);
            contenuti.Children.Add(contenuto);
            Scegli(Scelta);
        }

        public void Scegli(int n)
        {
            bool cambia = n != Scelta;
            Scelta = n;
            for (int i = 0; i < testate.Count; i++)
            {
                bool s = i == n;
                testate[i].Background = s ? Colori.Pagina : Colori.SchedaSpenta;
                testate[i].Padding = s ? new Thickness(16, 5, 16, 6) : new Thickness(14, 3, 14, 4);
                testate[i].Margin = s ? new Thickness(i == 0 ? 0 : -1, 0, 0, 0) : new Thickness(i == 0 ? 0 : -1, 2, 0, 1);
                testate[i].ZIndex = s ? 2 : 1;
                pagine[i].IsVisible = s;
            }
            if (cambia && Cambiata != null) Cambiata();
        }
    }

    // Avvisi e domande, come MessageBox di Windows. In modalita' automatica non si apre nulla:
    // l'avviso finisce nel registro e si prosegue con la risposta prudente.
    public static class Dialogo
    {
        public static bool Automatico;
        public static Action<string> Annota;

        public static async Task<bool> Mostra(Window padre, string testo, Icona icona, bool siNo = false)
        {
            if (Automatico || padre == null || !padre.IsVisible)
            {
                if (Annota != null) Annota("Avviso: " + testo.Replace("\n\n", " ").Replace("\n", " "));
                return true;
            }
            bool esito = false;
            var w = new Window
            {
                Title = "Repertum",
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                MinWidth = 340,
                MaxWidth = 560,
                Background = Brushes.White,
                Icon = padre.Icon
            };
            var corpo = new DockPanel();
            var piede = new Border { Background = Colori.Finestra, Padding = new Thickness(12, 10) };
            var bottoni = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Func<string, bool, Button> bottone = (t, v) =>
            {
                var b = new Button { Content = t, MinWidth = 84, Height = 26, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                b.Click += (s, e) => { esito = v; w.Close(); };
                bottoni.Children.Add(b);
                return b;
            };
            if (siNo) { bottone("Sì", true); bottone("No", false).IsDefault = true; }
            else bottone("OK", true).IsDefault = true;
            piede.Child = bottoni;
            DockPanel.SetDock(piede, Dock.Bottom);
            var alto = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 22, 26, 22), Spacing = 14 };
            alto.Children.Add(new IconaVista { Icona = icona, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top });
            alto.Children.Add(new TextBlock { Text = testo, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            corpo.Children.Add(piede);
            corpo.Children.Add(alto);
            w.Content = corpo;
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) { esito = false; w.Close(); } };
            await w.ShowDialog(padre);
            return esito;
        }
    }

    public abstract class Notificante : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Notifica(params string[] nomi)
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in nomi) h(this, new PropertyChangedEventArgs(n));
        }
    }

    // Una riga dell'elenco dei file della chiavetta.
    public sealed class RigaFile : Notificante
    {
        public readonly VoceFile Voce;
        readonly Action cambioSelezione;
        bool modificabile;

        public RigaFile(VoceFile v, Action cambio) { Voce = v; cambioSelezione = cambio; }

        public bool Sel
        {
            get { return Voce.Selezionato; }
            set { if (Voce.Selezionato == value) return; Voce.Selezionato = value; Notifica("Sel"); if (cambioSelezione != null) cambioSelezione(); }
        }
        public bool Modificabile { get { return modificabile; } set { if (modificabile != value) { modificabile = value; Notifica("Modificabile"); } } }
        public string Nome { get { return Voce.Nome; } }
        public string Cartella { get { return Voce.Cartella; } }
        public long Dimensione { get { return Voce.Dimensione; } }
        public string DimensioneTesto { get { return Formati.Byte(Voce.Dimensione); } }
        public string Esito { get { return Voce.Esito; } }
        public string SHA256 { get { return Voce.SHA256; } }
        public string MD5 { get { return Voce.MD5; } }
        public DateTime Modificato { get { return Voce.Modificato; } }
        public string ModificatoTesto { get { return Formati.Data(Voce.Modificato); } }

        bool Ok { get { return Voce.Esito == "Copia verificata"; } }
        bool Ko { get { var t = Voce.Esito ?? ""; return t.StartsWith("NON") || t.StartsWith("COPIA") || t.StartsWith("ORIGINALE"); } }
        public IBrush ColoreEsito { get { return Ok ? Colori.Verde : Ko ? Colori.Rosso : Colori.Nero; } }
        public FontWeight PesoEsito { get { return Ok || Ko ? FontWeight.Bold : FontWeight.Normal; } }

        public void Aggiorna() { Notifica("Esito", "SHA256", "MD5", "ColoreEsito", "PesoEsito", "Sel"); }
    }

    // Una riga della verifica.
    public sealed class RigaVerifica : Notificante
    {
        string esito, calcolato;
        public RigaVerifica(string nome, string atteso) { Nome = nome; Atteso = atteso; esito = "..."; calcolato = ""; }
        public string Nome { get; private set; }
        public string Atteso { get; private set; }
        public string Esito { get { return esito; } set { esito = value; Notifica("Esito", "ColoreEsito"); } }
        public string Calcolato { get { return calcolato; } set { calcolato = value; Notifica("Calcolato"); } }
        public IBrush ColoreEsito
        {
            get { return esito == "INTEGRO" ? Colori.Verde : esito == "ALTERATO" || esito == "MANCANTE" || esito == "ILLEGGIBILE" ? Colori.Rosso : Colori.Nero; }
        }
    }
}
