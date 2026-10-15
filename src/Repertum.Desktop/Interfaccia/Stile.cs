// Repertum per macOS e Linux - colori e misure dei controlli, presi dalla versione Windows
// (pulsanti chiari col bordo grigio, tabelle bianche con le righe, barra blu piena).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Repertum
{
    public static class Stile
    {
        public static readonly FontFamily Mono = new FontFamily("Menlo, Consolas, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, Ubuntu Mono, monospace");

        static IBrush B(string c) { return new SolidColorBrush(Color.Parse(c)); }

        public static void Applica(Application app)
        {
            var r = app.Resources;
            r["ControlContentThemeFontSize"] = 12.0;
            r["ControlCornerRadius"] = new CornerRadius(4);
            r["ButtonBackground"] = B("#FDFDFD");
            r["ButtonBackgroundPointerOver"] = B("#E5F1FB");
            r["ButtonBackgroundPressed"] = B("#CCE4F7");
            r["ButtonBackgroundDisabled"] = B("#F9F9F9");
            r["ButtonForeground"] = Brushes.Black;
            r["ButtonForegroundPointerOver"] = Brushes.Black;
            r["ButtonForegroundPressed"] = Brushes.Black;
            r["ButtonForegroundDisabled"] = B("#8C8C8C");
            r["ButtonBorderBrush"] = B("#D0D0D0");
            r["ButtonBorderBrushPointerOver"] = B("#0078D4");
            r["ButtonBorderBrushPressed"] = B("#005499");
            r["ButtonBorderBrushDisabled"] = B("#E3E3E3");
            r["ButtonBorderThemeThickness"] = new Thickness(1);
            r["TextControlBackground"] = Brushes.White;
            r["TextControlBackgroundPointerOver"] = Brushes.White;
            r["TextControlBackgroundFocused"] = Brushes.White;
            r["TextControlBorderBrush"] = B("#8A8A8A");
            r["TextControlBorderBrushPointerOver"] = B("#5A5A5A");
            r["TextControlBorderBrushFocused"] = B("#0078D4");
            r["TextControlForeground"] = Brushes.Black;
            r["TextControlForegroundFocused"] = Brushes.Black;
            r["TextControlForegroundPointerOver"] = Brushes.Black;

            var s = app.Styles;
            Aggiungi(s, x => x.OfType<Window>(), (TemplatedControl.FontSizeProperty, 12.0), (TemplatedControl.ForegroundProperty, Brushes.Black));
            Aggiungi(s, x => x.OfType<CheckBox>(), (Layoutable.MinWidthProperty, 0.0), (Layoutable.MinHeightProperty, 0.0),
                     (TemplatedControl.PaddingProperty, new Thickness(6, 0, 0, 0)), (TemplatedControl.FontSizeProperty, 12.0));
            Aggiungi(s, x => x.OfType<TextBox>(), (Layoutable.MinHeightProperty, 0.0), (TemplatedControl.PaddingProperty, new Thickness(5, 3, 5, 3)),
                     (TemplatedControl.FontSizeProperty, 12.0), (TemplatedControl.CornerRadiusProperty, new CornerRadius(3)));
            Aggiungi(s, x => x.OfType<ProgressBar>(), (Layoutable.MinHeightProperty, 0.0), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                     (TemplatedControl.BackgroundProperty, Colori.Finestra), (TemplatedControl.ForegroundProperty, Colori.Barra));
            Aggiungi(s, x => x.OfType<ToolTip>(), (TemplatedControl.FontSizeProperty, 12.0));

            // tabelle come DataGridView: intestazioni bianche e basse, righe da 22 punti, selezione azzurra
            Aggiungi(s, x => x.OfType<DataGrid>(), (TemplatedControl.BackgroundProperty, Brushes.White), (TemplatedControl.FontSizeProperty, 12.0),
                     (TemplatedControl.BorderBrushProperty, B("#ABADB3")), (TemplatedControl.BorderThicknessProperty, new Thickness(1)));
            Aggiungi(s, x => x.OfType<DataGridColumnHeader>(), (TemplatedControl.BackgroundProperty, Brushes.White),
                     (TemplatedControl.ForegroundProperty, Brushes.Black), (TemplatedControl.FontSizeProperty, 12.0),
                     (TemplatedControl.FontWeightProperty, FontWeight.Normal),
                     (Layoutable.MinHeightProperty, 23.0), (TemplatedControl.PaddingProperty, new Thickness(6, 0, 0, 0)),
                     (DataGridColumnHeader.SeparatorBrushProperty, B("#E5E5E5")));
            Aggiungi(s, x => x.OfType<DataGridColumnHeader>().Class(":pointerover").Template().OfType<Grid>().Name("PART_ColumnHeaderRoot"),
                     (Panel.BackgroundProperty, B("#D9EBF9")));
            Aggiungi(s, x => x.OfType<DataGridCell>(), (Layoutable.MinHeightProperty, 0.0), (TemplatedControl.FontSizeProperty, 12.0),
                     (TemplatedControl.PaddingProperty, new Thickness(0)));
            Aggiungi(s, x => x.OfType<DataGridRow>().Class(":selected").Template().OfType<Rectangle>().Name("BackgroundRectangle"),
                     (Shape.FillProperty, B("#CCE8FF")), (Visual.OpacityProperty, 1.0));
            Aggiungi(s, x => x.OfType<DataGridRow>().Class(":pointerover").Template().OfType<Rectangle>().Name("BackgroundRectangle"),
                     (Shape.FillProperty, B("#E5F3FF")), (Visual.OpacityProperty, 1.0));
            Aggiungi(s, x => x.OfType<DataGridRow>().Class(":selected").Class(":pointerover").Template().OfType<Rectangle>().Name("BackgroundRectangle"),
                     (Shape.FillProperty, B("#CCE8FF")), (Visual.OpacityProperty, 1.0));
            Aggiungi(s, x => x.OfType<DataGridCell>().Class(":focus").Template().OfType<Grid>().Name("FocusVisual"),
                     (Visual.IsVisibleProperty, false));

            // il percorso di salvataggio, cliccabile come un collegamento
            Aggiungi(s, x => x.OfType<Button>().Class("link"), (TemplatedControl.BackgroundProperty, Brushes.Transparent),
                     (TemplatedControl.BorderThicknessProperty, new Thickness(0)), (TemplatedControl.PaddingProperty, new Thickness(0)),
                     (TemplatedControl.ForegroundProperty, Colori.Collegamento), (InputElement.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)));
            foreach (var stato in new[] { ":pointerover", ":pressed", ":disabled" })
                Aggiungi(s, x => x.OfType<Button>().Class("link").Class(stato).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter"),
                         (Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.Transparent),
                         (Avalonia.Controls.Presenters.ContentPresenter.BorderThicknessProperty, new Thickness(0)),
                         (Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, stato == ":disabled" ? Colori.Grigio : Colori.Collegamento));
        }

        static void Aggiungi(Styles s, System.Func<Selector, Selector> selettore, params (AvaloniaProperty, object)[] valori)
        {
            var st = new Style(selettore);
            foreach (var v in valori) st.Setters.Add(new Setter(v.Item1, v.Item2));
            s.Add(st);
        }
    }
}
