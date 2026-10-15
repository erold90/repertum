// Repertum - le icone disegnate a mano, stesse forme e colori della versione Windows (Icone.cs con GDI+),
// ridisegnate con le primitive di Avalonia. Nessun file esterno, nessuna emoji.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Repertum
{
    public enum Icona { Usb, Clessidra, Fatto, Avviso, Errore, Scudo, Lucchetto, LucchettoAperto, Salva, Cartella, Ferma, Info, App }

    public static class Icone
    {
        static bool spenta;     // pulsante disabilitato: stessi disegni, in grigio chiaro

        static Color C(int r, int g, int b) { return C(255, r, g, b); }
        static Color C(int a, int r, int g, int b)
        {
            if (spenta)
            {
                int y = (int)(0.3 * r + 0.59 * g + 0.11 * b);
                y = y + (255 - y) * 45 / 100;
                return Color.FromArgb((byte)a, (byte)y, (byte)y, (byte)y);
            }
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }
        static IBrush B(Color c) { return new SolidColorBrush(c); }
        static Color Contorno { get { return C(45, 45, 55); } }

        public static void Disegna(DrawingContext g, Icona i, Rect r, bool disabilitata = false)
        {
            spenta = disabilitata;
            try
            {
                double w = r.Width;
                var penna = new Pen(B(Contorno), Math.Max(1.0, w * 0.05), lineJoin: PenLineJoin.Round);
                switch (i)
                {
                    case Icona.Usb: Usb(g, r, penna); break;
                    case Icona.Clessidra: Clessidra(g, r, penna); break;
                    case Icona.Fatto: Cerchio(g, r, penna, C(46, 160, 67)); Spunta(g, r); break;
                    case Icona.Errore: Cerchio(g, r, penna, C(214, 48, 39)); Esclamativo(g, r, C(255, 255, 255), 0.5); break;
                    case Icona.Info: Cerchio(g, r, penna, C(47, 111, 208)); Lettera(g, r); break;
                    case Icona.Avviso: Triangolo(g, r, penna); break;
                    case Icona.Scudo: Scudo(g, r, penna); break;
                    case Icona.Lucchetto: Lucchetto(g, r, penna, true); break;
                    case Icona.LucchettoAperto: Lucchetto(g, r, penna, false); break;
                    case Icona.Salva: Dischetto(g, r, penna); break;
                    case Icona.Cartella: Cartella(g, r, penna); break;
                    case Icona.Ferma: Cerchio(g, r, penna, C(214, 48, 39)); Quadratino(g, r); break;
                    case Icona.App: Applicazione(g, r); break;
                }
            }
            finally { spenta = false; }
        }

        static Point P(Rect r, double x, double y) { return new Point(r.X + x * r.Width, r.Y + y * r.Height); }
        static Rect R(Rect r, double x1, double y1, double x2, double y2)
        {
            return new Rect(new Point(r.X + x1 * r.Width, r.Y + y1 * r.Height), new Point(r.X + x2 * r.Width, r.Y + y2 * r.Height));
        }

        // Come GraphicsPath.AddArc/AddLine/AddBezier: i pezzi di una figura si uniscono da soli.
        sealed class Tracciato
        {
            readonly StreamGeometry geo = new StreamGeometry();
            readonly StreamGeometryContext c;
            readonly bool piena;
            bool aperta;
            Point ultimo;

            public Tracciato(bool piena = true) { c = geo.Open(); this.piena = piena; }

            void Verso(Point p)
            {
                if (!aperta) { c.BeginFigure(p, piena); aperta = true; }
                else if (p != ultimo) c.LineTo(p);
                ultimo = p;
            }

            public Tracciato Linea(Point a, Point b) { Verso(a); c.LineTo(b); ultimo = b; return this; }

            public Tracciato Linee(params Point[] pp)
            {
                Verso(pp[0]);
                for (int i = 1; i < pp.Length; i++) c.LineTo(pp[i]);
                ultimo = pp[pp.Length - 1];
                return this;
            }

            public Tracciato Bezier(Point a, Point b, Point d, Point e) { Verso(a); c.CubicBezierTo(b, d, e); ultimo = e; return this; }

            // Angoli in gradi, in senso orario dall'asse x, come in GDI+.
            public Tracciato Arco(Rect q, double inizio, double ampiezza)
            {
                double rx = q.Width / 2, ry = q.Height / 2;
                var cx = q.Center;
                Func<double, Point> pt = gradi => new Point(cx.X + rx * Math.Cos(gradi * Math.PI / 180), cx.Y + ry * Math.Sin(gradi * Math.PI / 180));
                Verso(pt(inizio));
                int pezzi = Math.Abs(ampiezza) > 180 ? 2 : 1;
                for (int k = 1; k <= pezzi; k++)
                {
                    var fine = pt(inizio + ampiezza * k / pezzi);
                    c.ArcTo(fine, new Size(rx, ry), 0, false, ampiezza > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                    ultimo = fine;
                }
                return this;
            }

            public Tracciato Chiudi() { if (aperta) { c.EndFigure(true); aperta = false; } return this; }

            public Tracciato Rettangolo(Rect q)
            {
                if (aperta) { c.EndFigure(false); aperta = false; }
                return Linee(q.TopLeft, q.TopRight, q.BottomRight, q.BottomLeft).Chiudi();
            }

            public Tracciato Ellisse(Rect q)
            {
                if (aperta) { c.EndFigure(false); aperta = false; }
                return Arco(q, 0, 360).Chiudi();
            }

            public Geometry Fine()
            {
                if (aperta) { c.EndFigure(false); aperta = false; }
                c.Dispose();
                return geo;
            }
        }

        static Geometry Arrotondato(Rect q, double raggio)
        {
            double d = Math.Max(0.1, Math.Min(raggio * 2, Math.Min(q.Width, q.Height)));
            return new Tracciato()
                .Arco(new Rect(q.X, q.Y, d, d), 180, 90)
                .Arco(new Rect(q.Right - d, q.Y, d, d), 270, 90)
                .Arco(new Rect(q.Right - d, q.Bottom - d, d, d), 0, 90)
                .Arco(new Rect(q.X, q.Bottom - d, d, d), 90, 90)
                .Chiudi().Fine();
        }

        static void Riempi(DrawingContext g, Geometry p, Color c, IPen penna) { g.DrawGeometry(B(c), penna, p); }

        static void Riflesso(DrawingContext g, Rect q) { g.DrawEllipse(B(C(80, 255, 255, 255)), null, q); }

        static void Cerchio(DrawingContext g, Rect r, IPen penna, Color c)
        {
            g.DrawEllipse(B(c), penna, R(r, 0.06, 0.06, 0.94, 0.94));
            Riflesso(g, R(r, 0.2, 0.13, 0.62, 0.4));
        }

        static void Spunta(DrawingContext g, Rect r)
        {
            var p = new Pen(B(C(255, 255, 255)), r.Width * 0.12, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            g.DrawGeometry(null, p, new Tracciato(false).Linee(P(r, 0.29, 0.52), P(r, 0.44, 0.67), P(r, 0.72, 0.36)).Fine());
        }

        static void Esclamativo(DrawingContext g, Rect r, Color c, double cx)
        {
            var p = new Pen(B(c), r.Width * 0.12, lineCap: PenLineCap.Round);
            g.DrawLine(p, P(r, cx, 0.26), P(r, cx, 0.55));
            g.DrawEllipse(B(c), null, R(r, cx - 0.075, 0.65, cx + 0.075, 0.8));
        }

        // La «i» delle informazioni: l'esclamativo capovolto.
        static void Lettera(DrawingContext g, Rect r)
        {
            var bianco = C(255, 255, 255);
            g.DrawEllipse(B(bianco), null, R(r, 0.425, 0.2, 0.575, 0.35));
            var p = new Pen(B(bianco), r.Width * 0.12, lineCap: PenLineCap.Round);
            g.DrawLine(p, P(r, 0.5, 0.47), P(r, 0.5, 0.74));
        }

        static void Quadratino(DrawingContext g, Rect r)
        {
            g.DrawGeometry(B(C(255, 255, 255)), null, Arrotondato(R(r, 0.33, 0.33, 0.67, 0.67), r.Width * 0.04));
        }

        static void Triangolo(DrawingContext g, Rect r, Pen penna)
        {
            var p = new Tracciato().Linee(P(r, 0.5, 0.07), P(r, 0.95, 0.88), P(r, 0.05, 0.88)).Chiudi().Fine();
            g.DrawGeometry(null, new Pen(penna.Brush, penna.Thickness * 2.2, lineJoin: PenLineJoin.Round), p);
            g.DrawGeometry(B(C(247, 181, 41)), null, p);
            Esclamativo(g, R(r, 0, 0.14, 1, 1.0), Contorno, 0.5);
        }

        static void Usb(DrawingContext g, Rect r, IPen penna)
        {
            // connettore metallico
            Riempi(g, new Tracciato().Rettangolo(R(r, 0.34, 0.05, 0.66, 0.34)).Fine(), C(205, 208, 214), penna);
            var scuro = B(Contorno);
            g.DrawRectangle(scuro, null, R(r, 0.40, 0.13, 0.47, 0.2));
            g.DrawRectangle(scuro, null, R(r, 0.53, 0.13, 0.60, 0.2));
            // corpo
            Riempi(g, Arrotondato(R(r, 0.24, 0.30, 0.76, 0.95), r.Width * 0.1), C(47, 111, 208), penna);
            Riflesso(g, R(r, 0.3, 0.35, 0.5, 0.75));
            // etichetta
            g.DrawGeometry(B(C(235, 242, 255)), null, Arrotondato(R(r, 0.36, 0.55, 0.64, 0.84), r.Width * 0.04));
        }

        static void Clessidra(DrawingContext g, Rect r, IPen penna)
        {
            var vetro = new Tracciato()
                .Bezier(P(r, 0.27, 0.15), P(r, 0.27, 0.42), P(r, 0.47, 0.44), P(r, 0.47, 0.5))
                .Bezier(P(r, 0.47, 0.5), P(r, 0.47, 0.56), P(r, 0.27, 0.58), P(r, 0.27, 0.85))
                .Linea(P(r, 0.27, 0.85), P(r, 0.73, 0.85))
                .Bezier(P(r, 0.73, 0.85), P(r, 0.73, 0.58), P(r, 0.53, 0.56), P(r, 0.53, 0.5))
                .Bezier(P(r, 0.53, 0.5), P(r, 0.53, 0.44), P(r, 0.73, 0.42), P(r, 0.73, 0.15))
                .Chiudi().Fine();
            Riempi(g, vetro, C(225, 240, 252), penna);
            var sabbia = B(C(240, 190, 60));
            g.DrawGeometry(sabbia, null, new Tracciato().Linee(P(r, 0.33, 0.84), P(r, 0.67, 0.84), P(r, 0.5, 0.64)).Chiudi().Fine());
            g.DrawGeometry(sabbia, null, new Tracciato().Linee(P(r, 0.37, 0.3), P(r, 0.63, 0.3), P(r, 0.5, 0.45)).Chiudi().Fine());
            foreach (var y in new[] { 0.06, 0.85 })
                Riempi(g, Arrotondato(R(r, 0.17, y, 0.83, y + 0.1), r.Width * 0.03), C(150, 95, 45), penna);
        }

        static Geometry FormaScudo(Rect r)
        {
            return new Tracciato()
                .Linea(P(r, 0.5, 0.05), P(r, 0.88, 0.18))
                .Bezier(P(r, 0.88, 0.18), P(r, 0.88, 0.6), P(r, 0.72, 0.82), P(r, 0.5, 0.95))
                .Bezier(P(r, 0.5, 0.95), P(r, 0.28, 0.82), P(r, 0.12, 0.6), P(r, 0.12, 0.18))
                .Chiudi().Fine();
        }

        static void Scudo(DrawingContext g, Rect r, IPen penna)
        {
            Riempi(g, FormaScudo(r), C(47, 111, 208), penna);
            Riflesso(g, R(r, 0.22, 0.16, 0.5, 0.5));
            Spunta(g, R(r, 0.02, 0.02, 0.98, 0.98));
        }

        static void Lucchetto(DrawingContext g, Rect r, Pen penna, bool chiuso)
        {
            double dx = chiuso ? 0 : 0.2, dy = chiuso ? 0 : -0.1;
            var arco = new Tracciato(false)
                .Linea(P(r, 0.3 + dx, 0.5 + dy), P(r, 0.3 + dx, 0.32 + dy))
                .Arco(R(r, 0.3 + dx, 0.06 + dy, 0.7 + dx, 0.5 + dy), 180, 180)
                .Linea(P(r, 0.7 + dx, 0.32 + dy), P(r, 0.7 + dx, 0.5 + dy))
                .Fine();
            g.DrawGeometry(null, new Pen(penna.Brush, r.Width * 0.2), arco);
            g.DrawGeometry(null, new Pen(B(C(150, 155, 165)), r.Width * 0.12), arco);
            Riempi(g, Arrotondato(R(r, 0.16, 0.44, 0.84, 0.95), r.Width * 0.08), chiuso ? C(232, 181, 48) : C(200, 200, 200), penna);
            var scuro = B(Contorno);
            g.DrawEllipse(scuro, null, R(r, 0.43, 0.58, 0.57, 0.72));
            g.DrawRectangle(scuro, null, R(r, 0.47, 0.66, 0.53, 0.82));
        }

        static void Dischetto(DrawingContext g, Rect r, IPen penna)
        {
            Riempi(g, new Tracciato().Linee(P(r, 0.1, 0.08), P(r, 0.78, 0.08), P(r, 0.92, 0.22), P(r, 0.92, 0.92), P(r, 0.1, 0.92)).Chiudi().Fine(), C(40, 70, 150), penna);
            Riempi(g, new Tracciato().Rettangolo(R(r, 0.27, 0.08, 0.71, 0.36)).Fine(), C(205, 208, 214), penna);
            g.DrawRectangle(B(C(40, 70, 150)), null, R(r, 0.56, 0.13, 0.65, 0.31));
            Riempi(g, new Tracciato().Rettangolo(R(r, 0.22, 0.52, 0.8, 0.92)).Fine(), C(255, 255, 255), penna);
            var l = new Pen(B(C(170, 180, 200)), Math.Max(1.0, r.Width * 0.035));
            g.DrawLine(l, P(r, 0.3, 0.65), P(r, 0.72, 0.65));
            g.DrawLine(l, P(r, 0.3, 0.77), P(r, 0.72, 0.77));
        }

        static void Cartella(DrawingContext g, Rect r, IPen penna)
        {
            Riempi(g, new Tracciato().Linee(P(r, 0.06, 0.2), P(r, 0.38, 0.2), P(r, 0.46, 0.3), P(r, 0.94, 0.3), P(r, 0.94, 0.86), P(r, 0.06, 0.86)).Chiudi().Fine(), C(222, 160, 40), penna);
            Riempi(g, new Tracciato().Linee(P(r, 0.06, 0.42), P(r, 0.94, 0.42), P(r, 0.94, 0.86), P(r, 0.06, 0.86)).Chiudi().Fine(), C(250, 206, 90), penna);
        }

        // L'icona del programma (repertum.ico): la chiavetta inclinata con lo scudo verde.
        static void Applicazione(DrawingContext g, Rect r)
        {
            double w = r.Width;
            var blu = new Pen(B(C(24, 44, 100)), w * 0.034, lineJoin: PenLineJoin.Round);
            var centro = P(r, 0.43, 0.47);
            var m = Matrix.CreateTranslation(-centro.X, -centro.Y) * Matrix.CreateRotation(32 * Math.PI / 180) * Matrix.CreateTranslation(centro.X, centro.Y);
            using (g.PushTransform(m))
            {
                // connettore con i due fori
                Riempi(g, new Tracciato().Rettangolo(R(r, 0.33, 0.05, 0.53, 0.30)).Fine(), C(200, 205, 213), blu);
                var foro = B(C(55, 60, 75));
                g.DrawRectangle(foro, null, R(r, 0.37, 0.12, 0.415, 0.175));
                g.DrawRectangle(foro, null, R(r, 0.445, 0.12, 0.49, 0.175));
                // corpo blu con il riflesso lungo il fianco
                Riempi(g, Arrotondato(R(r, 0.28, 0.28, 0.58, 0.93), w * 0.07), C(44, 98, 200), blu);
                g.DrawGeometry(B(C(170, 110, 165, 240)), null, Arrotondato(R(r, 0.32, 0.34, 0.39, 0.82), w * 0.035));
                // spia verde
                g.DrawEllipse(B(C(70, 205, 100)), new Pen(B(C(24, 44, 100)), w * 0.012), R(r, 0.40, 0.835, 0.46, 0.895));
            }
            // scudo verde con la spunta bianca
            var s = R(r, 0.38, 0.42, 0.98, 1.0);
            Riempi(g, FormaScudo(s), C(40, 145, 62), new Pen(B(C(20, 80, 36)), w * 0.034, lineJoin: PenLineJoin.Round));
            g.DrawGeometry(null, new Pen(B(C(110, 70, 185, 95)), w * 0.02, lineJoin: PenLineJoin.Round), FormaScudo(R(s, 0.07, 0.07, 0.93, 0.95)));
            var spunta = new Pen(B(C(255, 255, 255)), w * 0.075, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            g.DrawGeometry(null, spunta, new Tracciato(false).Linee(P(s, 0.28, 0.52), P(s, 0.45, 0.68), P(s, 0.74, 0.36)).Fine());
        }
    }

    // Un'icona come controllo: si disegna da sola alla dimensione che riceve.
    public class IconaVista : Control
    {
        public static readonly StyledProperty<Icona> IconaProperty = AvaloniaProperty.Register<IconaVista, Icona>(nameof(Icona));

        static IconaVista() { AffectsRender<IconaVista>(IconaProperty, IsEffectivelyEnabledProperty); }

        public Icona Icona { get { return GetValue(IconaProperty); } set { SetValue(IconaProperty, value); } }

        public override void Render(DrawingContext g)
        {
            double lato = Math.Min(Bounds.Width, Bounds.Height);
            if (lato <= 2) return;
            var r = new Rect((Bounds.Width - lato) / 2, (Bounds.Height - lato) / 2, lato, lato).Deflate(0.5);
            Icone.Disegna(g, Icona, r, !IsEffectivelyEnabled);
        }
    }
}
