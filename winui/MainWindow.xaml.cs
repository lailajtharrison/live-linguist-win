using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using LiveLinguistWinUI.Services;
using LiveLinguistWinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LiveLinguistWinUI;

public sealed partial class MainWindow : Window
{
    // The fine-tuned 0.6B (v0.8): about 3x faster than the 1.7B on a laptop CPU, trained on
    // the bare prompt. The 1.7B is still found if it is all that is installed.
    private const string FastModelName = "ll-fr-0.6b-v2-Q4_K_M.gguf";
    private const string LegacyModelName = "qwen3-1.7b-easylang-fr-Q4_K_M.gguf";

    // How long the meeting/video source may hear nothing before the student is told.
    private static readonly TimeSpan SilenceWarningAfter = TimeSpan.FromSeconds(15);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainViewModel ViewModel { get; } = new();

    private ISpeechSource? _speech;
    private LlamaSimplifier? _llm;
    private AudioSource _source = AudioSource.Microphone;
    private bool _ready;   // suppress the RadioButton's initial Checked during init

    // One phrase is simplified at a time, in arrival order, so streamed captions never
    // interleave and the previous-caption line stays in sequence.
    private readonly SemaphoreSlim _simplifyGate = new(1, 1);
    private string _lastCaption = "";

    private DispatcherQueueTimer? _silenceTimer;
    private bool _captionMode;
    private RectInt32 _fullWindowRect;

