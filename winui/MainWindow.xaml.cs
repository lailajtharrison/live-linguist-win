using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using LiveLinguistWinUI.Services;
using LiveLinguistWinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LiveLinguistWinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private SpeechSource? _speech;
    private LlamaSimplifier? _llm;

    public MainWindow()
    {
        this.InitializeComponent();
        RootGrid.Loaded += OnRootLoaded;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        // CI/screenshot mode: render the demo frame to PNG and exit.
        var shot = Environment.GetEnvironmentVariable("SCREENSHOT_PATH");
        if (!string.IsNullOrEmpty(shot))
        {
            await Task.Delay(1200);
            await CaptureAsync(shot);
            Application.Current.Exit();
            return;
        }

        await GoLiveAsync();
    }

    // Wire mic -> simplifier -> UI. Degrades to demo mode if either is missing.
    private async Task GoLiveAsync()
    {
        var modelPath = ModelPath();
        if (File.Exists(modelPath))
        {
            try { _llm = LlamaSimplifier.Load(modelPath); } catch { _llm = null; }
        }

        _speech = new SpeechSource();
        _speech.Hypothesis += text => Dispatch(() => ViewModel.Verbatim = text);
        _speech.Phrase += text => _ = OnPhraseAsync(text);
        var micOk = await _speech.StartAsync();

        Dispatch(() => ViewModel.Mode = (micOk && _llm != null) ? "En direct" : "Mode démo");
    }

    // A finalized phrase: show it verbatim, then simplify in the background (burst).
    private async Task OnPhraseAsync(string text)
    {
        Dispatch(() => ViewModel.Verbatim = text);
        if (_llm == null) return;
        try
        {
            var simple = await _llm.SimplifyAsync(Prompts.FrenchFalc, text);
            if (!string.IsNullOrWhiteSpace(simple))
                Dispatch(() => ViewModel.Simplified = simple);
        }
        catch { /* skip a bad phrase rather than crash the caption loop */ }
    }

    // Type-to-test: simplify whatever is typed (works without a mic/speech pack).
    private void OnSimplifyClick(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text;
        if (!string.IsNullOrWhiteSpace(text)) _ = OnPhraseAsync(text);
    }

    private void Dispatch(Action action) => DispatcherQueue.TryEnqueue(() => action());

    private static string ModelPath()
    {
        const string name = "qwen3-1.7b-easylang-fr-Q4_K_M.gguf";
        // Easiest for the user: drop the model right next to the exe.
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside)) return beside;
        // Fallback: the per-user data folder (what setup.ps1 populates).
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLinguist", name);
    }

    private async Task CaptureAsync(string path)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(RootGrid);
        var pixels = await rtb.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();

        using var fs = File.Create(path);
        stream.Seek(0);
        await stream.AsStreamForRead().CopyToAsync(fs);
    }
}
