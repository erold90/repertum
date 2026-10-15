using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Repertum
{
    public class FormPrincipale : Form
    {
        enum Fase { Avvio, DaScollegare, Attesa, Lettura, Pronto, Lavoro }

        static readonly Color Verde = Color.FromArgb(0, 128, 0);
        static readonly Color Rosso = Color.FromArgb(200, 0, 0);
        static readonly Color Blu = Color.FromArgb(0, 51, 153);
        static readonly Color Grigio = Color.FromArgb(110, 110, 110);
        static readonly Color Arancio = Color.FromArgb(190, 100, 0);
        const Algoritmi Alg = Algoritmi.SHA256 | Algoritmi.MD5;

        readonly Opzioni opz;
        readonly Registro log = new Registro();
        int righeAvvio, rigaCollegamento, numeroFoto;

        // interfaccia
        TabControl schede;
        TabPage pChiavetta, pVerifica;
        Banner banner, vBanner;
        readonly Dictionary<string, Label> valori = new Dictionary<string, Label>();
        CheckBox cTutti;
        Label lSelezione;
        LinkLabel lDestinazione;
        DataGridView griglia, vGriglia;
        ProgressBar barra, vBarra;
        Button bSalva, bApri, vbVerifica;
        TextBox vtCartella;
        ToolStripStatusLabel sMsg, sProt;
        Bitmap imgSalva, imgFerma, imgApri, imgVerifica;

        // stato
        Fase fase = Fase.Avvio;
        HashSet<string> conosciute = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> daScollegare = new List<string>();
        string lettera, destinazione, ultimaCartella;
        InfoSupporto info;
        Protezione prot;
        DateTime oraBlocco, oraCollegamento, inizioCalcolo, fineCalcolo;
        List<VoceFile> voci = new List<VoceFile>();
        readonly Dictionary<VoceFile, DataGridViewRow> righe = new Dictionary<VoceFile, DataGridViewRow>();
        bool calcoloCompleto, verificaInCorso;
        CancellationTokenSource cts, vCts;
        int generazione;
        readonly System.Windows.Forms.Timer tControllo = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly System.Windows.Forms.Timer tAvanzamento = new System.Windows.Forms.Timer { Interval = 250 };
        long totBytes, fattiBytes;
        int totFile, fattiFile;
        volatile string fileCorrente = "";
        DateTime inizioAttivita;

        readonly float scala;
        int S(int v) { return (int)Math.Round(v * scala); }

        public FormPrincipale(Opzioni opz)
        {
            this.opz = opz;
            scala = DeviceDpi / 96f;
            destinazione = DestinazioneSalvata();
            int li = S(22);
            imgSalva = Icone.Immagine(Icona.Salva, li);
            imgFerma = Icone.Immagine(Icona.Ferma, li);
            imgApri = Icone.Immagine(Icona.Cartella, li);
            imgVerifica = Icone.Immagine(Icona.Scudo, li);

            SuspendLayout();
            Text = "Repertum - copia e hash di chiavette USB";
            Font = new Font("Segoe UI", 9F);
            Size = new Size(1100, 740);
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            schede = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 5) };
            pChiavetta = new TabPage("Chiavetta USB") { UseVisualStyleBackColor = true, Padding = new Padding(8) };
            pVerifica = new TabPage("Verifica") { UseVisualStyleBackColor = true, Padding = new Padding(8) };
            schede.TabPages.AddRange(new[] { pChiavetta, pVerifica });
            CostruisciChiavetta();
            CostruisciVerifica();
            CostruisciBarraStato();
            Controls.Add(schede);
            schede.BringToFront();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();

            if (opz.Istantanee != null)
                log.Nuova += r => { try { Directory.CreateDirectory(opz.Istantanee); File.AppendAllText(Path.Combine(opz.Istantanee, "registro.txt"), r + "\r\n", new UTF8Encoding(true)); } catch { } };
            tControllo.Tick += (s, e) => ControllaUnita();
            tAvanzamento.Tick += (s, e) => AggiornaAvanzamento();
            Shown += (s, e) => Avvio();
            FormClosing += Chiusura;
        }

        // ================================================================== costruzione

        void CostruisciBarraStato()
        {
            var st = new StatusStrip { SizingGrip = true };
            sMsg = new ToolStripStatusLabel("Avvio...") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            sProt = new ToolStripStatusLabel("Blocco scrittura USB: -") { BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched };
            st.Items.AddRange(new ToolStripItem[] { sMsg, sProt });
            Controls.Add(st);
        }

        Button Pulsante(string testo, string nome, Bitmap img, int larghezza, int altezza, EventHandler clic, bool grande = false)
        {
            var b = new Button
            {
                Text = testo, Name = nome, Image = img, Size = new Size(larghezza, altezza),
                ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 4, 8, 4), UseVisualStyleBackColor = true
            };
            if (grande) b.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            b.Click += clic;
            return b;
        }

        void CostruisciChiavetta()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(78)));    // intestazione
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(76)));    // dati della chiavetta
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // seleziona tutti
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // elenco dei file
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(24)));    // barra
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // pulsanti

            banner = new Banner { Name = "banner", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            t.Controls.Add(banner, 0, 0);

            var g = new GroupBox { Text = "Chiavetta", Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 0, 4) };
            var ti = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2 };
            foreach (var p in new[] { 0f, 40f, 0f, 30f, 0f, 30f })
                ti.ColumnStyles.Add(p == 0 ? new ColumnStyle(SizeType.AutoSize) : new ColumnStyle(SizeType.Percent, p));
            ti.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            ti.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            string[] chiavi = { "Modello", "Capacità", "File system", "Numero di serie", "Sola lettura", "Contenuto" };
            for (int i = 0; i < chiavi.Length; i++)
            {
                int col = (i % 3) * 2, riga = i / 3;
                ti.Controls.Add(new Label { Text = chiavi[i] + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(col == 0 ? 0 : 14, 0, 6, 0) }, col, riga);
                var v = new Label { Text = "-", AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
                valori[chiavi[i]] = v;
                ti.Controls.Add(v, col + 1, riga);
            }
            g.Controls.Add(ti);
            t.Controls.Add(g, 0, 1);

            var fSel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(2, 2, 0, 2) };
            cTutti = new CheckBox { Text = "Seleziona tutti i file", Name = "cTutti", AutoSize = true, AutoCheck = false, Margin = new Padding(3, 3, 0, 3), Enabled = false };
            cTutti.Click += (s, e) => { bool tutti = !voci.All(v => v.Selezionato); Seleziona(v => tutti); };
            lSelezione = new Label { AutoSize = true, Margin = new Padding(18, 6, 0, 0), ForeColor = Color.FromArgb(64, 64, 64) };
            fSel.Controls.AddRange(new Control[] { cTutti, lSelezione });
            t.Controls.Add(fSel, 0, 2);

            griglia = CreaGriglia("griglia");
            griglia.ReadOnly = false;
            griglia.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sel", HeaderText = "", Width = S(30), Resizable = DataGridViewTriState.False });
            Colonna(griglia, "Nome", "Nome file", 230, typeof(string));
            Colonna(griglia, "Cartella", "Cartella", 140, typeof(string));
            Colonna(griglia, "Dimensione", "Dimensione", 90, typeof(long), true);
            Colonna(griglia, "Esito", "Stato", 130, typeof(string));
            ColonnaHash(griglia, "SHA256", "SHA-256", 470);
            ColonnaHash(griglia, "MD5", "MD5", 250);
            Colonna(griglia, "Modificato", "Ultima modifica", 130, typeof(DateTime));
            griglia.Columns["Modificato"].DefaultCellStyle.Format = "dd/MM/yyyy HH:mm:ss";
            foreach (DataGridViewColumn c in griglia.Columns) c.ReadOnly = c.Name != "Sel";
            griglia.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var col = griglia.Columns[e.ColumnIndex].Name;
                if (col == "Dimensione" && e.Value is long) { e.Value = Formati.Byte((long)e.Value); e.FormattingApplied = true; }
                if (col == "Esito" && e.Value is string)
                {
                    var t2 = (string)e.Value;
                    bool ok = t2 == "Copia verificata", ko = t2.StartsWith("NON") || t2.StartsWith("COPIA") || t2.StartsWith("ORIGINALE");
                    e.CellStyle.ForeColor = ok ? Verde : ko ? Rosso : Color.Black;
                    if (ok || ko) e.CellStyle.Font = new Font(griglia.Font, FontStyle.Bold);
                }
            };
            griglia.CurrentCellDirtyStateChanged += (s, e) => { if (griglia.IsCurrentCellDirty) griglia.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            griglia.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || griglia.Columns[e.ColumnIndex].Name != "Sel") return;
                var v = griglia.Rows[e.RowIndex].Tag as VoceFile;
                if (v != null) { v.Selezionato = (bool)(griglia.Rows[e.RowIndex].Cells["Sel"].Value ?? false); AggiornaSelezione(); }
            };
            var m = new ContextMenuStrip();
            m.Items.Add("Copia l'hash SHA-256", null, (s, e) => CopiaHash());
            m.Items.Add("Copia tutta la tabella", null, (s, e) => CopiaGriglia(griglia));
            griglia.ContextMenuStrip = m;
            t.Controls.Add(griglia, 0, 3);

            barra = Barra();
            t.Controls.Add(barra, 0, 4);

            var basso = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 4, 0, 0) };
            basso.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            basso.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            basso.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bSalva = Pulsante("Salva copia e hash", "bSalva", imgSalva, S(240), S(42), (s, e) => Salva(), true);
            bApri = Pulsante("Apri cartella", "bApri", imgApri, S(150), S(42), (s, e) => { if (ultimaCartella != null) ApriCartella(ultimaCartella); });
            bSalva.Enabled = bApri.Enabled = false;
            var fDest = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(0, 0, 4, 0) };
            fDest.Controls.Add(new Label { Text = "Salva in:", AutoSize = true, Margin = new Padding(0, 1, 4, 0) });
            lDestinazione = new LinkLabel { Text = destinazione, Name = "lDestinazione", AutoSize = true, Margin = new Padding(0, 1, 0, 0) };
            lDestinazione.LinkClicked += (s, e) => CambiaDestinazione();
            new ToolTip().SetToolTip(lDestinazione, "Clic per scegliere un'altra cartella");
            fDest.Controls.Add(lDestinazione);
            basso.Controls.Add(bSalva, 0, 0);
            basso.Controls.Add(bApri, 1, 0);
            basso.Controls.Add(fDest, 2, 0);
            t.Controls.Add(basso, 0, 5);

            pChiavetta.Controls.Add(t);
        }

        ProgressBar Barra()
        {
            var b = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Blocks, Maximum = 1000, Margin = new Padding(0, 3, 0, 3) };
            b.HandleCreated += (s, e) => { try { Nativo.SetWindowTheme(b.Handle, "", ""); } catch { } };   // barra classica a blocchi blu
            return b;
        }

        DataGridView CreaGriglia(string nome)
        {
            var g = new DataGridView
            {
                Name = nome,
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                GridColor = Color.FromArgb(200, 200, 200),
                BorderStyle = BorderStyle.Fixed3D,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ReadOnly = true
            };
            g.RowTemplate.Height = S(22);
            g.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            g.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(g, true, null);
            return g;
        }

        void Colonna(DataGridView g, string nome, string titolo, int larghezza, Type tipo, bool destra = false)
        {
            var c = new DataGridViewTextBoxColumn { Name = nome, HeaderText = titolo, Width = S(larghezza), ValueType = tipo, SortMode = DataGridViewColumnSortMode.Automatic };
            if (destra) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            g.Columns.Add(c);
        }

        void ColonnaHash(DataGridView g, string nome, string titolo, int larghezza)
        {
            Colonna(g, nome, titolo, larghezza, typeof(string));
            g.Columns[nome].DefaultCellStyle.Font = new Font("Consolas", 9F);
        }

        void CostruisciVerifica()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(78)));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, S(24)));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            vBanner = new Banner { Name = "vBanner", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            vBanner.Imposta(Icona.Scudo, "Verifica una copia", "Ricontrolla che i file salvati da Repertum non siano cambiati.", Blu);
            t.Controls.Add(vBanner, 0, 0);

            var riga = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 2, 0, 4) };
            riga.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            riga.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            riga.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            riga.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            vtCartella = new TextBox { Name = "vtCartella", Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(6, 0, 6, 0) };
            var vbSfoglia = new Button { Text = "Sfoglia...", Name = "vbSfoglia", Size = new Size(S(96), S(28)), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0), UseVisualStyleBackColor = true };
            vbSfoglia.Click += (s, e) =>
            {
                var d = ScegliCartella("Scegli la cartella salvata da Repertum", Directory.Exists(destinazione) ? destinazione : null);
                if (d != null) { vtCartella.Text = d; AvviaVerifica(); }
            };
            vbVerifica = Pulsante("Verifica", "vbVerifica", imgVerifica, S(150), S(42), (s, e) => AvviaVerifica(), true);
            riga.Controls.Add(new Label { Text = "Cartella:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            riga.Controls.Add(vtCartella, 1, 0);
            riga.Controls.Add(vbSfoglia, 2, 0);
            riga.Controls.Add(vbVerifica, 3, 0);
            t.Controls.Add(riga, 0, 1);

            vBarra = Barra();
            t.Controls.Add(vBarra, 0, 2);

            vGriglia = CreaGriglia("vGriglia");
            Colonna(vGriglia, "Nome", "File", 320, typeof(string));
            Colonna(vGriglia, "Esito", "Esito", 110, typeof(string));
            ColonnaHash(vGriglia, "Atteso", "SHA-256 salvato", 470);
            ColonnaHash(vGriglia, "Calcolato", "SHA-256 di adesso", 470);
            vGriglia.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || !(e.Value is string) || vGriglia.Columns[e.ColumnIndex].Name != "Esito") return;
                var v = (string)e.Value;
                if (v == "INTEGRO") e.CellStyle.ForeColor = Verde;
                else if (v == "ALTERATO" || v == "MANCANTE" || v == "ILLEGGIBILE") e.CellStyle.ForeColor = Rosso;
                e.CellStyle.Font = new Font(vGriglia.Font, FontStyle.Bold);
            };
            var m = new ContextMenuStrip();
            m.Items.Add("Copia tutta la tabella", null, (s, e) => CopiaGriglia(vGriglia));
            vGriglia.ContextMenuStrip = m;
            t.Controls.Add(vGriglia, 0, 3);
            pVerifica.Controls.Add(t);
        }

        // ================================================================== avvio, unita', fasi

        void Avvio()
        {
            log.Scrivi("Repertum per Windows " + Versione.Breve + " avviato su " + Environment.MachineName + ", utente " + Environment.UserName + ".");
            if (opz.Anteprima != null) { EseguiAnteprima(opz.Anteprima); return; }
            try
            {
                Blocco.Attiva();
                oraBlocco = DateTime.Now;
                log.Scrivi("Protezione da scrittura USB attivata (StorageDevicePolicies\\WriteProtect = 1)." +
                           (Blocco.SessionePrecedenteInterrotta ? " La sessione precedente si era chiusa senza disattivarla." : ""));
                MostraProtezione(true);
            }
            catch (Exception ex)
            {
                MostraProtezione(false);
                log.Scrivi("Protezione da scrittura NON attivata: " + ex.Message);
                MessageBox.Show(this, "Non è stato possibile attivare la sola lettura:\n" + ex.Message +
                                "\n\nIl programma funziona, ma Windows potrà scrivere sulla chiavetta.", "Repertum",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            righeAvvio = log.Conteggio;
            var pronte = Dischi.Pronte();
            conosciute = new HashSet<string>(pronte.Keys, StringComparer.OrdinalIgnoreCase);
            var exe = (Path.GetPathRoot(Application.ExecutablePath) ?? "").TrimEnd('\\');
            daScollegare = pronte.Where(kv => kv.Value == DriveType.Removable && !kv.Key.Equals(exe, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            if (daScollegare.Count > 0) log.Scrivi("All'avvio era già collegato un supporto rimovibile (" + string.Join(", ", daScollegare) + "): va scollegato e ricollegato.");
            ImpostaFase(daScollegare.Count > 0 ? Fase.DaScollegare : Fase.Attesa);
            tControllo.Start();
            Istantanea("avvio");
            if (opz.Verifica != null) { schede.SelectedTab = pVerifica; vtCartella.Text = opz.Verifica; AvviaVerifica(); }
        }

        void MostraProtezione(bool attiva)
        {
            banner.Protezione(attiva ? "Blocco scrittura attivo" : "Blocco scrittura NON attivo", attiva ? Verde : Rosso, attiva);
            sProt.Text = "Blocco scrittura USB: " + (attiva ? "attivo" : "NON attivo");
            sProt.ForeColor = attiva ? Verde : Rosso;
        }

        void ControllaUnita()
        {
            Dictionary<string, DriveType> ora;
            try { ora = Dischi.Pronte(); } catch { return; }
            switch (fase)
            {
                case Fase.DaScollegare:
                    if (!daScollegare.Any(ora.ContainsKey))
                    {
                        conosciute = new HashSet<string>(ora.Keys, StringComparer.OrdinalIgnoreCase);
                        log.Scrivi("Il supporto collegato prima dell'avvio è stato scollegato.");
                        ImpostaFase(Fase.Attesa);
                    }
                    break;
                case Fase.Attesa:
                    conosciute.IntersectWith(ora.Keys);
                    var nuova = ora.Keys.FirstOrDefault(k => !conosciute.Contains(k));
                    if (nuova != null) Collega(nuova);
                    break;
                case Fase.Lettura:
                case Fase.Pronto:
                case Fase.Lavoro:
                    if (lettera != null && !ora.ContainsKey(lettera)) Scollegata();
                    break;
            }
        }

        string TestoSalva() { return voci.Any(v => v.Selezionato) || voci.Count == 0 ? "Salva copia e hash" : "Salva solo gli hash"; }

        void ImpostaFase(Fase f)
        {
            fase = f;
            bool pronto = f == Fase.Pronto && calcoloCompleto;
            cTutti.Enabled = pronto && voci.Count > 0;
            griglia.Columns["Sel"].ReadOnly = f != Fase.Pronto;
            bSalva.Text = f == Fase.Lavoro ? "Ferma" : TestoSalva();
            bSalva.Image = f == Fase.Lavoro ? imgFerma : imgSalva;
            bSalva.Enabled = (pronto && voci.Count > 0) || f == Fase.Lavoro;
            bApri.Enabled = ultimaCartella != null && f != Fase.Lavoro;
            lDestinazione.Enabled = f != Fase.Lavoro;
            if (f != Fase.Lettura && f != Fase.Lavoro) tAvanzamento.Enabled = false;

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
                banner.Imposta(Icona.Errore, "Attenzione: la chiavetta NON è in sola lettura", "Scollegala, riavvia Windows e riprova.", Rosso);
            else if (voci.Count == 0)
                banner.Imposta(Icona.Usb, "La chiavetta è vuota", "Non ci sono file da salvare.", Grigio);
            else if (letti < voci.Count)
                banner.Imposta(Icona.Avviso, "Chiavetta letta: " + (voci.Count - letti) + " file non leggibili", "Spunta i file da salvare e premi «Salva copia e hash».", Arancio);
            else
                banner.Imposta(Icona.Usb, "Chiavetta letta: " + quanti + " (" + Formati.Byte(voci.Sum(v => v.Dimensione)) + ")", "Spunta i file da salvare e premi «Salva copia e hash».", Blu);
        }

        // ================================================================== collegamento della chiavetta

        void Collega(string l)
        {
            lettera = l;
            oraCollegamento = DateTime.Now;
            generazione++;
            int gen = generazione;
            rigaCollegamento = log.Conteggio;
            log.Scrivi("Supporto collegato: unità " + l);
            PulisciSupporto();
            ImpostaFase(Fase.Lettura);
            banner.Imposta(Icona.Clessidra, "Lettura della chiavetta...", "Riconoscimento in corso.", Blu);
            sMsg.Text = "Riconoscimento della chiavetta " + l;
            Task.Run(() =>
            {
                try
                {
                    var i = Dischi.Descrivi(l);
                    var p = Dischi.Controlla(l);
                    var err = new List<string>();
                    var lista = Elenco.Leggi(l + "\\", err);
                    BeginInvoke((Action)(() => { if (gen == generazione) { MostraSupporto(i, p, lista, err); AvviaCalcolo(); } }));
                }
                catch (Exception ex)
                {
                    log.Scrivi("Errore nella lettura del supporto: " + ex);
                    BeginInvoke((Action)(() => { if (gen == generazione) banner.Imposta(Icona.Errore, "Non riesco a leggere la chiavetta", ex.Message, Rosso); }));
                }
            });
        }

        void PulisciSupporto()
        {
            voci = new List<VoceFile>();
            righe.Clear();
            griglia.Rows.Clear();
            info = null;
            prot = null;
            calcoloCompleto = false;
            foreach (var l in valori.Values) { l.Text = "-"; l.ForeColor = Color.Black; }
            barra.Value = 0;
            totFile = 0;
            totBytes = 0;
            lSelezione.Text = "";
            cTutti.CheckState = CheckState.Unchecked;
        }

        void ImpostaValore(string chiave, string testo, Color? colore = null)
        {
            Label l;
            if (!valori.TryGetValue(chiave, out l)) return;
            l.Text = string.IsNullOrEmpty(testo) ? "-" : testo;
            l.ForeColor = colore ?? Color.Black;
        }

        void MostraSupporto(InfoSupporto i, Protezione p, List<VoceFile> lista, List<string> errori)
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
            foreach (var kv in i.Righe()) log.Scrivi(kv.Key + ": " + kv.Value);
            log.Scrivi("Controllo della protezione: " + (p.Protetto ? "SOLA LETTURA - " : "NON CONFERMATA - ") + p.Dettaglio + ".");
            log.Scrivi("Contenuto: " + lista.Count + " file, " + Formati.ByteEsatti(lista.Sum(v => v.Dimensione)) + ".");
            foreach (var e in errori) log.Scrivi("Cartella non leggibile: " + e);

            var nuove = new List<DataGridViewRow>();
            foreach (var v in lista)
            {
                var r = new DataGridViewRow();
                r.CreateCells(griglia, v.Selezionato, v.Nome, v.Cartella, v.Dimensione, "", null, null, v.Modificato);
                r.Tag = v;
                righe[v] = r;
                nuove.Add(r);
            }
            griglia.Rows.AddRange(nuove.ToArray());
            griglia.ClearSelection();
            griglia.CurrentCell = null;
            AggiornaSelezione();
            Istantanea("rilevata");

            if (!p.Protetto && Blocco.Attivo)
            {
                MessageBox.Show(this, "ATTENZIONE: la chiavetta NON risulta in sola lettura.\n(" + p.Dettaglio + ")\n\n" +
                                "Se è la prima volta che usi Repertum su questo PC, scollega la chiavetta, riavvia Windows e riprova.",
                                "Repertum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Scollegata()
        {
            bool inCorso = fase == Fase.Lettura || fase == Fase.Lavoro;
            generazione++;
            if (cts != null) cts.Cancel();
            log.Scrivi("Supporto " + lettera + " scollegato" + (inCorso ? " DURANTE il lavoro: operazione interrotta." : "."));
            log.Chiudi();
            lettera = null;
            PulisciSupporto();
            conosciute = new HashSet<string>(Dischi.Pronte().Keys, StringComparer.OrdinalIgnoreCase);
            ImpostaFase(Fase.Attesa);
            if (ultimaCartella != null && !inCorso) sMsg.Text = "Ultimo salvataggio: " + ultimaCartella;
            Istantanea("scollegata");
            if (inCorso)
                MessageBox.Show(this, "La chiavetta è stata scollegata durante il lavoro: operazione interrotta.", "Repertum",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            tAvanzamento.Enabled = true;
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
            banner.Sotto = string.Format(Formati.It, "{0:N0} di {1:N0} file  -  {2:N0}%{3}", Math.Min(fattiFile, totFile), totFile, quota * 100, resta);
            sMsg.Text = (fase == Fase.Lavoro ? "Copia: " : "Lettura: ") + fileCorrente;
        }

        void AvviaCalcolo()
        {
            if (voci.Count == 0) { calcoloCompleto = true; fineCalcolo = DateTime.Now; ImpostaFase(Fase.Pronto); return; }
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
                BeginInvoke((Action)(() => { if (gen == generazione) FineCalcolo(interrotto); }));
            });
        }

        // A ogni file finito: la riga e, subito, anche «N di M» e la percentuale dell'intestazione,
        // che altrimenti resterebbero indietro di un giro del timer quando i file sono piccoli.
        void FileFinito(VoceFile v, int gen)
        {
            Interlocked.Increment(ref fattiFile);
            BeginInvoke((Action)(() => { if (gen == generazione) { AggiornaRiga(v); AggiornaAvanzamento(); } }));
        }

        void AggiornaRiga(VoceFile v)
        {
            DataGridViewRow r;
            if (!righe.TryGetValue(v, out r)) return;
            r.Cells["MD5"].Value = v.MD5;
            r.Cells["SHA256"].Value = v.SHA256;
            r.Cells["Esito"].Value = v.Esito;
        }

        void FineCalcolo(bool interrotto)
        {
            fineCalcolo = DateTime.Now;
            AggiornaAvanzamento();
            calcoloCompleto = !interrotto;
            int letti = voci.Count(v => v.SHA256 != null);
            if (Calcolo.Md5Disponibile == false) log.Scrivi("MD5 non disponibile su questo PC (criterio FIPS di Windows).");
            log.Scrivi(interrotto ? "Calcolo degli hash interrotto." : "Fine del calcolo degli hash: " + letti + " file su " + voci.Count + ".");
            barra.Value = interrotto ? barra.Value : 1000;
            ImpostaFase(Fase.Pronto);
            sMsg.Text = interrotto ? "Lettura interrotta" : "Hash calcolati su " + letti + " file";
            Istantanea("pronto");
        }

        // ================================================================== selezione

        void Seleziona(Func<VoceFile, bool> criterio)
        {
            foreach (var v in voci)
            {
                v.Selezionato = criterio(v);
                DataGridViewRow r;
                if (righe.TryGetValue(v, out r)) r.Cells["Sel"].Value = v.Selezionato;
            }
            AggiornaSelezione();
        }

        void AggiornaSelezione()
        {
            var s = voci.Where(v => v.Selezionato).ToList();
            lSelezione.Text = voci.Count == 0 ? "" : string.Format(Formati.It, "{0:N0} di {1:N0} file selezionati  ({2})", s.Count, voci.Count, Formati.Byte(s.Sum(v => v.Dimensione)));
            cTutti.CheckState = s.Count == 0 ? CheckState.Unchecked : s.Count == voci.Count ? CheckState.Checked : CheckState.Indeterminate;
            if (fase != Fase.Lavoro) bSalva.Text = TestoSalva();
        }

        // ================================================================== salvataggio

        static string CartellaPredefinita()
        {
            return (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\Repertum";
        }

        static string DestinazioneSalvata()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Repertum"))
                {
                    var d = k == null ? null : k.GetValue("Destinazione") as string;
                    return string.IsNullOrEmpty(d) ? CartellaPredefinita() : d;
                }
            }
            catch { return CartellaPredefinita(); }
        }

        void CambiaDestinazione()
        {
            var d = ScegliCartella("Dove salvare le copie e gli hash?", Directory.Exists(destinazione) ? destinazione : null, true);
            if (d == null) return;
            destinazione = d;
            lDestinazione.Text = d;
            try { using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Repertum")) k.SetValue("Destinazione", d); } catch { }
        }

        void Salva()
        {
            if (fase == Fase.Lavoro) { if (cts != null) cts.Cancel(); return; }
            bool conCopia = voci.Any(v => v.Selezionato && v.SHA256 != null);
            var scelti = conCopia ? voci.Where(v => v.Selezionato && v.SHA256 != null).ToList() : voci.Where(v => v.SHA256 != null).ToList();
            if (scelti.Count == 0) return;
            long serve = conCopia ? scelti.Sum(v => v.Dimensione) : 0;
            try
            {
                var radice = Path.GetPathRoot(Path.GetFullPath(destinazione));
                if (lettera != null && radice.TrimEnd('\\').Equals(lettera, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "Non si può salvare sulla chiavetta stessa: scegli un'altra cartella con «Salva in».", "Repertum", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Directory.CreateDirectory(destinazione);
                long libero = new DriveInfo(radice).AvailableFreeSpace;
                if (serve + 16L * 1024 * 1024 > libero)
                {
                    MessageBox.Show(this, "Spazio insufficiente in " + destinazione + ":\nservono " + Formati.Byte(serve) + ", liberi " + Formati.Byte(libero) +
                                    ".\n\nScegli un'altra cartella con «Salva in».", "Repertum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Non riesco a usare la cartella " + destinazione + ":\n" + ex.Message, "Repertum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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
            var i0 = info; var p0 = prot;
            Task.Run(() =>
            {
                int buone = 0;
                foreach (var v in tutte) { v.Copiato = false; v.InElenco = false; if (v.SHA256 != null) v.Esito = "Letto"; }
                if (conCopia)
                    buone = Procedura.CopiaEVerifica(scelti, cartella, log, n => Interlocked.Add(ref fattiBytes, n),
                                                     v => fileCorrente = v.Percorso, v => FileFinito(v, gen), token);
                else foreach (var v in scelti) v.InElenco = true;

                bool interrotto = token.IsCancellationRequested;
                var tempi = Allegati.Tempi(oraBlocco, oraCollegamento, inizioCalcolo, fineCalcolo, conCopia, inizio, DateTime.Now);
                var protezione = new List<string>();
                if (Blocco.Attivo)
                {
                    protezione.Add("Criterio di Windows StorageDevicePolicies\\WriteProtect attivato alle " + Formati.Data(oraBlocco) + ",");
                    protezione.Add("prima del collegamento della chiavetta (" + Formati.Data(oraCollegamento) + ").");
                }
                else protezione.Add("Protezione da scrittura NON attiva.");
                if (p0 != null)
                    protezione.Add(p0.Protetto ? "Controllo sulla chiavetta, senza scriverci: " + p0.Dettaglio + "."
                                               : "ATTENZIONE: il controllo sulla chiavetta NON ha confermato la sola lettura (" + p0.Dettaglio + ").");
                if (interrotto) protezione.Add("NOTA: copia interrotta prima della fine.");
                Allegati.Scrivi(cartella, i0, protezione, tutte, Alg, tempi, conCopia, log);
                int totale = scelti.Count;
                BeginInvoke((Action)(() => { if (gen == generazione) FineLavoro(cartella, conCopia, interrotto, buone, totale); }));
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

        string ScegliCartella(string descrizione, string iniziale, bool nuova = false)
        {
            using (var d = new FolderBrowserDialog { Description = descrizione, ShowNewFolderButton = nuova })
            {
                if (!string.IsNullOrEmpty(iniziale) && Directory.Exists(iniziale)) d.SelectedPath = iniziale;
                return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
            }
        }

        void AvviaVerifica()
        {
            if (verificaInCorso) { if (vCts != null) vCts.Cancel(); return; }
            var cartella = vtCartella.Text.Trim().Trim('"');
            if (cartella.Length == 0 || !Directory.Exists(cartella))
            {
                var d = ScegliCartella("Scegli la cartella salvata da Repertum", Directory.Exists(destinazione) ? destinazione : null);
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
                var d = ScegliCartella("Qui ci sono solo gli hash: scegli la chiavetta o la cartella con i file da confrontare.", null);
                if (d == null) return;
                baseDir = d;
            }
            vGriglia.Rows.Clear();
            var elenco = sha.ToList();
            var righeV = new List<DataGridViewRow>();
            foreach (var kv in elenco)
            {
                var r = new DataGridViewRow();
                r.CreateCells(vGriglia, kv.Key, "...", kv.Value, "");
                righeV.Add(r);
            }
            vGriglia.Rows.AddRange(righeV.ToArray());
            vGriglia.ClearSelection();
            vGriglia.CurrentCell = null;
            verificaInCorso = true;
            vbVerifica.Text = "Ferma";
            vbVerifica.Image = imgFerma;
            vBarra.Value = 0;
            vBanner.Imposta(Icona.Clessidra, "Verifica in corso...", cartella, Blu);
            vCts = new CancellationTokenSource();
            var token = vCts.Token;
            long totale = Procedura.DaVerificare(elenco, baseDir);
            Task.Run(() =>
            {
                var esito = Procedura.Verifica(elenco, md5, baseDir, (riga, calcolato, e, f) => BeginInvoke((Action)(() =>
                {
                    var r = vGriglia.Rows[riga];
                    r.Cells["Calcolato"].Value = calcolato;
                    r.Cells["Esito"].Value = e;
                    vBarra.Value = (int)Math.Min(1000, f * 1000 / totale);
                })), token);
                BeginInvoke((Action)(() => FineVerifica(cartella, baseDir, esito)));
            });
        }

        void FineVerifica(string cartella, string baseDir, EsitoVerifica esito)
        {
            int totale = esito.Totale, integri = esito.Integri, alterati = esito.Alterati, mancanti = esito.Mancanti;
            bool interrotto = esito.Interrotta;
            verificaInCorso = false;
            vbVerifica.Text = "Verifica";
            vbVerifica.Image = imgVerifica;
            if (!interrotto) vBarra.Value = 1000;
            if (interrotto)
                vBanner.Imposta(Icona.Avviso, "Verifica fermata", integri + " file integri su " + totale + " controllati finora.", Arancio);
            else if (integri == totale)
                vBanner.Imposta(Icona.Fatto, "Tutto integro: " + (totale == 1 ? "1 file su 1" : totale + " file su " + totale), "Gli hash coincidono con quelli salvati.", Verde);
            else
                vBanner.Imposta(Icona.Errore, "Attenzione: " + (alterati + mancanti == 1 ? "1 file diverso o mancante" : (alterati + mancanti) + " file diversi o mancanti"), "Non usare i file segnati in rosso.", Rosso);
            Procedura.AnnotaVerifica(cartella, baseDir, esito, log);
            sMsg.Text = "Verifica: " + integri + " integri su " + totale;
            Istantanea("verifica");
        }

        // ================================================================== appunti e varie

        void CopiaGriglia(DataGridView g)
        {
            var sb = new StringBuilder();
            var colonne = g.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && c.Name != "Sel").OrderBy(c => c.DisplayIndex).ToList();
            sb.AppendLine(string.Join("\t", colonne.Select(c => c.HeaderText)));
            foreach (DataGridViewRow r in g.Rows)
                sb.AppendLine(string.Join("\t", colonne.Select(c => r.Cells[c.Index].FormattedValue as string ?? "")));
            if (g.Rows.Count > 0) { Clipboard.SetText(sb.ToString()); sMsg.Text = "Tabella copiata"; }
        }

        void CopiaHash()
        {
            var h = griglia.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Cells["SHA256"].Value as string).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (h.Count > 0) { Clipboard.SetText(string.Join("\r\n", h)); sMsg.Text = "Hash copiato"; }
        }

        static void ApriCartella(string c)
        {
            try { Directory.CreateDirectory(c); Process.Start("explorer.exe", "\"" + c + "\""); } catch { }
        }

        void Istantanea(string nome)
        {
            if (opz.Istantanee == null) return;
            try
            {
                Directory.CreateDirectory(opz.Istantanee);
                Foto.Salva(this, Path.Combine(opz.Istantanee, (++numeroFoto).ToString("00") + "-" + nome + ".png"));
            }
            catch { }
        }

        void Chiusura(object sender, FormClosingEventArgs e)
        {
            if (opz.Anteprima == null && lettera != null && (fase == Fase.Lettura || fase == Fase.Pronto || fase == Fase.Lavoro) &&
                Dischi.Pronte().ContainsKey(lettera) && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(this, "La chiavetta è ancora collegata: è meglio scollegarla prima di chiudere.\n\nChiudere comunque?",
                                    "Repertum", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            tControllo.Stop();
            tAvanzamento.Stop();
            if (cts != null) cts.Cancel();
            if (vCts != null) vCts.Cancel();
            if (Blocco.Attivo)
            {
                try { Blocco.Ripristina(); log.Scrivi("Protezione da scrittura USB disattivata: il PC è tornato come prima."); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Non è stato possibile togliere la sola lettura:\n" + ex.Message +
                                    "\n\nRiapri e richiudi il programma.", "Repertum", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================================================================== anteprima con dati finti

        void EseguiAnteprima(string dir)
        {
            Directory.CreateDirectory(dir);
            Action<string> foto = nomeFoto => { Application.DoEvents(); Thread.Sleep(150); Application.DoEvents(); Foto.Salva(this, Path.Combine(dir, nomeFoto + ".png")); };
            MostraProtezione(true);
            oraBlocco = DateTime.Now;
            ImpostaFase(Fase.Attesa);
            foto("01-collega");

            lettera = "E:";
            oraCollegamento = DateTime.Now;
            ImpostaFase(Fase.Lettura);
            var finti = new[]
            {
                "DCIM\\100MEDIA\\IMG_0001.JPG", "DCIM\\100MEDIA\\IMG_0002.JPG", "DCIM\\100MEDIA\\IMG_0003.JPG", "DCIM\\100MEDIA\\VID_0004.MP4",
                "Video\\registrazione 21-03.mp4", "Video\\esportazione.avi", "Documenti\\Contratto firmato.pdf", "Documenti\\Elenco.xlsx",
                "Documenti\\Note.txt", "DVR\\CH01\\20250321_183000.dav", "DVR\\CH02\\20250321_183000.dav", "DVR\\Player\\SmartPlayer.exe"
            };
            var lista = new List<VoceFile>();
            var rnd = new Random(7);
            for (int i = 0; i < finti.Length; i++)
            {
                var ext = Path.GetExtension(finti[i]).ToLowerInvariant();
                long dim = ext == ".dav" ? 512L * 1024 * 1024 + rnd.Next(1 << 24) : ext == ".mp4" || ext == ".avi" ? 30L * 1024 * 1024 + rnd.Next(1 << 24) : ext == ".jpg" ? 3L * 1024 * 1024 + rnd.Next(1 << 20) : 12000 + rnd.Next(400000);
                lista.Add(new VoceFile
                {
                    N = i + 1, Percorso = finti[i], Assoluto = "E:\\" + finti[i], Dimensione = dim,
                    Modificato = new DateTime(2025, 3, 21, 18, 46, 0).AddMinutes(i * 7), Creato = new DateTime(2025, 3, 21, 18, 46, 0).AddMinutes(i * 7),
                    Categoria = VoceFile.CategoriaDi(ext)
                });
            }
            var i0 = new InfoSupporto { Lettera = "E:\\", Modello = "Kingston DataTraveler 3.0 USB Device", Seriale = "2CFDA1BBB4CF1A3169070A95", VidPid = "0951:1666", Interfaccia = "USB", CapacitaDispositivo = 61958676480L, CapacitaVolume = 61941899264L, Occupato = 4200000000L, FileSystem = "FAT32", Etichetta = "KINGSTON", SerialeVolume = "2C4F1A0B" };
            var p0 = new Protezione { Protetto = true, Verificata = true, Dettaglio = "il file system ha montato il volume in sola lettura; il disco risponde «protetto da scrittura»" };
            MostraSupporto(i0, p0, lista, new List<string>());
            using (var sha = SHA256.Create())
            using (var md5 = MD5.Create())
                foreach (var v in lista)
                {
                    var b = Encoding.UTF8.GetBytes(v.Percorso);
                    v.SHA256 = Formati.Esa(sha.ComputeHash(b));
                    v.MD5 = Formati.Esa(md5.ComputeHash(b));
                }
            for (int i = 0; i < 5; i++) { lista[i].Esito = "Letto"; AggiornaRiga(lista[i]); }
            banner.Imposta(Icona.Clessidra, "Lettura della chiavetta...", "", Blu);
            IniziaAvanzamento(lista.Count, lista.Sum(v => v.Dimensione));
            tAvanzamento.Enabled = false;
            inizioAttivita = DateTime.Now.AddSeconds(-12);
            fattiBytes = lista.Take(5).Sum(v => v.Dimensione);
            fattiFile = 5;
            fileCorrente = lista[5].Percorso;
            AggiornaAvanzamento();
            foto("02-lettura");

            foreach (var v in lista) { v.Esito = "Letto"; AggiornaRiga(v); }
            calcoloCompleto = true;
            barra.Value = 1000;
            ImpostaFase(Fase.Pronto);
            sMsg.Text = "Hash calcolati su " + lista.Count + " file";
            foto("03-pronto");

            lista[11].Selezionato = false; griglia.Rows[11].Cells["Sel"].Value = false;
            lista[8].Selezionato = false; griglia.Rows[8].Cells["Sel"].Value = false;
            AggiornaSelezione();
            var scelti = lista.Where(v => v.Selezionato).ToList();
            ImpostaFase(Fase.Lavoro);
            banner.Imposta(Icona.Clessidra, "Copia in corso...", "", Blu);
            IniziaAvanzamento(scelti.Count, 2 * scelti.Sum(v => v.Dimensione));
            tAvanzamento.Enabled = false;
            inizioAttivita = DateTime.Now.AddSeconds(-40);
            for (int i = 0; i < 6; i++) { scelti[i].Esito = "Copia verificata"; AggiornaRiga(scelti[i]); }
            fattiBytes = 2 * scelti.Take(6).Sum(v => v.Dimensione);
            fattiFile = 6;
            fileCorrente = scelti[6].Percorso;
            AggiornaAvanzamento();
            foto("04-copia");

            foreach (var v in scelti) { v.Esito = "Copia verificata"; v.Copiato = true; AggiornaRiga(v); }
            var cartella = "C:\\Repertum\\2026-10-15_1030_KINGSTON";
            ultimaCartella = cartella;
            ImpostaFase(Fase.Pronto);
            barra.Value = 1000;
            banner.Imposta(Icona.Fatto, "Fatto: " + scelti.Count + " file copiati e verificati", "Salvati in " + cartella + "  -  ora puoi scollegare la chiavetta.", Verde);
            sMsg.Text = "Salvato in " + cartella;
            foto("05-fatto");

            schede.SelectedTab = pVerifica;
            vtCartella.Text = cartella;
            foreach (var v in scelti) vGriglia.Rows.Add(v.Percorso, "INTEGRO", v.SHA256, v.SHA256);
            vGriglia.ClearSelection();
            vGriglia.CurrentCell = null;
            vBarra.Value = 1000;
            vBanner.Imposta(Icona.Fatto, "Tutto integro: " + scelti.Count + " file su " + scelti.Count, "Gli hash coincidono con quelli salvati.", Verde);
            foto("06-verifica");

            vGriglia.Rows[3].Cells["Esito"].Value = "ALTERATO";
            vGriglia.Rows[3].Cells["Calcolato"].Value = "9f2c" + scelti[3].SHA256.Substring(4);
            vBanner.Imposta(Icona.Errore, "Attenzione: 1 file diverso o mancante", "Non usare i file segnati in rosso.", Rosso);
            foto("07-verifica-errore");

            schede.SelectedTab = pChiavetta;
            PulisciSupporto();
            ultimaCartella = null;
            daScollegare = new List<string> { "F:" };
            ImpostaFase(Fase.DaScollegare);
            foto("08-scollega");

            lettera = "E:";
            ImpostaFase(Fase.Lettura);
            var p1 = new Protezione { Protetto = false, Verificata = true, Dettaglio = "il disco risponde «scrivibile»" };
            MostraSupportoSenzaAvviso(i0, p1, lista);
            calcoloCompleto = true;
            ImpostaFase(Fase.Pronto);
            foto("09-non-protetta");
            Close();
        }

        // Solo per l'anteprima: come MostraSupporto, ma senza la finestra di avviso modale.
        void MostraSupportoSenzaAvviso(InfoSupporto i, Protezione p, List<VoceFile> lista)
        {
            info = i; prot = p; voci = lista;
            ImpostaValore("Modello", i.Modello);
            ImpostaValore("Numero di serie", i.Seriale);
            ImpostaValore("Capacità", Formati.Byte(i.CapacitaDispositivo));
            ImpostaValore("File system", i.FileSystem + "  «" + i.Etichetta + "»");
            ImpostaValore("Sola lettura", "NO", Rosso);
            ImpostaValore("Contenuto", lista.Count + " file, " + Formati.Byte(lista.Sum(v => v.Dimensione)));
            foreach (var v in lista)
            {
                v.Selezionato = true; v.Esito = "Letto";
                var r = new DataGridViewRow();
                r.CreateCells(griglia, true, v.Nome, v.Cartella, v.Dimensione, v.Esito, v.SHA256, v.MD5, v.Modificato);
                r.Tag = v;
                righe[v] = r;
                griglia.Rows.Add(r);
            }
            griglia.ClearSelection();
            griglia.CurrentCell = null;
            sMsg.Text = "Hash calcolati su " + lista.Count + " file";
            AggiornaSelezione();
        }
    }
}
