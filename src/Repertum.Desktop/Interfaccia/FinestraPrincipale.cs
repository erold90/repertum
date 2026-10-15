// Repertum per macOS e Linux - la finestra principale. Porting fedele di FormPrincipale.cs
// (versione Windows): stessi stati, stessi testi, stessa disposizione, stessi file prodotti.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Repertum
{
    public class FinestraPrincipale : Window
    {
        enum Fase { Avvio, DaScollegare, Attesa, Lettura, Pronto, Lavoro }

        static readonly IBrush Verde = Colori.Verde, Rosso = Colori.Rosso, Blu = Colori.Blu, Grigio = Colori.Grigio, Arancio = Colori.Arancio;
        const Algoritmi Alg = Algoritmi.SHA256 | Algoritmi.MD5;
        static FinestraPrincipale attuale;

        readonly Opzioni opz;
        readonly ISistema sistema;
        readonly Registro log = new Registro();
        readonly object chiaveIstantanee = new object();
        int righeAvvio, rigaCollegamento, numeroFoto;
        Registratore registratore;

        // interfaccia
        Schede schede;
        Banner banner, vBanner;
        readonly Dictionary<string, TextBlock> valori = new Dictionary<string, TextBlock>();
        CheckBox cTutti;
        TextBlock lSelezione, lTutti, sMsg, sProt;
        Button lDestinazione;
        TextBlock tDestinazione;
        DataGrid griglia, vGriglia;
        ProgressBar barra, vBarra;
        PulsanteIcona bSalva, bApri, vbVerifica;
        TextBox vtCartella;
        readonly ObservableCollection<RigaFile> righeFile = new ObservableCollection<RigaFile>();
        readonly ObservableCollection<RigaVerifica> righeVerifica = new ObservableCollection<RigaVerifica>();

        // stato
        Fase fase = Fase.Avvio;
        HashSet<string> conosciute = new HashSet<string>();
        List<string> daScollegare = new List<string>();
        Volume volume;
        string destinazione, ultimaCartella, ultimoProblema;
        InfoSupporto info;
        Protezione prot;
        DateTime oraBlocco, oraCollegamento, inizioCalcolo, fineCalcolo;
        List<VoceFile> voci = new List<VoceFile>();
        readonly Dictionary<VoceFile, RigaFile> righe = new Dictionary<VoceFile, RigaFile>();
        bool calcoloCompleto, verificaInCorso, chiusuraConfermata, chiuso, salvataggioAutomatico;
        CancellationTokenSource cts, vCts;
        int generazione;
        readonly DispatcherTimer tControllo = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        readonly DispatcherTimer tAvanzamento = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        long totBytes, fattiBytes;
        int totFile, fattiFile;
        volatile string fileCorrente = "";
        DateTime inizioAttivita;

        public FinestraPrincipale(Opzioni opz, ISistema sistema)
        {
            attuale = this;
            this.opz = opz;
            this.sistema = sistema;
            destinazione = DestinazioneSalvata();
            Dialogo.Automatico = opz.Automatico || opz.Anteprima != null;
            Dialogo.Annota = t => log.Scrivi(t);

            Title = "Repertum - copia e hash di chiavette USB";
            Width = 1084;
            Height = 700;
            MinWidth = 864;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Colori.Finestra;
            FontSize = 12;
            try { using (var i = Foto.Icona(256)) { var ms = new MemoryStream(); i.Save(ms); ms.Position = 0; Icon = new WindowIcon(ms); } } catch { }

            schede = new Schede { Margin = new Thickness(4, 4, 4, 6) };
            schede.Aggiungi("Chiavetta USB", CostruisciChiavetta());
            schede.Aggiungi("Verifica", CostruisciVerifica());
            var corpo = new DockPanel();
            var stato = CostruisciBarraStato();
            DockPanel.SetDock(stato, Dock.Bottom);
            corpo.Children.Add(stato);
            corpo.Children.Add(schede);
            Content = corpo;

            if (opz.Istantanee != null)
                log.Nuova += r =>
                {
                    lock (chiaveIstantanee)
                    {
                        try { Directory.CreateDirectory(opz.Istantanee); File.AppendAllText(Path.Combine(opz.Istantanee, "registro.txt"), r + "\r\n", new UTF8Encoding(true)); } catch { }
                    }
                };
            tControllo.Tick += (s, e) => ControllaUnita();
            tAvanzamento.Tick += (s, e) => AggiornaAvanzamento();
            Opened += (s, e) => Dispatcher.UIThread.Post(Avvio, DispatcherPriority.Background);
            Closing += Chiusura;
        }

        public static void ErroreImprevisto(Exception e)
        {
            var f = attuale;
            if (f == null || f.chiuso) return;
            f.log.Scrivi("Errore imprevisto: " + e.Message);
            _ = Dialogo.Mostra(f, "Si è verificato un errore imprevisto:\n\n" + e.Message, Icona.Errore);
        }

        // ================================================================== costruzione

        Control CostruisciBarraStato()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 23 };
            sMsg = new TextBlock { Text = "Avvio...", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            sProt = new TextBlock { Text = "Blocco scrittura USB: -", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) };
            var sep = new Border { BorderBrush = Colori.BordoScuro, BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(0, 3, 0, 3), Child = sProt };
            Grid.SetColumn(sep, 1);
            g.Children.Add(sMsg);
            g.Children.Add(sep);
            return new Border { Background = Colori.Finestra, BorderBrush = Colori.Da(215, 215, 215), BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
        }

        Control CostruisciChiavetta()
        {
            var t = new Grid { RowDefinitions = new RowDefinitions("78,76,Auto,*,24,Auto") };

            banner = new Banner { Margin = new Thickness(0, 0, 0, 8) };
            t.Children.Add(banner);

            // riquadro «Chiavetta» come il GroupBox di Windows
            var gruppo = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var cornice = new Border { BorderBrush = Colori.BordoGruppo, BorderThickness = new Thickness(1), Margin = new Thickness(0, 7, 0, 0), CornerRadius = new CornerRadius(3) };
            var ti = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,40*,Auto,30*,Auto,30*"),
                RowDefinitions = new RowDefinitions("*,*"),
                Margin = new Thickness(12, 14, 10, 4)
            };
            string[] chiavi = { "Modello", "Capacità", "File system", "Numero di serie", "Sola lettura", "Contenuto" };
            for (int i = 0; i < chiavi.Length; i++)
            {
                int col = (i % 3) * 2, riga = i / 3;
                var l = new TextBlock { Text = chiavi[i] + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(col == 0 ? 0 : 14, 0, 8, 0) };
                var v = new TextBlock { Text = "-", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(l, col); Grid.SetRow(l, riga);
                Grid.SetColumn(v, col + 1); Grid.SetRow(v, riga);
                ti.Children.Add(l);
                ti.Children.Add(v);
                valori[chiavi[i]] = v;
            }
            cornice.Child = ti;
            gruppo.Children.Add(cornice);
            gruppo.Children.Add(new Border
            {
                Background = Colori.Pagina, Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(8, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "Chiavetta" }
            });
            Grid.SetRow(gruppo, 1);
            t.Children.Add(gruppo);

            var fSel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 4) };
            cTutti = new CheckBox { IsThreeState = true, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            cTutti.Click += (s, e) => { bool tutti = !voci.All(v => v.Selezionato); Seleziona(v => tutti); };
            lTutti = new TextBlock { Text = "Seleziona tutti i file", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0), Foreground = Colori.Spento };
            lTutti.PointerPressed += (s, e) => { if (cTutti.IsEnabled) { bool tutti = !voci.All(v => v.Selezionato); Seleziona(v => tutti); } };
            lSelezione = new TextBlock { Margin = new Thickness(18, 0, 0, 0), Foreground = Colori.Testo, VerticalAlignment = VerticalAlignment.Center };
            fSel.Children.Add(Piccola(cTutti));
            fSel.Children.Add(lTutti);
            fSel.Children.Add(lSelezione);
            Grid.SetRow(fSel, 2);
            t.Children.Add(fSel);

            griglia = CreaGriglia(righeFile);
            griglia.Columns.Add(ColonnaSel());
            griglia.Columns.Add(Colonna("Nome file", 230, "Nome"));
            griglia.Columns.Add(Colonna("Cartella", 140, "Cartella"));
            griglia.Columns.Add(Colonna("Dimensione", 90, "DimensioneTesto", "Dimensione", destra: true));
            griglia.Columns.Add(Colonna("Stato", 130, "Esito", colore: true));
            griglia.Columns.Add(Colonna("SHA-256", 470, "SHA256", mono: true));
            griglia.Columns.Add(Colonna("MD5", 250, "MD5", mono: true));
            griglia.Columns.Add(Colonna("Ultima modifica", 130, "ModificatoTesto", "Modificato"));
            griglia.ContextMenu = Menu(("Copia l'hash SHA-256", CopiaHash), ("Copia tutta la tabella", () => CopiaGriglia(griglia)));
            Grid.SetRow(griglia, 3);
            t.Children.Add(griglia);

            barra = Barra();
            Grid.SetRow(barra, 4);
            t.Children.Add(barra);

            var basso = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
            bSalva = new PulsanteIcona("Salva copia e hash", Icona.Salva, 240, 42, true) { IsEnabled = false };
            bSalva.Click += (s, e) => Salva();
            bApri = new PulsanteIcona("Apri cartella", Icona.Cartella, 150, 42) { IsEnabled = false };
            bApri.Click += (s, e) => { if (ultimaCartella != null) Sistemi.ApriCartella(ultimaCartella); };
            Grid.SetColumn(bApri, 1);
            var fDest = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            fDest.Children.Add(new TextBlock { Text = "Salva in:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            tDestinazione = new TextBlock { Text = destinazione, TextDecorations = TextDecorations.Underline, VerticalAlignment = VerticalAlignment.Center };
            lDestinazione = new Button { Content = tDestinazione, VerticalAlignment = VerticalAlignment.Center };
            lDestinazione.Classes.Add("link");
            ToolTip.SetTip(lDestinazione, "Clic per scegliere un'altra cartella");
            lDestinazione.Click += (s, e) => CambiaDestinazione();
            fDest.Children.Add(lDestinazione);
            Grid.SetColumn(fDest, 2);
            basso.Children.Add(bSalva);
            basso.Children.Add(bApri);
            basso.Children.Add(fDest);
            Grid.SetRow(basso, 5);
            t.Children.Add(basso);
            return t;
        }

        // Le caselle di Fluent sono da 20 punti, quelle di Windows da 13: si rimpiccioliscono.
        static Control Piccola(CheckBox c)
        {
            return new LayoutTransformControl { LayoutTransform = new ScaleTransform(0.68, 0.68), Child = c, VerticalAlignment = VerticalAlignment.Center };
        }

        static ProgressBar Barra()
        {
            return new ProgressBar { Minimum = 0, Maximum = 1000, Height = 18, Margin = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
        }

        static DataGrid CreaGriglia(System.Collections.IEnumerable righe)
        {
            return new DataGrid
            {
                ItemsSource = righe,
                AutoGenerateColumns = false,
                CanUserReorderColumns = false,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = Colori.Righe,
                VerticalGridLinesBrush = Colori.Righe,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                RowHeight = 22,
                ColumnHeaderHeight = 23,
                IsReadOnly = true,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
        }

        static DataGridTemplateColumn Colonna(string titolo, double larghezza, string percorso, string ordina = null,
                                               bool destra = false, bool mono = false, bool colore = false, bool grassetto = false)
        {
            return new DataGridTemplateColumn
            {
                Header = titolo,
                Width = new DataGridLength(larghezza),
                SortMemberPath = ordina ?? percorso,
                CanUserSort = true,
                CellTemplate = new FuncDataTemplate<object>((o, ns) =>
                {
                    var t = new TextBlock
                    {
                        Margin = new Thickness(5, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = destra ? TextAlignment.Right : TextAlignment.Left
                    };
                    t.Bind(TextBlock.TextProperty, new Binding(percorso));
                    if (mono) { t.FontFamily = Stile.Mono; t.FontSize = 11; }
                    if (colore)
                    {
                        t.Bind(TextBlock.ForegroundProperty, new Binding("ColoreEsito"));
                        if (grassetto) t.FontWeight = FontWeight.Bold;
                        else t.Bind(TextBlock.FontWeightProperty, new Binding("PesoEsito"));
                    }
                    return t;
                }, true)
            };
        }

        DataGridTemplateColumn ColonnaSel()
        {
            return new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(30),
                CanUserResize = false,
                CanUserSort = false,
                CellTemplate = new FuncDataTemplate<object>((o, ns) =>
                {
                    var c = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
                    c.Bind(CheckBox.IsCheckedProperty, new Binding("Sel") { Mode = BindingMode.TwoWay });
                    // come la colonna ReadOnly di Windows: resta blu, ma non si cambia fuori dalla fase «pronto»
                    c.Bind(InputElement.IsHitTestVisibleProperty, new Binding("Modificabile"));
                    var p = Piccola(c);
                    p.HorizontalAlignment = HorizontalAlignment.Center;
                    return p;
                }, true)
            };
        }

        static ContextMenu Menu(params (string testo, Action azione)[] voci)
        {
            var m = new ContextMenu();
            var elenco = new List<MenuItem>();
            foreach (var v in voci)
            {
                var i = new MenuItem { Header = v.testo };
                var a = v.azione;
                i.Click += (s, e) => a();
                elenco.Add(i);
            }
            m.ItemsSource = elenco;
            return m;
        }

        Control CostruisciVerifica()
        {
            var t = new Grid { RowDefinitions = new RowDefinitions("78,Auto,24,*") };
            vBanner = new Banner { Margin = new Thickness(0, 0, 0, 8) };
            vBanner.Imposta(Icona.Scudo, "Verifica una copia", "Ricontrolla che i file salvati da Repertum non siano cambiati.", Blu);
            t.Children.Add(vBanner);

            var riga = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 4) };
            vtCartella = new TextBox { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Height = 25 };
            vtCartella.KeyDown += (s, e) => { if (e.Key == Key.Enter) AvviaVerifica(); };
            var vbSfoglia = new Button { Content = "Sfoglia...", Width = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            vbSfoglia.Click += async (s, e) =>
            {
                var d = await ScegliCartella("Scegli la cartella salvata da Repertum", Directory.Exists(destinazione) ? destinazione : null);
                if (d != null) { vtCartella.Text = d; AvviaVerifica(); }
            };
            vbVerifica = new PulsanteIcona("Verifica", Icona.Scudo, 150, 42, true);
            vbVerifica.Click += (s, e) => AvviaVerifica();
            var lc = new TextBlock { Text = "Cartella:", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(vtCartella, 1);
            Grid.SetColumn(vbSfoglia, 2);
            Grid.SetColumn(vbVerifica, 3);
            riga.Children.Add(lc);
            riga.Children.Add(vtCartella);
            riga.Children.Add(vbSfoglia);
            riga.Children.Add(vbVerifica);
            Grid.SetRow(riga, 1);
            t.Children.Add(riga);

            vBarra = Barra();
            Grid.SetRow(vBarra, 2);
            t.Children.Add(vBarra);

            vGriglia = CreaGriglia(righeVerifica);
            vGriglia.Columns.Add(Colonna("File", 320, "Nome"));
            vGriglia.Columns.Add(Colonna("Esito", 110, "Esito", colore: true, grassetto: true));
            vGriglia.Columns.Add(Colonna("SHA-256 salvato", 470, "Atteso", mono: true));
            vGriglia.Columns.Add(Colonna("SHA-256 di adesso", 470, "Calcolato", mono: true));
            vGriglia.ContextMenu = Menu(("Copia tutta la tabella", () => CopiaGriglia(vGriglia)));
            Grid.SetRow(vGriglia, 3);
            t.Children.Add(vGriglia);
            return t;
        }

        // ================================================================== avvio, unita', fasi

        async void Avvio()
        {
            log.Scrivi("Repertum per " + Piattaforma.Nome + " " + Versione.Breve + " avviato su " + Environment.MachineName + ", utente " + Environment.UserName + ".");
            if (opz.Registra != null)
            {
                registratore = new Registratore(this, opz.Registra);
                if (opz.Automatico) { registratore.Sintetico = true; registratore.Puntatore = new Point(Bounds.Width * 0.62, Bounds.Height * 0.58); }
                registratore.Avvia();
            }
            if (opz.Anteprima != null) { await EseguiAnteprima(opz.Anteprima); return; }

            banner.Imposta(Icona.Clessidra, "Attivazione della sola lettura...", "", Blu);
            sMsg.Text = "Attivazione della sola lettura";
            try
            {
                bool prova = opz.TestDispositivi;
                var descrizione = await Task.Run(() => sistema.Attiva(prova, log));
                oraBlocco = DateTime.Now;
                log.Scrivi("Protezione da scrittura USB attivata (" + descrizione + ").");
                MostraProtezione(true);
            }
            catch (Exception ex)
            {
                MostraProtezione(false);
                log.Scrivi("Protezione da scrittura NON attivata: " + ex.Message);
                await Dialogo.Mostra(this, "Non è stato possibile attivare la sola lettura:\n" + ex.Message +
                                     "\n\nIl programma funziona, ma " + Piattaforma.Nome + " potrà scrivere sulla chiavetta.", Icona.Avviso);
            }
            if (chiuso) return;
            righeAvvio = log.Conteggio;
            conosciute = new HashSet<string>(sistema.Pronti().Keys);
            daScollegare = sistema.Preesistenti();
            if (daScollegare.Count > 0) log.Scrivi("All'avvio era già collegato un supporto rimovibile (" + string.Join(", ", daScollegare) + "): va scollegato e ricollegato.");
            ImpostaFase(daScollegare.Count > 0 ? Fase.DaScollegare : Fase.Attesa);
            tControllo.Start();
            Istantanea("avvio");
            if (opz.Verifica != null) { schede.Scegli(1); vtCartella.Text = opz.Verifica; AvviaVerifica(); }
        }

        void MostraProtezione(bool attiva)
        {
            banner.Protezione(attiva ? "Blocco scrittura attivo" : "Blocco scrittura NON attivo", attiva ? Verde : Rosso, attiva);
            sProt.Text = "Blocco scrittura USB: " + (attiva ? "attivo" : "NON attivo");
            sProt.Foreground = attiva ? Verde : Rosso;
        }

        void ControllaUnita()
        {
            Dictionary<string, Volume> ora;
            try { ora = sistema.Pronti(); } catch { return; }
            switch (fase)
            {
                case Fase.DaScollegare:
                    var esterni = sistema.Esterni();
                    if (!daScollegare.Any(esterni.Contains))
                    {
                        conosciute = new HashSet<string>(ora.Keys);
                        log.Scrivi("Il supporto collegato prima dell'avvio è stato scollegato.");
                        ImpostaFase(Fase.Attesa);
                    }
                    break;
                case Fase.Attesa:
                    conosciute.IntersectWith(ora.Keys);
                    var nuova = ora.Keys.FirstOrDefault(k => !conosciute.Contains(k));
                    if (nuova != null) { Collega(ora[nuova]); break; }
                    var problema = sistema.UltimoProblema;
                    if (problema != null && problema != ultimoProblema) { ultimoProblema = problema; sMsg.Text = problema; }
                    break;
                case Fase.Lettura:
                case Fase.Pronto:
                case Fase.Lavoro:
                    if (volume != null && !ora.ContainsKey(volume.Id)) Scollegata();
                    break;
            }
        }

        string TestoSalva() { return voci.Any(v => v.Selezionato) || voci.Count == 0 ? "Salva copia e hash" : "Salva solo gli hash"; }

        void ImpostaFase(Fase f)
        {
            fase = f;
            bool pronto = f == Fase.Pronto && calcoloCompleto;
            cTutti.IsEnabled = pronto && voci.Count > 0;
            lTutti.Foreground = cTutti.IsEnabled ? Brushes.Black : Colori.Spento;
            foreach (var r in righeFile) r.Modificabile = f == Fase.Pronto;
            bSalva.Testo = f == Fase.Lavoro ? "Ferma" : TestoSalva();
            bSalva.Icona = f == Fase.Lavoro ? Icona.Ferma : Icona.Salva;
            bSalva.IsEnabled = (pronto && voci.Count > 0) || f == Fase.Lavoro;
            bApri.IsEnabled = ultimaCartella != null && f != Fase.Lavoro;
            lDestinazione.IsEnabled = f != Fase.Lavoro;
            tDestinazione.TextDecorations = f != Fase.Lavoro ? TextDecorations.Underline : null;
            if (f != Fase.Lettura && f != Fase.Lavoro) tAvanzamento.IsEnabled = false;

            switch (f)
            {
                case Fase.DaScollegare:
                    banner.Imposta(Icona.Avviso, "Scollega la chiavetta e ricollegala", "Era già collegata all'apertura del programma: la sola lettura vale da adesso.", Arancio);
                    sMsg.Text = "Scollegare la chiavetta già collegata (" + string.Join(", ", daScollegare) + ")";
                    break;
                case Fase.Attesa:
                    banner.Imposta(Icona.Usb, "Collega la chiavetta", "Verrà letta senza modificarla.", Blu);
                    sMsg.Text = "In attesa della chiavetta";
                    break;
                case Fase.Pronto:
                    if (calcoloCompleto) MostraPronto();
                    break;
            }
        }

        void MostraPronto()
        {
            int letti = voci.Count(v => v.SHA256 != null);
            string quanti = voci.Count == 1 ? "1 file" : voci.Count.ToString("N0", Formati.It) + " file";
            if (prot != null && !prot.Protetto)
                banner.Imposta(Icona.Errore, "Attenzione: la chiavetta NON è in sola lettura", "Scollegala e riprova.", Rosso);
            else if (voci.Count == 0)
                banner.Imposta(Icona.Usb, "La chiavetta è vuota", "Non ci sono file da salvare.", Grigio);
            else if (letti < voci.Count)
                banner.Imposta(Icona.Avviso, "Chiavetta letta: " + (voci.Count - letti) + " file non leggibili", "Spunta i file da salvare e premi «Salva copia e hash».", Arancio);
            else
                banner.Imposta(Icona.Usb, "Chiavetta letta: " + quanti + " (" + Formati.Byte(voci.Sum(v => v.Dimensione)) + ")", "Spunta i file da salvare e premi «Salva copia e hash».", Blu);
        }

        // ================================================================== collegamento della chiavetta

        void Collega(Volume v)
        {
            volume = v;
            oraCollegamento = DateTime.Now;
            generazione++;
            int gen = generazione;
            // il LOG.txt parte dalla prima riga su questo supporto (comparsa e montaggio in sola lettura)
            rigaCollegamento = v.PrimaRiga >= righeAvvio && v.PrimaRiga <= log.Conteggio ? v.PrimaRiga : log.Conteggio;
            log.Scrivi("Supporto collegato: " + v.Dispositivo + (v.DaRepertum ? ", montato da Repertum in sola lettura in " : ", montato in ") + v.Radice);
            PulisciSupporto();
            ImpostaFase(Fase.Lettura);
            banner.Imposta(Icona.Clessidra, "Lettura della chiavetta...", "Riconoscimento in corso.", Blu);
            sMsg.Text = "Riconoscimento della chiavetta " + v.Dispositivo;
            Task.Run(() =>
            {
                try
                {
                    var i = sistema.Descrivi(v);
                    var p = sistema.Controlla(v);
                    var err = new List<string>();
                    var lista = Elenco.Leggi(v.Radice, err);
                    Dispatcher.UIThread.Post(() => { if (gen == generazione) { MostraSupporto(i, p, lista, err); AvviaCalcolo(); } });
                }
                catch (Exception ex)
                {
                    log.Scrivi("Errore nella lettura del supporto: " + ex);
                    Dispatcher.UIThread.Post(() => { if (gen == generazione) banner.Imposta(Icona.Errore, "Non riesco a leggere la chiavetta", ex.Message, Rosso); });
                }
            });
        }

        void PulisciSupporto()
        {
            voci = new List<VoceFile>();
            righe.Clear();
            righeFile.Clear();
            info = null;
            prot = null;
            calcoloCompleto = false;
            foreach (var l in valori.Values) { l.Text = "-"; l.Foreground = Brushes.Black; }
            barra.Value = 0;
            totFile = 0;
            totBytes = 0;
            lSelezione.Text = "";
            cTutti.IsChecked = false;
        }

        void ImpostaValore(string chiave, string testo, IBrush colore = null)
        {
            TextBlock l;
            if (!valori.TryGetValue(chiave, out l)) return;
            l.Text = string.IsNullOrEmpty(testo) ? "-" : testo;
            l.Foreground = colore ?? Brushes.Black;
        }

        void MostraSupporto(InfoSupporto i, Protezione p, List<VoceFile> lista, List<string> errori, bool avviso = true)
        {
            info = i;
            prot = p;
            voci = lista;
            ImpostaValore("Modello", i.Modello);
            ImpostaValore("Numero di serie", i.Seriale);
            ImpostaValore("Capacità", i.CapacitaDispositivo > 0 ? Formati.Byte(i.CapacitaDispositivo) : Formati.Byte(i.CapacitaVolume));
            ImpostaValore("File system", (i.FileSystem ?? "") + (string.IsNullOrEmpty(i.Etichetta) ? "" : "  «" + i.Etichetta + "»"));
            if (p.Protetto) ImpostaValore("Sola lettura", "Sì, verificata", Verde);
            else ImpostaValore("Sola lettura", p.Verificata ? "NO" : "Non verificata", Rosso);
            ImpostaValore("Contenuto", lista.Count.ToString("N0", Formati.It) + " file, " + Formati.Byte(lista.Sum(v => v.Dimensione)));
            if (avviso)
            {
                foreach (var kv in i.Righe()) log.Scrivi(kv.Key + ": " + kv.Value);
                log.Scrivi("Controllo della protezione: " + (p.Protetto ? "SOLA LETTURA - " : "NON CONFERMATA - ") + p.Dettaglio + ".");
                log.Scrivi("Contenuto: " + lista.Count + " file, " + Formati.ByteEsatti(lista.Sum(v => v.Dimensione)) + ".");
                foreach (var e in errori) log.Scrivi("Cartella non leggibile: " + e);
            }

            var nuove = new List<RigaFile>();
            foreach (var v in lista)
            {
                var r = new RigaFile(v, AggiornaSelezione);
                righe[v] = r;
                nuove.Add(r);
            }
            foreach (var r in nuove) righeFile.Add(r);
            griglia.SelectedItem = null;
            AggiornaSelezione();
            if (!avviso) return;
            Istantanea("rilevata");

            if (!p.Protetto && sistema != null && sistema.Attivo)
            {
                _ = Dialogo.Mostra(this, "ATTENZIONE: la chiavetta NON risulta in sola lettura.\n(" + p.Dettaglio + ")\n\n" +
                                   "Scollega la chiavetta e riprova.", Icona.Avviso);
            }
        }

        void Scollegata()
        {
            bool inCorso = fase == Fase.Lettura || fase == Fase.Lavoro;
            generazione++;
            if (cts != null) cts.Cancel();
            log.Scrivi("Supporto " + volume.Dispositivo + " scollegato" + (inCorso ? " DURANTE il lavoro: operazione interrotta." : "."));
            log.Chiudi();
            volume = null;
            PulisciSupporto();
            conosciute = new HashSet<string>(sistema.Pronti().Keys);
            ImpostaFase(Fase.Attesa);
            if (ultimaCartella != null && !inCorso) sMsg.Text = "Ultimo salvataggio: " + ultimaCartella;
            Istantanea("scollegata");
            if (opz.Automatico)
            {
                // modalita' automatica: chiavetta tolta, si chiude con la pulizia completa
                Dispatcher.UIThread.Post(() => { chiusuraConfermata = true; Close(); }, DispatcherPriority.Background);
                return;
            }
            if (inCorso)
                _ = Dialogo.Mostra(this, "La chiavetta è stata scollegata durante il lavoro: operazione interrotta.", Icona.Avviso);
        }

        // ================================================================== hash

        CancellationToken NuovoToken()
        {
            cts = new CancellationTokenSource();
            return cts.Token;
        }

        void IniziaAvanzamento(int file, long bytes)
        {
            totFile = file;
            totBytes = Math.Max(1, bytes);
            Interlocked.Exchange(ref fattiBytes, 0);
            Interlocked.Exchange(ref fattiFile, 0);
            inizioAttivita = DateTime.Now;
            barra.Value = 0;
            tAvanzamento.IsEnabled = true;
            AggiornaAvanzamento();
        }

        void AggiornaAvanzamento()
        {
            if (totFile <= 0 || (fase != Fase.Lettura && fase != Fase.Lavoro)) return;
            long fatti = Interlocked.Read(ref fattiBytes);
            double quota = Math.Max(0.0, Math.Min(1.0, fatti / (double)totBytes));
            barra.Value = Math.Max(0, Math.Min(1000, (int)(quota * 1000)));
            var trascorso = DateTime.Now - inizioAttivita;
            double vel = trascorso.TotalSeconds > 0.5 ? fatti / trascorso.TotalSeconds : 0;
            string resta = vel > 0 && quota < 1 ? "  -  mancano circa " + Formati.Durata(TimeSpan.FromSeconds((totBytes - fatti) / vel)) : "";
            banner.Sotto = string.Format(Formati.It, "{0:N0} di {1:N0} file  -  {2:N0}%{3}", Math.Min(Volatile.Read(ref fattiFile), totFile), totFile, quota * 100, resta);
            sMsg.Text = (fase == Fase.Lavoro ? "Copia: " : "Lettura: ") + fileCorrente;
        }

        // A ogni file finito: la riga e, subito, anche «N di M» e la percentuale dell'intestazione,
        // che altrimenti resterebbero indietro di un giro del timer quando i file sono piccoli.
        void FileFinito(VoceFile v, int gen)
        {
            Interlocked.Increment(ref fattiFile);
            Dispatcher.UIThread.Post(() => { if (gen == generazione) { AggiornaRiga(v); AggiornaAvanzamento(); } });
        }

        void AvviaCalcolo()
        {
            if (voci.Count == 0) { calcoloCompleto = true; fineCalcolo = DateTime.Now; ImpostaFase(Fase.Pronto); FineLettura(); return; }
            calcoloCompleto = false;
            foreach (var v in voci) { v.MD5 = v.SHA1 = v.SHA256 = v.SHA512 = null; v.Esito = ""; v.Copiato = false; AggiornaRiga(v); }
            ImpostaFase(Fase.Lettura);
            banner.Imposta(Icona.Clessidra, "Lettura della chiavetta...", "", Blu);
            var token = NuovoToken();
            int gen = generazione;
            var lista = voci.ToList();
            inizioCalcolo = DateTime.Now;
            IniziaAvanzamento(lista.Count, lista.Sum(v => v.Dimensione));
            log.Scrivi("Inizio del calcolo degli hash (MD5, SHA-256).");
            Task.Run(() =>
            {
                bool interrotto = Procedura.CalcolaHash(lista, Alg, log, n => Interlocked.Add(ref fattiBytes, n),
                                                        v => fileCorrente = v.Percorso, v => FileFinito(v, gen), token);
                Dispatcher.UIThread.Post(() => { if (gen == generazione) FineCalcolo(interrotto); });
            });
        }

        void AggiornaRiga(VoceFile v)
        {
            RigaFile r;
            if (righe.TryGetValue(v, out r)) r.Aggiorna();
        }

        void FineCalcolo(bool interrotto)
        {
            fineCalcolo = DateTime.Now;
            AggiornaAvanzamento();
            calcoloCompleto = !interrotto;
            int letti = voci.Count(v => v.SHA256 != null);
            if (Calcolo.Md5Disponibile == false) log.Scrivi("MD5 non disponibile su questo computer (criterio FIPS).");
            log.Scrivi(interrotto ? "Calcolo degli hash interrotto." : "Fine del calcolo degli hash: " + letti + " file su " + voci.Count + ".");
            barra.Value = interrotto ? barra.Value : 1000;
            ImpostaFase(Fase.Pronto);
            sMsg.Text = interrotto ? "Lettura interrotta" : "Hash calcolati su " + letti + " file";
            Istantanea("pronto");
            if (!interrotto) FineLettura();
        }

        // --automatico: finita la lettura si preme da soli «Salva copia e hash», portandoci sopra il puntatore.
        async void FineLettura()
        {
            if (!opz.Automatico || salvataggioAutomatico || voci.Count == 0 || !bSalva.IsEnabled) return;
            salvataggioAutomatico = true;
            int gen = generazione;
            await Task.Delay(600);
            if (gen != generazione || fase != Fase.Pronto) return;
            if (registratore != null)
            {
                var centro = bSalva.TranslatePoint(new Point(bSalva.Bounds.Width / 2, bSalva.Bounds.Height / 2), this);
                if (centro.HasValue) await registratore.VaiEClicca(centro.Value);
            }
            if (gen != generazione || fase != Fase.Pronto) return;
            log.Scrivi("Modalità automatica: «" + bSalva.Testo + "».");
            Salva();
        }

        // ================================================================== selezione

        void Seleziona(Func<VoceFile, bool> criterio)
        {
            foreach (var v in voci)
            {
                v.Selezionato = criterio(v);
                RigaFile r;
                if (righe.TryGetValue(v, out r)) r.Aggiorna();
            }
            AggiornaSelezione();
        }

        void AggiornaSelezione()
        {
            var s = voci.Where(v => v.Selezionato).ToList();
            lSelezione.Text = voci.Count == 0 ? "" : string.Format(Formati.It, "{0:N0} di {1:N0} file selezionati  ({2})", s.Count, voci.Count, Formati.Byte(s.Sum(v => v.Dimensione)));
            cTutti.IsChecked = s.Count == 0 ? false : s.Count == voci.Count ? true : (bool?)null;
            if (fase != Fase.Lavoro) bSalva.Testo = TestoSalva();
        }

        // ================================================================== salvataggio

        static string CartellaPredefinita() { return Path.Combine(Sistemi.Casa(), "Repertum"); }

        static string DestinazioneSalvata()
        {
            try
            {
                var f = Sistemi.Impostazioni();
                if (File.Exists(f))
                    foreach (var r in File.ReadAllLines(f, Encoding.UTF8))
                        if (r.StartsWith("Destinazione=") && r.Length > 13) return r.Substring(13);
            }
            catch { }
            return CartellaPredefinita();
        }

        async void CambiaDestinazione()
        {
            var d = await ScegliCartella("Dove salvare le copie e gli hash?", Directory.Exists(destinazione) ? destinazione : null);
            if (d == null) return;
            destinazione = d;
            tDestinazione.Text = d;
            try
            {
                var f = Sistemi.Impostazioni();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "Destinazione=" + d + "\n", new UTF8Encoding(false));
            }
            catch { }
        }

        // La cartella di destinazione non puo' stare dentro un supporto collegato (protetto o no).
        bool DentroUnSupporto(string percorso)
        {
            var p = Path.GetFullPath(percorso).TrimEnd('/') + "/";
            var radici = sistema.Pronti().Values.Select(v => v.Radice).ToList();
            if (volume != null) radici.Add(volume.Radice);
            return radici.Any(r => !string.IsNullOrEmpty(r) && p.StartsWith(r.TrimEnd('/') + "/", StringComparison.Ordinal));
        }

        async void Salva()
        {
            if (fase == Fase.Lavoro) { if (cts != null) cts.Cancel(); return; }
            bool conCopia = voci.Any(v => v.Selezionato && v.SHA256 != null);
            var scelti = conCopia ? voci.Where(v => v.Selezionato && v.SHA256 != null).ToList() : voci.Where(v => v.SHA256 != null).ToList();
            if (scelti.Count == 0) return;
            long serve = conCopia ? scelti.Sum(v => v.Dimensione) : 0;
            try
            {
                if (DentroUnSupporto(destinazione))
                {
                    await Dialogo.Mostra(this, "Non si può salvare sulla chiavetta stessa: scegli un'altra cartella con «Salva in».", Icona.Info);
                    return;
                }
                Directory.CreateDirectory(destinazione);
                Utente.Restituisci(destinazione);
                long libero = new DriveInfo(destinazione).AvailableFreeSpace;
                if (serve + 16L * 1024 * 1024 > libero)
                {
                    await Dialogo.Mostra(this, "Spazio insufficiente in " + destinazione + ":\nservono " + Formati.Byte(serve) + ", liberi " + Formati.Byte(libero) +
                                         ".\n\nScegli un'altra cartella con «Salva in».", Icona.Avviso);
                    return;
                }
            }
            catch (Exception ex)
            {
                await Dialogo.Mostra(this, "Non riesco a usare la cartella " + destinazione + ":\n" + ex.Message, Icona.Avviso);
                return;
            }
            if (fase != Fase.Pronto) return;

            var nome = DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_" + Formati.NomeSicuro(info != null && !string.IsNullOrEmpty(info.Etichetta) ? info.Etichetta : "chiavetta");
            var cartella = Path.Combine(destinazione, nome);
            for (int i = 2; Directory.Exists(cartella); i++) cartella = Path.Combine(destinazione, nome + "-" + i);
            Directory.CreateDirectory(cartella);
            log.Apri(Path.Combine(cartella, "LOG.txt"), righeAvvio, rigaCollegamento);
            log.Scrivi((conCopia ? "Copia e verifica di " + scelti.Count + " file" : "Salvataggio dei soli hash") + ". Cartella: " + cartella);

            ImpostaFase(Fase.Lavoro);
            banner.Imposta(Icona.Clessidra, conCopia ? "Copia in corso..." : "Salvataggio degli hash...", "", Blu);
            var token = NuovoToken();
            int gen = generazione;
            var inizio = DateTime.Now;
            IniziaAvanzamento(scelti.Count, conCopia ? 2 * serve : 1);
            var tutte = voci.ToList();
            var i0 = info; var p0 = prot; var v0 = volume;
            bool attivo = sistema.Attivo;
            DateTime ob = oraBlocco, oc = oraCollegamento, ic = inizioCalcolo, fc = fineCalcolo;
            await Task.Run(() =>
            {
                int buone = 0;
                foreach (var v in tutte) { v.Copiato = false; v.InElenco = false; if (v.SHA256 != null) v.Esito = "Letto"; }
                if (conCopia)
                    buone = Procedura.CopiaEVerifica(scelti, cartella, log, n => Interlocked.Add(ref fattiBytes, n),
                                                     v => fileCorrente = v.Percorso, v => FileFinito(v, gen), token);
                else foreach (var v in scelti) v.InElenco = true;

                bool interrotto = token.IsCancellationRequested;
                var tempi = Allegati.Tempi(ob, oc, ic, fc, conCopia, inizio, DateTime.Now);
                var protezione = new List<string>();
                if (attivo && v0 != null && v0.DaRepertum) protezione.AddRange(sistema.RigheProtezione(ob, oc, v0));
                else protezione.Add("Protezione da scrittura NON attiva.");
                if (p0 != null)
                    protezione.Add(p0.Protetto ? "Controllo sulla chiavetta, senza scriverci: " + p0.Dettaglio + "."
                                               : "ATTENZIONE: il controllo sulla chiavetta NON ha confermato la sola lettura (" + p0.Dettaglio + ").");
                if (interrotto) protezione.Add("NOTA: copia interrotta prima della fine.");
                Allegati.Scrivi(cartella, i0, protezione, tutte, Alg, tempi, conCopia, log);
                Utente.Restituisci(cartella);
                int totale = scelti.Count;
                Dispatcher.UIThread.Post(() => { if (gen == generazione) FineLavoro(cartella, conCopia, interrotto, buone, totale); });
            });
        }

        void FineLavoro(string cartella, bool conCopia, bool interrotto, int buone, int totale)
        {
            log.Chiudi();
            AggiornaAvanzamento();
            ultimaCartella = cartella;
            ImpostaFase(Fase.Pronto);
            barra.Value = interrotto ? barra.Value : 1000;
            string dove = "Salvati in " + cartella + "  -  ora puoi scollegare la chiavetta.";
            if (!conCopia)
                banner.Imposta(Icona.Fatto, "Fatto: hash di " + voci.Count(v => v.InElenco) + " file salvati", dove, Verde);
            else if (interrotto)
                banner.Imposta(Icona.Avviso, "Copia fermata: " + voci.Count(v => v.Copiato) + " file su " + totale, "I file copiati e i loro hash sono in " + cartella, Arancio);
            else if (buone == totale)
                banner.Imposta(Icona.Fatto, "Fatto: " + (totale == 1 ? "1 file copiato e verificato" : totale + " file copiati e verificati"), dove, Verde);
            else
                banner.Imposta(Icona.Errore, "Copia con errori: verificati " + buone + " file su " + totale, "Controlla la colonna «Stato».  " + cartella, Rosso);
            sMsg.Text = "Salvato in " + cartella;
            Istantanea("fatto");
        }

        // ================================================================== verifica di una cartella salvata

        async Task<string> ScegliCartella(string descrizione, string iniziale)
        {
            if (opz.Automatico) return null;
            var o = new FolderPickerOpenOptions { Title = descrizione, AllowMultiple = false };
            try { if (!string.IsNullOrEmpty(iniziale) && Directory.Exists(iniziale)) o.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(iniziale); } catch { }
            var r = await StorageProvider.OpenFolderPickerAsync(o);
            return r != null && r.Count > 0 ? r[0].TryGetLocalPath() : null;
        }

        async void AvviaVerifica()
        {
            if (verificaInCorso) { if (vCts != null) vCts.Cancel(); return; }
            var cartella = (vtCartella.Text ?? "").Trim().Trim('"', '\'');
            if (cartella.Length == 0 || !Directory.Exists(cartella))
            {
                var d = await ScegliCartella("Scegli la cartella salvata da Repertum", Directory.Exists(destinazione) ? destinazione : null);
                if (d == null) return;
                vtCartella.Text = cartella = d;
            }
            var sha = Allegati.LeggiElenco(Path.Combine(cartella, "HASH.sha256"));
            if (sha.Count == 0)
            {
                vBanner.Imposta(Icona.Avviso, "Non è una cartella di Repertum", "Scegli la cartella che contiene HASH.sha256.", Arancio);
                return;
            }
            var md5 = Allegati.LeggiElenco(Path.Combine(cartella, "HASH.md5"));
            string baseDir = Path.Combine(cartella, "File");
            if (!Directory.Exists(baseDir))
            {
                var d = await ScegliCartella("Qui ci sono solo gli hash: scegli la chiavetta o la cartella con i file da confrontare.", null);
                if (d == null) return;
                baseDir = d;
            }
            righeVerifica.Clear();
            var elenco = sha.ToList();
            foreach (var kv in elenco) righeVerifica.Add(new RigaVerifica(kv.Key, kv.Value));
            vGriglia.SelectedItem = null;
            verificaInCorso = true;
            vbVerifica.Testo = "Ferma";
            vbVerifica.Icona = Icona.Ferma;
            vBarra.Value = 0;
            vBanner.Imposta(Icona.Clessidra, "Verifica in corso...", cartella, Blu);
            vCts = new CancellationTokenSource();
            var token = vCts.Token;
            long totale = Procedura.DaVerificare(elenco, baseDir);
            var esito = await Task.Run(() => Procedura.Verifica(elenco, md5, baseDir, (riga, calcolato, e, f) => Dispatcher.UIThread.Post(() =>
            {
                if (riga >= righeVerifica.Count) return;
                var r = righeVerifica[riga];
                r.Calcolato = calcolato;
                r.Esito = e;
                vBarra.Value = (int)Math.Min(1000, f * 1000 / totale);
            }), token));
            Dispatcher.UIThread.Post(() => FineVerifica(cartella, baseDir, esito));
        }

        void FineVerifica(string cartella, string baseDir, EsitoVerifica esito)
        {
            int totale = esito.Totale, integri = esito.Integri, alterati = esito.Alterati, mancanti = esito.Mancanti;
            bool interrotto = esito.Interrotta;
            verificaInCorso = false;
            vbVerifica.Testo = "Verifica";
            vbVerifica.Icona = Icona.Scudo;
            if (!interrotto) vBarra.Value = 1000;
            if (interrotto)
                vBanner.Imposta(Icona.Avviso, "Verifica fermata", integri + " file integri su " + totale + " controllati finora.", Arancio);
            else if (integri == totale)
                vBanner.Imposta(Icona.Fatto, "Tutto integro: " + (totale == 1 ? "1 file su 1" : totale + " file su " + totale), "Gli hash coincidono con quelli salvati.", Verde);
            else
                vBanner.Imposta(Icona.Errore, "Attenzione: " + (alterati + mancanti == 1 ? "1 file diverso o mancante" : (alterati + mancanti) + " file diversi o mancanti"), "Non usare i file segnati in rosso.", Rosso);
            Procedura.AnnotaVerifica(cartella, baseDir, esito, log);
            Utente.Restituisci(Path.Combine(cartella, "VERIFICHE.txt"));
            sMsg.Text = "Verifica: " + integri + " integri su " + totale;
            Istantanea("verifica");
            if (opz.Automatico && opz.Verifica != null)
                Dispatcher.UIThread.Post(async () => { await Task.Delay(800); chiusuraConfermata = true; Close(); }, DispatcherPriority.Background);
        }

        // ================================================================== appunti e varie

        async void CopiaGriglia(DataGrid g)
        {
            var sb = new StringBuilder();
            var colonne = g.Columns.Where(c => c.IsVisible && (c.Header as string ?? "") != "").ToList();
            sb.Append(string.Join("\t", colonne.Select(c => c.Header as string))).Append('\n');
            int n = 0;
            foreach (var o in g.ItemsSource)
            {
                n++;
                sb.Append(string.Join("\t", colonne.Select(c => Valore(o, ((DataGridTemplateColumn)c).SortMemberPath, c.Header as string)))).Append('\n');
            }
            if (n > 0 && Clipboard != null) { await Clipboard.SetTextAsync(sb.ToString()); sMsg.Text = "Tabella copiata"; }
        }

        static string Valore(object o, string membro, string titolo)
        {
            var rf = o as RigaFile;
            if (rf != null)
            {
                if (membro == "Dimensione") return rf.DimensioneTesto;
                if (membro == "Modificato") return rf.ModificatoTesto;
            }
            var p = o.GetType().GetProperty(membro);
            var v = p == null ? null : p.GetValue(o);
            return v == null ? "" : v.ToString();
        }

        async void CopiaHash()
        {
            var h = griglia.SelectedItems.OfType<RigaFile>().OrderBy(r => righeFile.IndexOf(r)).Select(r => r.SHA256).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (h.Count > 0 && Clipboard != null) { await Clipboard.SetTextAsync(string.Join("\n", h)); sMsg.Text = "Hash copiato"; }
        }

        void Istantanea(string nome)
        {
            if (opz.Istantanee == null) return;
            try
            {
                Directory.CreateDirectory(opz.Istantanee);
                UpdateLayout();
                Foto.Salva(this, Path.Combine(opz.Istantanee, (++numeroFoto).ToString("00") + "-" + nome + ".png"), Math.Max(1.0, RenderScaling));
            }
            catch { }
        }

        async void Chiusura(object sender, WindowClosingEventArgs e)
        {
            if (chiuso) return;
            if (!chiusuraConfermata && opz.Anteprima == null && volume != null && (fase == Fase.Lettura || fase == Fase.Pronto || fase == Fase.Lavoro) &&
                sistema.Pronti().ContainsKey(volume.Id) && !e.IsProgrammatic)
            {
                e.Cancel = true;
                if (await Dialogo.Mostra(this, "La chiavetta è ancora collegata: è meglio scollegarla prima di chiudere.\n\nChiudere comunque?", Icona.Avviso, true))
                {
                    chiusuraConfermata = true;
                    Close();
                }
                return;
            }
            chiuso = true;
            tControllo.Stop();
            tAvanzamento.Stop();
            if (cts != null) cts.Cancel();
            if (vCts != null) vCts.Cancel();
            if (sistema != null)
            {
                bool eraAttivo = sistema.Attivo;
                try
                {
                    sistema.Chiudi(log);
                    if (eraAttivo) log.Scrivi("Protezione da scrittura USB disattivata: " + sistema.Computer + " è tornato come prima.");
                }
                catch (Exception ex) { log.Scrivi("Non è stato possibile togliere la sola lettura: " + ex.Message); }
            }
            if (registratore != null) registratore.Ferma();
        }

        // ================================================================== anteprima con dati finti

        async Task Scatta(string dir, string nome)
        {
            await Task.Delay(150);
            UpdateLayout();
            await Task.Delay(50);
            Repertum.Foto.Salva(this, Path.Combine(dir, nome + ".png"), Math.Max(1.0, RenderScaling));
        }

        async Task EseguiAnteprima(string dir)
        {
            Directory.CreateDirectory(dir);
            bool mac = OperatingSystem.IsMacOS();
            string radice = mac ? "/Volumes/KINGSTON" : "/run/repertum/sdb1";
            await Task.Delay(300);
            MostraProtezione(true);
            oraBlocco = DateTime.Now;
            ImpostaFase(Fase.Attesa);
            await Scatta(dir, "01-collega");

            volume = new Volume { Id = mac ? "disk4s1" : "sdb1", Disco = mac ? "disk4" : "sdb", Dispositivo = mac ? "/dev/disk4s1" : "/dev/sdb1", Radice = radice, DaRepertum = true };
            oraCollegamento = DateTime.Now;
            ImpostaFase(Fase.Lettura);
            var sep = Formati.Separatore;
            var finti = new[]
            {
                "DCIM/100MEDIA/IMG_0001.JPG", "DCIM/100MEDIA/IMG_0002.JPG", "DCIM/100MEDIA/IMG_0003.JPG", "DCIM/100MEDIA/VID_0004.MP4",
                "Video/registrazione 21-03.mp4", "Video/esportazione.avi", "Documenti/Contratto firmato.pdf", "Documenti/Elenco.xlsx",
                "Documenti/Note.txt", "DVR/CH01/20250321_183000.dav", "DVR/CH02/20250321_183000.dav", "DVR/Player/SmartPlayer.exe"
            }.Select(x => x.Replace('/', sep)).ToArray();
            var lista = new List<VoceFile>();
            var rnd = new Random(7);
            for (int i = 0; i < finti.Length; i++)
            {
                var ext = Path.GetExtension(finti[i]).ToLowerInvariant();
                long dim = ext == ".dav" ? 512L * 1024 * 1024 + rnd.Next(1 << 24) : ext == ".mp4" || ext == ".avi" ? 30L * 1024 * 1024 + rnd.Next(1 << 24) : ext == ".jpg" ? 3L * 1024 * 1024 + rnd.Next(1 << 20) : 12000 + rnd.Next(400000);
                lista.Add(new VoceFile
                {
                    N = i + 1, Percorso = finti[i], Assoluto = Path.Combine(radice, finti[i]), Dimensione = dim,
                    Modificato = new DateTime(2025, 3, 21, 18, 46, 0).AddMinutes(i * 7), Creato = new DateTime(2025, 3, 21, 18, 46, 0).AddMinutes(i * 7),
                    Categoria = VoceFile.CategoriaDi(ext)
                });
            }
            var i0 = new InfoSupporto
            {
                Lettera = volume.Dispositivo, Montaggio = radice, Modello = "Kingston DataTraveler 3.0", Seriale = "2CFDA1BBB4CF1A3169070A95", VidPid = "0951:1666", Interfaccia = "USB",
                CapacitaDispositivo = 61958676480L, CapacitaVolume = 61941899264L, Occupato = 4200000000L, FileSystem = "FAT32", Etichetta = "KINGSTON", SerialeVolume = "2C4F-1A0B"
            };
            var p0 = new Protezione { Protetto = true, Verificata = true, Dettaglio = mac ? "il volume è montato in sola lettura (MNT_RDONLY)" : "il disco è in sola lettura per il kernel; il volume è montato in sola lettura (ST_RDONLY)" };
            MostraSupporto(i0, p0, lista, new List<string>(), false);
            using (var sha = SHA256.Create())
            using (var md5 = MD5.Create())
                foreach (var v in lista)
                {
                    var b = Encoding.UTF8.GetBytes(Formati.PercorsoHash(v.Percorso).Replace('/', '\\'));
                    v.SHA256 = Formati.Esa(sha.ComputeHash(b));
                    v.MD5 = Formati.Esa(md5.ComputeHash(b));
                }
            for (int i = 0; i < 5; i++) { lista[i].Esito = "Letto"; AggiornaRiga(lista[i]); }
            banner.Imposta(Icona.Clessidra, "Lettura della chiavetta...", "", Blu);
            IniziaAvanzamento(lista.Count, lista.Sum(v => v.Dimensione));
            tAvanzamento.IsEnabled = false;
            inizioAttivita = DateTime.Now.AddSeconds(-12);
            fattiBytes = lista.Take(5).Sum(v => v.Dimensione);
            fattiFile = 5;
            fileCorrente = lista[5].Percorso;
            AggiornaAvanzamento();
            await Scatta(dir, "02-lettura");

            foreach (var v in lista) { v.Esito = "Letto"; AggiornaRiga(v); }
            calcoloCompleto = true;
            barra.Value = 1000;
            ImpostaFase(Fase.Pronto);
            sMsg.Text = "Hash calcolati su " + lista.Count + " file";
            await Scatta(dir, "03-pronto");

            righe[lista[11]].Sel = false;
            righe[lista[8]].Sel = false;
            AggiornaSelezione();
            var scelti = lista.Where(v => v.Selezionato).ToList();
            ImpostaFase(Fase.Lavoro);
            banner.Imposta(Icona.Clessidra, "Copia in corso...", "", Blu);
            IniziaAvanzamento(scelti.Count, 2 * scelti.Sum(v => v.Dimensione));
            tAvanzamento.IsEnabled = false;
            inizioAttivita = DateTime.Now.AddSeconds(-40);
            for (int i = 0; i < 6; i++) { scelti[i].Esito = "Copia verificata"; AggiornaRiga(scelti[i]); }
            fattiBytes = 2 * scelti.Take(6).Sum(v => v.Dimensione);
            fattiFile = 6;
            fileCorrente = scelti[6].Percorso;
            AggiornaAvanzamento();
            await Scatta(dir, "04-copia");

            foreach (var v in scelti) { v.Esito = "Copia verificata"; v.Copiato = true; AggiornaRiga(v); }
            var cartella = Path.Combine(CartellaPredefinita(), "2026-10-15_1030_KINGSTON");
            ultimaCartella = cartella;
            ImpostaFase(Fase.Pronto);
            barra.Value = 1000;
            banner.Imposta(Icona.Fatto, "Fatto: " + scelti.Count + " file copiati e verificati", "Salvati in " + cartella + "  -  ora puoi scollegare la chiavetta.", Verde);
            sMsg.Text = "Salvato in " + cartella;
            await Scatta(dir, "05-fatto");

            schede.Scegli(1);
            vtCartella.Text = cartella;
            foreach (var v in scelti) righeVerifica.Add(new RigaVerifica(v.Percorso, v.SHA256) { Esito = "INTEGRO", Calcolato = v.SHA256 });
            vBarra.Value = 1000;
            vBanner.Imposta(Icona.Fatto, "Tutto integro: " + scelti.Count + " file su " + scelti.Count, "Gli hash coincidono con quelli salvati.", Verde);
            await Scatta(dir, "06-verifica");

            righeVerifica[3].Esito = "ALTERATO";
            righeVerifica[3].Calcolato = "9f2c" + scelti[3].SHA256.Substring(4);
            vBanner.Imposta(Icona.Errore, "Attenzione: 1 file diverso o mancante", "Non usare i file segnati in rosso.", Rosso);
            await Scatta(dir, "07-verifica-errore");

            schede.Scegli(0);
            PulisciSupporto();
            ultimaCartella = null;
            daScollegare = new List<string> { mac ? "disk5" : "sdc" };
            ImpostaFase(Fase.DaScollegare);
            await Scatta(dir, "08-scollega");

            ImpostaFase(Fase.Lettura);
            var p1 = new Protezione { Protetto = false, Verificata = true, Dettaglio = "il volume è montato in scrittura" };
            foreach (var v in lista) { v.Selezionato = true; v.Esito = "Letto"; }
            MostraSupporto(i0, p1, lista, new List<string>(), false);
            calcoloCompleto = true;
            ImpostaFase(Fase.Pronto);
            sMsg.Text = "Hash calcolati su " + lista.Count + " file";
            await Scatta(dir, "09-non-protetta");

            try { using (var ic = Repertum.Foto.Icona(256)) ic.Save(Path.Combine(dir, "10-icona.png")); } catch { }
            chiusuraConfermata = true;
            Close();
        }
    }
}