    public MainWindow()
    {
        this.InitializeComponent();
        Title = "Live Linguist";
        // A comfortable first size, in physical pixels for this screen's scaling.
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1120 * scale), (int)(820 * scale)));
        RootGrid.Loaded += OnRootLoaded;
        Closed += (_, _) => { if (_captionMode) SaveCaptionRect(); };
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
        var fast = FindModel(FastModelName);
        var modelPath = fast ?? FindModel(LegacyModelName);
        if (modelPath != null)
        {
            try
            {
                _llm = LlamaSimplifier.Load(modelPath, useWorkedExamples: fast == null);
                ViewModel.ModelLabel = fast != null ? "Qwen3-0.6B" : "Qwen3-1.7B";
            }
            catch { _llm = null; }
        }

        if (_llm == null)
        {
            Dispatch(() =>
            {
                ViewModel.Mode = "Model missing";
                SetHint(InfoBarSeverity.Error, "The language model was not found.", "Please reinstall the app.");
            });
            _ready = true;
            return;
        }

        await StartSourceAsync(_source);
        _ready = true;

        _silenceTimer = DispatcherQueue.CreateTimer();
        _silenceTimer.Interval = TimeSpan.FromSeconds(2);
        _silenceTimer.Tick += (_, _) => UpdateAudioWarning();
        _silenceTimer.Start();
    }

    // Tells the student when the meeting/video source is hearing nothing: most often the
    // call or video is muted, paused, or playing through headphones that are not the
    // Windows default output (loopback captures the default output only).
    private void UpdateAudioWarning()
    {
        ViewModel.AudioWarning =
            _speech is LoopbackTranscriber lt && DateTime.UtcNow - lt.LastSoundUtc > SilenceWarningAfter
                ? "No sound is reaching the app. Check that the meeting or video is playing, " +
                  "through the Windows default audio output."
                : "";
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
                ViewModel.Mode = "Live · Meeting / video";
                SetHint(InfoBarSeverity.Informational, "Tip:",
                        "“Caption box” keeps the captions on top of Teams, Zoom or a video.");
                ViewModel.Verbatim = "Waiting for sound from the meeting or video…";
            }
            else if (ok)
            {
                ViewModel.Mode = "Live · Microphone";
                ViewModel.Hint = "";   // nothing to act on; the cards already say "speak"
                ViewModel.Verbatim = "Listening… speak French.";
            }
            else if (source == AudioSource.SystemPlayback)
            {
                ViewModel.Mode = "Audio unavailable";
                SetHint(InfoBarSeverity.Warning, "Can't listen to the computer's sound.",
                        "The speech model “ggml-small-q5_1.bin” is missing, or no sound is playing.");
            }
            else
            {
                ViewModel.Mode = "Microphone off"; // keeps the default mic-off instructions
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

    // A finalized phrase: show it verbatim, then simplify it, streaming the caption in as
    // it is generated so the reader is not left waiting for the whole rewrite.
    private async Task OnPhraseAsync(string text)
    {
        Dispatch(() => ViewModel.Verbatim = text);
        if (_llm == null) return;
        await _simplifyGate.WaitAsync();
        try
        {
            var sw = Stopwatch.StartNew();
            bool started = false;
            void Show(string caption) => Dispatch(() =>
            {
                if (!started)
                {
                    started = true;
                    ViewModel.PreviousSimplified = _lastCaption;
                }
                ViewModel.Simplified = caption;
            });

            // null = the model answered in English; showing the French that was actually
            // said is always better than a wrong-language caption.
            var simple = await _llm.SimplifyAsync(Prompts.FrenchFalc, text, Show) ?? text;
            sw.Stop();
            var latency = FormatLatency(sw.Elapsed);
            if (!string.IsNullOrWhiteSpace(simple))
            {
                Show(simple);
                Dispatch(() =>
                {
                    _lastCaption = simple;
                    ViewModel.Latency = latency;
                });
            }
        }
        catch { /* skip a bad phrase rather than crash the caption loop */ }
        finally { _simplifyGate.Release(); }
    }

    // ---- Caption box -------------------------------------------------------------

    private void OnCaptionModeClick(object sender, RoutedEventArgs e) => EnterCaptionMode();

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExitCaptionMode();

    private void OnOriginalToggleClick(object sender, RoutedEventArgs e) =>
        ViewModel.ShowOriginal = OriginalToggle.IsChecked == true;

    // Shrink to a strip that stays on top of the call or video, where the student left it
    // last time, or else along the bottom of the screen.
    private void EnterCaptionMode()
    {
        if (_captionMode) return;
        _captionMode = true;
        _fullWindowRect = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y,
                                        AppWindow.Size.Width, AppWindow.Size.Height);
        RootGrid.Visibility = Visibility.Collapsed;
        CaptionView.Visibility = Visibility.Visible;
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = true;
        AppWindow.MoveAndResize(CaptionWindowSettings.Load() ?? DefaultCaptionRect());
    }

    private void ExitCaptionMode()
    {
        if (!_captionMode) return;
        SaveCaptionRect();
        _captionMode = false;
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = false;
        CaptionView.Visibility = Visibility.Collapsed;
        RootGrid.Visibility = Visibility.Visible;
        AppWindow.MoveAndResize(_fullWindowRect);
    }

    private void SaveCaptionRect() => CaptionWindowSettings.Save(AppWindow.Position, AppWindow.Size);

    // Bottom-centre of the current screen, above the taskbar: where film subtitles sit.
    private RectInt32 DefaultCaptionRect()
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = Shell.XamlRoot?.RasterizationScale ?? 1.0;
        int width = Math.Min((int)(1000 * scale), work.Width - (int)(40 * scale));
        int height = (int)(210 * scale);
        return new RectInt32(work.X + (work.Width - width) / 2,
                             work.Y + work.Height - height - (int)(24 * scale),
                             width, height);
    }

    // One decimal place, English formatting to match the interface: "Latency 3.2 s".
    private static string FormatLatency(TimeSpan elapsed) =>
        "Latency " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";


    private void Dispatch(Action action) => DispatcherQueue.TryEnqueue(() => action());

    private void SetHint(InfoBarSeverity severity, string title, string message)
    {
        ViewModel.HintSeverity = severity;
        ViewModel.HintTitle = title;
        ViewModel.Hint = message;
    }

    // Next to the exe first (what the installer does), then the per-user data folder
    // (what setup.ps1 populates). Null when the model is in neither.
    private static string? FindModel(string name)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside)) return beside;
        var data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLinguist", name);
        return File.Exists(data) ? data : null;
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
