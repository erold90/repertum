using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Repertum
{
    public enum Icona { Usb, Clessidra, Fatto, Avviso, Errore, Scudo, Lucchetto, LucchettoAperto, Salva, Cartella, Ferma }

    // Icone disegnate a mano: contorno scuro, colori pieni, un riflesso. Nessun file esterno.
    public static class Icone
    {
        static readonly Color Contorno = Color.FromArgb(45, 45, 55);

        public static Bitmap Immagine(Icona i, int lato)
        {
            var b = new Bitmap(lato, lato);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Disegna(g, i, new RectangleF(0.5f, 0.5f, lato - 1.5f, lato - 1.5f));
            }
            return b;
        }

        public static void Disegna(Graphics g, Icona i, RectangleF r)
        {
            var vecchio = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = r.Width;
            using (var penna = new Pen(Contorno, Math.Max(1f, w * 0.05f)) { LineJoin = LineJoin.Round })
            {
                switch (i)
                {
                    case Icona.Usb: Usb(g, r, penna); break;
                    case Icona.Clessidra: Clessidra(g, r, penna); break;
                    case Icona.Fatto: Cerchio(g, r, penna, Color.FromArgb(46, 160, 67)); Spunta(g, r); break;
                    case Icona.Errore: Cerchio(g, r, penna, Color.FromArgb(214, 48, 39)); Esclamativo(g, r, Color.White, 0.5f); break;
                    case Icona.Avviso: Triangolo(g, r, penna); break;
                    case Icona.Scudo: Scudo(g, r, penna); break;
                    case Icona.Lucchetto: Lucchetto(g, r, penna, true); break;
                    case Icona.LucchettoAperto: Lucchetto(g, r, penna, false); break;
                    case Icona.Salva: Dischetto(g, r, penna); break;
                    case Icona.Cartella: Cartella(g, r, penna); break;
                    case Icona.Ferma: Cerchio(g, r, penna, Color.FromArgb(214, 48, 39)); Quadratino(g, r); break;
                }
            }
            g.SmoothingMode = vecchio;
        }

        static PointF P(RectangleF r, float x, float y) { return new PointF(r.X + x * r.Width, r.Y + y * r.Height); }
        static RectangleF R(RectangleF r, float x1, float y1, float x2, float y2) { return RectangleF.FromLTRB(r.X + x1 * r.Width, r.Y + y1 * r.Height, r.X + x2 * r.Width, r.Y + y2 * r.Height); }

        static GraphicsPath Arrotondato(RectangleF q, float raggio)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(raggio * 2, Math.Min(q.Width, q.Height)));
            p.AddArc(q.X, q.Y, d, d, 180, 90);
            p.AddArc(q.Right - d, q.Y, d, d, 270, 90);
            p.AddArc(q.Right - d, q.Bottom - d, d, d, 0, 90);
            p.AddArc(q.X, q.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void Riempi(Graphics g, GraphicsPath p, Color c, Pen penna)
        {
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
            g.DrawPath(penna, p);
        }

        static void Riflesso(Graphics g, RectangleF q)
        {
            using (var b = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillEllipse(b, q);
        }

        static void Cerchio(Graphics g, RectangleF r, Pen penna, Color c)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(R(r, 0.06f, 0.06f, 0.94f, 0.94f));
                Riempi(g, p, c, penna);
            }
            Riflesso(g, R(r, 0.2f, 0.13f, 0.62f, 0.4f));
        }

        static void Spunta(Graphics g, RectangleF r)
        {
            using (var p = new Pen(Color.White, r.Width * 0.12f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { P(r, 0.29f, 0.52f), P(r, 0.44f, 0.67f), P(r, 0.72f, 0.36f) });
        }

        static void Esclamativo(Graphics g, RectangleF r, Color c, float cx)
        {
            using (var p = new Pen(c, r.Width * 0.12f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(p, P(r, cx, 0.26f), P(r, cx, 0.55f));
            using (var b = new SolidBrush(c)) g.FillEllipse(b, R(r, cx - 0.075f, 0.65f, cx + 0.075f, 0.8f));
        }

        static void Quadratino(Graphics g, RectangleF r)
        {
            using (var p = Arrotondato(R(r, 0.33f, 0.33f, 0.67f, 0.67f), r.Width * 0.04f))
            using (var b = new SolidBrush(Color.White)) g.FillPath(b, p);
        }

        static void Triangolo(Graphics g, RectangleF r, Pen penna)
        {
            using (var p = new GraphicsPath())
            {
                p.AddLines(new[] { P(r, 0.5f, 0.07f), P(r, 0.95f, 0.88f), P(r, 0.05f, 0.88f) });
                p.CloseFigure();
                using (var spessa = new Pen(penna.Color, penna.Width * 2.2f) { LineJoin = LineJoin.Round }) g.DrawPath(spessa, p);
                using (var b = new SolidBrush(Color.FromArgb(247, 181, 41))) g.FillPath(b, p);
            }
            var q = R(r, 0f, 0.14f, 1f, 1.0f);
            Esclamativo(g, q, Contorno, 0.5f);
        }

        static void Usb(Graphics g, RectangleF r, Pen penna)
        {
            // connettore metallico
            using (var p = new GraphicsPath())
            {
                p.AddRectangle(R(r, 0.34f, 0.05f, 0.66f, 0.34f));
                Riempi(g, p, Color.FromArgb(205, 208, 214), penna);
            }
            using (var b = new SolidBrush(Contorno))
            {
                g.FillRectangle(b, R(r, 0.40f, 0.13f, 0.47f, 0.2f));
                g.FillRectangle(b, R(r, 0.53f, 0.13f, 0.60f, 0.2f));
            }
            // corpo
            using (var p = Arrotondato(R(r, 0.24f, 0.30f, 0.76f, 0.95f), r.Width * 0.1f))
                Riempi(g, p, Color.FromArgb(47, 111, 208), penna);
            Riflesso(g, R(r, 0.3f, 0.35f, 0.5f, 0.75f));
            // etichetta
            using (var p = Arrotondato(R(r, 0.36f, 0.55f, 0.64f, 0.84f), r.Width * 0.04f))
            using (var b = new SolidBrush(Color.FromArgb(235, 242, 255))) g.FillPath(b, p);
        }

        static void Clessidra(Graphics g, RectangleF r, Pen penna)
        {
            using (var vetro = new GraphicsPath())
            {
                vetro.AddBezier(P(r, 0.27f, 0.15f), P(r, 0.27f, 0.42f), P(r, 0.47f, 0.44f), P(r, 0.47f, 0.5f));
                vetro.AddBezier(P(r, 0.47f, 0.5f), P(r, 0.47f, 0.56f), P(r, 0.27f, 0.58f), P(r, 0.27f, 0.85f));
                vetro.AddLine(P(r, 0.27f, 0.85f), P(r, 0.73f, 0.85f));
                vetro.AddBezier(P(r, 0.73f, 0.85f), P(r, 0.73f, 0.58f), P(r, 0.53f, 0.56f), P(r, 0.53f, 0.5f));
                vetro.AddBezier(P(r, 0.53f, 0.5f), P(r, 0.53f, 0.44f), P(r, 0.73f, 0.42f), P(r, 0.73f, 0.15f));
                vetro.CloseFigure();
                Riempi(g, vetro, Color.FromArgb(225, 240, 252), penna);
            }
            using (var b = new SolidBrush(Color.FromArgb(240, 190, 60)))
            {
                g.FillPolygon(b, new[] { P(r, 0.33f, 0.84f), P(r, 0.67f, 0.84f), P(r, 0.5f, 0.64f) });
                g.FillPolygon(b, new[] { P(r, 0.37f, 0.3f), P(r, 0.63f, 0.3f), P(r, 0.5f, 0.45f) });
            }
            foreach (var y in new[] { 0.06f, 0.85f })
                using (var p = Arrotondato(R(r, 0.17f, y, 0.83f, y + 0.1f), r.Width * 0.03f))
                    Riempi(g, p, Color.FromArgb(150, 95, 45), penna);
        }

        static void Scudo(Graphics g, RectangleF r, Pen penna)
        {
            using (var p = new GraphicsPath())
            {
                p.AddLine(P(r, 0.5f, 0.05f), P(r, 0.88f, 0.18f));
                p.AddBezier(P(r, 0.88f, 0.18f), P(r, 0.88f, 0.6f), P(r, 0.72f, 0.82f), P(r, 0.5f, 0.95f));
                p.AddBezier(P(r, 0.5f, 0.95f), P(r, 0.28f, 0.82f), P(r, 0.12f, 0.6f), P(r, 0.12f, 0.18f));
                p.CloseFigure();
                Riempi(g, p, Color.FromArgb(47, 111, 208), penna);
            }
            Riflesso(g, R(r, 0.22f, 0.16f, 0.5f, 0.5f));
            Spunta(g, R(r, 0.02f, 0.02f, 0.98f, 0.98f));
        }

        static void Lucchetto(Graphics g, RectangleF r, Pen penna, bool chiuso)
        {
            float dx = chiuso ? 0f : 0.2f, dy = chiuso ? 0f : -0.1f;
            using (var arco = new Pen(Color.FromArgb(150, 155, 165), r.Width * 0.12f))
            using (var bordo = new Pen(penna.Color, r.Width * 0.2f))
            using (var p = new GraphicsPath())
            {
                p.AddLine(P(r, 0.3f + dx, 0.5f + dy), P(r, 0.3f + dx, 0.32f + dy));
                p.AddArc(R(r, 0.3f + dx, 0.06f + dy, 0.7f + dx, 0.5f + dy), 180, 180);
                p.AddLine(P(r, 0.7f + dx, 0.32f + dy), P(r, 0.7f + dx, 0.5f + dy));
                g.DrawPath(bordo, p);
                g.DrawPath(arco, p);
            }
            using (var p = Arrotondato(R(r, 0.16f, 0.44f, 0.84f, 0.95f), r.Width * 0.08f))
                Riempi(g, p, chiuso ? Color.FromArgb(232, 181, 48) : Color.FromArgb(200, 200, 200), penna);
            using (var b = new SolidBrush(Contorno))
            {
                g.FillEllipse(b, R(r, 0.43f, 0.58f, 0.57f, 0.72f));
                g.FillRectangle(b, R(r, 0.47f, 0.66f, 0.53f, 0.82f));
            }
        }

        static void Dischetto(Graphics g, RectangleF r, Pen penna)
        {
            using (var p = new GraphicsPath())
            {
                p.AddLines(new[] { P(r, 0.1f, 0.08f), P(r, 0.78f, 0.08f), P(r, 0.92f, 0.22f), P(r, 0.92f, 0.92f), P(r, 0.1f, 0.92f) });
                p.CloseFigure();
                Riempi(g, p, Color.FromArgb(40, 70, 150), penna);
            }
            using (var p = new GraphicsPath())
            {
                p.AddRectangle(R(r, 0.27f, 0.08f, 0.71f, 0.36f));
                Riempi(g, p, Color.FromArgb(205, 208, 214), penna);
            }
            using (var b = new SolidBrush(Color.FromArgb(40, 70, 150))) g.FillRectangle(b, R(r, 0.56f, 0.13f, 0.65f, 0.31f));
            using (var p = new GraphicsPath())
            {
                p.AddRectangle(R(r, 0.22f, 0.52f, 0.8f, 0.92f));
                Riempi(g, p, Color.White, penna);
            }
            using (var l = new Pen(Color.FromArgb(170, 180, 200), Math.Max(1f, r.Width * 0.035f)))
            {
                g.DrawLine(l, P(r, 0.3f, 0.65f), P(r, 0.72f, 0.65f));
                g.DrawLine(l, P(r, 0.3f, 0.77f), P(r, 0.72f, 0.77f));
            }
        }

        static void Cartella(Graphics g, RectangleF r, Pen penna)
        {
            using (var p = new GraphicsPath())
            {
                p.AddLines(new[] { P(r, 0.06f, 0.2f), P(r, 0.38f, 0.2f), P(r, 0.46f, 0.3f), P(r, 0.94f, 0.3f), P(r, 0.94f, 0.86f), P(r, 0.06f, 0.86f) });
                p.CloseFigure();
                Riempi(g, p, Color.FromArgb(222, 160, 40), penna);
            }
            using (var p = new GraphicsPath())
            {
                p.AddLines(new[] { P(r, 0.06f, 0.42f), P(r, 0.94f, 0.42f), P(r, 0.94f, 0.86f), P(r, 0.06f, 0.86f) });
                p.CloseFigure();
                Riempi(g, p, Color.FromArgb(250, 206, 90), penna);
            }
        }
    }

    // Intestazione bianca in alto, come le procedure guidate di Windows: icona, titolo, una riga di spiegazione.
    public class Banner : Control
    {
        Icona icona = Icona.Usb;
        string titolo = "", sotto = "", destra = "";
        Color colore = Color.Black, coloreDestra = Color.Gray;
        bool lucchetto;

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            TabStop = false;
        }

        public void Imposta(Icona i, string t, string s, Color c)
        {
            icona = i; titolo = t ?? ""; sotto = s ?? ""; colore = c;
            Text = titolo;
            AccessibleName = titolo;
            Invalidate();
        }

        public string Titolo { get { return titolo; } }
        public string Sotto { get { return sotto; } set { sotto = value ?? ""; Invalidate(); } }

        public void Protezione(string t, Color c, bool chiusa)
        {
            destra = t ?? ""; coloreDestra = c; lucchetto = chiusa;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float k = DeviceDpi / 96f;
            var r = ClientRectangle;
            g.Clear(BackColor);
            ControlPaint.DrawBorder3D(g, r, Border3DStyle.SunkenOuter);

            float lato = Math.Min(r.Height - 18 * k, 50 * k);
            var ri = new RectangleF(16 * k, (r.Height - lato) / 2f, lato, lato);
            Icone.Disegna(g, icona, ri);

            float larghezzaDestra = 0;
            if (destra.Length > 0)
            {
                using (var f = new Font("Segoe UI", 9F, FontStyle.Bold))
                using (var b = new SolidBrush(coloreDestra))
                {
                    var sz = g.MeasureString(destra, f);
                    float li = 20 * k;
                    larghezzaDestra = li + 6 * k + sz.Width + 18 * k;
                    float x = r.Width - larghezzaDestra;
                    float y = (r.Height - li) / 2f;
                    Icone.Disegna(g, lucchetto ? Icona.Lucchetto : Icona.LucchettoAperto, new RectangleF(x, y, li, li));
                    g.DrawString(destra, f, b, x + li + 6 * k, (r.Height - sz.Height) / 2f);
                }
            }

            float xt = ri.Right + 16 * k;
            float wt = Math.Max(10, r.Width - xt - larghezzaDestra - 10 * k);
            using (var ft = new Font("Segoe UI", 13F, FontStyle.Bold))
            using (var fs = new Font("Segoe UI", 9.5F))
            using (var bt = new SolidBrush(colore))
            using (var bs = new SolidBrush(Color.FromArgb(70, 70, 70)))
            using (var st = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                float ht = g.MeasureString("Ag", ft).Height, hs = g.MeasureString("Ag", fs).Height;
                float y = (r.Height - ht - (sotto.Length > 0 ? hs : 0)) / 2f;
                g.DrawString(titolo, ft, bt, new RectangleF(xt, y, wt, ht), st);
                if (sotto.Length > 0) g.DrawString(sotto, fs, bs, new RectangleF(xt, y + ht, wt, hs), st);
            }
        }
    }
}
