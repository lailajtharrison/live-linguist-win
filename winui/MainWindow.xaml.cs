using System;
using System.Diagnostics;
using System.Globalization;
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

    private ISpeechSource? _speech;
    private LlamaSimplifier? _llm;
    private AudioSource _source = AudioSource.Microphone;
    private bool _ready;   // suppress the RadioButton's initial Checked during init

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

    // Load the LLM once, then start the selected audio source.
    private async Task GoLiveAsync()
    {
        var modelPath = ModelPath();
        if (File.Exists(modelPath))
        {
            try { _llm = LlamaSimplifier.Load(modelPath); } catch { _llm = null; }
        }

        if (_llm == null)
        {
            Dispatch(() =>
            {
                ViewModel.Mode = "Modèle manquant";
                ViewModel.Hint = "⚠️  Le modèle n'a pas été trouvé — réinstallez l'application.";
            });
            _ready = true;
            return;
        }

        await StartSourceAsync(_source);
        _ready = true;
    }

    // Start (or restart) capture from the chosen source, wiring it into the pipeline.
    private async Task StartSourceAsync(AudioSource source)
    {
        // tear down any current source first
        if (_speech != null)
        {
            try { await _speech.StopAsync(); } catch { }
            _speech.Dispose();
            _speech = null;
        }
        _source = source;

        _speech = source == AudioSource.SystemPlayback
            ? new LoopbackTranscriber(WhisperModelPath())
            : new SpeechSource();
        _speech.Hypothesis += text => Dispatch(() => ViewModel.Verbatim = text);
        _speech.Phrase += text => _ = OnPhraseAsync(text);

        var ok = await _speech.StartAsync();
        Dispatch(() =>
        {
            if (ok && source == AudioSource.SystemPlayback)
            {
                ViewModel.Mode = "En direct · Réunion/vidéo";
                ViewModel.Hint = "🔊  J'écoute le son de l'ordinateur (Teams, Zoom, vidéo). " +
                                 "Le français que vous entendez sera simplifié ci-dessus.";
                ViewModel.Verbatim = "En attente du son de la réunion ou de la vidéo…";
            }
            else if (ok)
            {
                ViewModel.Mode = "En direct · Micro";
                ViewModel.Hint = "🎤  Micro activé — parlez en français, ou écrivez ci-dessous.";
                ViewModel.Verbatim = "Parlez en français, ou écrivez ci-dessous.";
            }
            else if (source == AudioSource.SystemPlayback)
            {
                ViewModel.Mode = "Audio indisponible";
                ViewModel.Hint = "⚠️  Le modèle audio « ggml-small-q5_1.bin » est introuvable, " +
                                 "ou aucun son ne joue. Écrivez ci-dessous pour tester.";
            }
            else
            {
                ViewModel.Mode = "Micro éteint"; // keeps the default mic-off instructions
            }
        });
    }

    // User flipped the mic / speakers toggle.
    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _llm == null) return;
        var wanted = (sender == SourceLoopback) ? AudioSource.SystemPlayback : AudioSource.Microphone;
        if (wanted == _source) return;
        _ = StartSourceAsync(wanted);
    }

    // A finalized phrase: show it verbatim, then simplify in the background (burst).
    private async Task OnPhraseAsync(string text)
    {
        Dispatch(() => ViewModel.Verbatim = text);
        if (_llm == null) return;
        try
        {
            var sw = Stopwatch.StartNew();
            var simple = await _llm.SimplifyAsync(Prompts.FrenchFalc, text);
            sw.Stop();
            var latency = FormatLatency(sw.Elapsed);
            if (!string.IsNullOrWhiteSpace(simple))
                Dispatch(() =>
                {
                    ViewModel.Simplified = simple;
                    ViewModel.Latency = latency;
                });
        }
        catch { /* skip a bad phrase rather than crash the caption loop */ }
    }

    // French decimal comma, one place: "Latence 3,2 s".
    private static string FormatLatency(TimeSpan elapsed) =>
        "Latence " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.GetCultureInfo("fr-FR")) + " s";

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

    // Whisper STT model (for the "Réunion / vidéo" loopback source). Same lookup as
    // the LLM: next to the exe first, then the per-user data folder.
    private static string WhisperModelPath()
    {
        const string name = "ggml-small-q5_1.bin";
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside)) return beside;
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
