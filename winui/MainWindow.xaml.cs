using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LiveLinguistWinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        RootGrid.Loaded += OnRootLoaded;
    }

    // Once the UI has painted, capture it to a PNG and exit. This is the CI
    // de-risk: does WinUI 3 composition actually render + capture on a headless
    // GitHub Actions runner?
    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        // Let the compositor paint a few frames before capturing.
        await Task.Delay(1500);

        try
        {
            var path = Environment.GetEnvironmentVariable("SCREENSHOT_PATH")
                       ?? Path.Combine(Environment.CurrentDirectory, "screenshot.png");

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(RootGrid);
            var pixels = await rtb.GetPixelsAsync();

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth,
                (uint)rtb.PixelHeight,
                96, 96,
                pixels.ToArray());
            await encoder.FlushAsync();

            using (var fs = File.Create(path))
            {
                stream.Seek(0);
                await stream.AsStreamForRead().CopyToAsync(fs);
            }

            Console.WriteLine($"Wrote {path} ({rtb.PixelWidth}x{rtb.PixelHeight})");
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAPTURE FAILED: " + ex);
            Environment.ExitCode = 3;
        }
        finally
        {
            Application.Current.Exit();
        }
    }
}
