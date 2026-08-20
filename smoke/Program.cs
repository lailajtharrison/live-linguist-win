// Smoke test: prove GitHub Actions (windows-latest) can BUILD a Windows GUI app
// and produce a real screenshot of the UI, headlessly.
//
// It builds the Live Linguist dual-transcript layout (verbatim + simplified) as a
// WPF visual tree, renders it OFFSCREEN to a PNG (software rasterizer, no display
// or GPU needed), writes the file, and exits. If CI returns this PNG as an
// artifact, the visual-critic pipeline has a substrate to work on.

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "screenshot.png";
        const int W = 960, H = 540;

        var root = BuildDualTranscript(W, H);

        // Lay out and render the visual tree offscreen — no window, no display.
        root.Measure(new Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(outPath);
        encoder.Save(fs);

        Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length} bytes)");
    }

    private static Grid BuildDualTranscript(int w, int h)
    {
        var root = new Grid
        {
            Width = w,
            Height = h,
            Background = new SolidColorBrush(Color.FromRgb(0x0f, 0x11, 0x14)),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = "Live Linguist  ·  Windows  ·  smoke test",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5a, 0x63, 0x70)),
            FontSize = 14,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(32, 22, 32, 0),
        };
        Grid.SetRow(header, 0);

        var verbatim = new TextBlock
        {
            Text = "VERBATIM\nDer Vorstand hat beschlossen, die Sitzung aufgrund der anhaltenden "
                 + "technischen Schwierigkeiten auf den folgenden Werktag zu verschieben.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0x93, 0x9f)),
            FontSize = 21,
            LineHeight = 30,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(32, 20, 32, 8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(verbatim, 1);

        var simplified = new TextBlock
        {
            Text = "EINFACHE SPRACHE\nDie Sitzung ist heute nicht. Es gibt technische Probleme. "
                 + "Die Sitzung ist morgen.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xf2, 0xf4, 0xf7)),
            FontSize = 31,
            FontWeight = FontWeights.SemiBold,
            LineHeight = 42,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(32, 8, 32, 24),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(simplified, 2);

        root.Children.Add(header);
        root.Children.Add(verbatim);
        root.Children.Add(simplified);
        return root;
    }
}
