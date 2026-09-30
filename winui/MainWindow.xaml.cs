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
    private AudioSource _source = AudioSource.Microphone;   // what is running
    private AudioSource _wanted = AudioSource.SystemPlayback; // what the student picked
    private bool _ready;   // suppress the RadioButton's initial Checked during init

    // One phrase is simplified at a time, in arrival order, so streamed captions never
    // interleave and the previous-caption line stays in sequence.
    private readonly SemaphoreSlim _simplifyGate = new(1, 1);
    private string _lastCaption = "";

    private DispatcherQueueTimer? _silenceTimer;
    private readonly TranscriptLog _transcript = new();
    private bool _followLatest = true;   // the transcript scrolls with new lines
    private bool _captionMode;
    private RectInt32 _fullWindowRect;

    public MainWindow()
    {
        this.InitializeComponent();
        Title = "Live Linguist";
        // A comfortable first size, in physical pixels for this screen's scaling.
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1120 * scale), (int)(820 * scale)));

        // Open on the source the student chose last time (the welcome screen sets it).
        AppSettings.Load();
        _wanted = AppSettings.Source;
        (_wanted == AudioSource.SystemPlayback ? SourceLoopback : SourceMic).IsChecked = true;

        RootGrid.Loaded += OnRootLoaded;
        Closed += (_, _) => { if (_captionMode) SaveCaptionRect(); };
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        // First launch: the welcome screen. SCREENSHOT_VIEW=main skips it for CI.
        if (!AppSettings.Onboarded && Environment.GetEnvironmentVariable("SCREENSHOT_VIEW") != "main")
            ShowOnboarding(true);

        // CI/screenshot mode: render the first frame to PNG and exit.
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

    // Load the LLM once (off the UI thread, so the welcome screen stays usable), then
    // start the selected audio source.
    private async Task GoLiveAsync()
    {
        var fast = FindModel(FastModelName);
        var modelPath = fast ?? FindModel(LegacyModelName);
        if (modelPath != null)
        {
            try
            {
                _llm = await Task.Run(() => LlamaSimplifier.Load(modelPath, useWorkedExamples: fast == null));
            }
            catch { _llm = null; }
        }

        if (_llm == null)
        {
            Dispatch(() =>
            {
                ViewModel.Mode = "Model missing";
                SetWaiting("Not listening", "");
                SetHint(InfoBarSeverity.Error, "The language model was not found.", "Please reinstall the app.");
            });
            _ready = true;
            return;
        }

        await StartSourceAsync(_wanted);
        _ready = true;
        // The student may have picked a source on the welcome screen while this was starting.
        if (_wanted != _source) await StartSourceAsync(_wanted);

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
        _speech.Hypothesis += text => Dispatch(() => ShowHearing(text));
        _speech.Phrase += text => _ = OnPhraseAsync(text);

        var ok = await _speech.StartAsync();
        Dispatch(() =>
        {
            ViewModel.Hint = "";
            if (ok && source == AudioSource.SystemPlayback)
            {
                ViewModel.Mode = "Live · Call or video";
                SetWaiting("Listening to this computer's sound",
                           "Start your call or video. Captions appear here as soon as someone speaks French.");
            }
            else if (ok)
            {
                ViewModel.Mode = "Live · Microphone";
                SetWaiting("Listening to the microphone",
                           "Captions appear here as soon as someone speaks French.");
            }
            else if (source == AudioSource.SystemPlayback)
            {
                ViewModel.Mode = "Audio unavailable";
                SetWaiting("Not listening", "");
                SetHint(InfoBarSeverity.Warning, "Can't listen to the computer's sound.",
                        "The speech model “ggml-small-q5_1.bin” is missing. Please reinstall the app.");
            }
            else
            {
                ViewModel.Mode = "Microphone off";
                SetWaiting("Not listening", "");
                SetHint(InfoBarSeverity.Informational, "The microphone needs the French speech pack.",
                        "Windows Settings → Time & language → Speech → add “French (France)”, then reopen " +
                        "the app. For a call or video, choose “Call or video” instead: it needs no setup.");
            }
        });
    }

    // User flipped the call-or-video / microphone choice.
    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        _wanted = (sender == SourceLoopback) ? AudioSource.SystemPlayback : AudioSource.Microphone;
        if (!_ready || _llm == null) return;   // GoLiveAsync starts _wanted once it is ready
        AppSettings.Save(AppSettings.Onboarded, _wanted);
        if (_wanted == _source) return;
        _ = StartSourceAsync(_wanted);
    }

    // ---- Welcome screen ----------------------------------------------------------

    private void OnHelpClick(object sender, RoutedEventArgs e) => ShowOnboarding(true);

    // A choice on the welcome screen: remember it, select it, and go to the captions.
    private void OnOnboardingChoice(object sender, RoutedEventArgs e)
    {
        var source = sender == ChooseMic ? AudioSource.Microphone : AudioSource.SystemPlayback;
        AppSettings.Save(true, source);
        // Checking the radio button runs OnSourceChanged, which switches the audio over.
        (source == AudioSource.SystemPlayback ? SourceLoopback : SourceMic).IsChecked = true;
        ShowOnboarding(false);
    }

    private void ShowOnboarding(bool show)
    {
        OnboardingView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MainView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetWaiting(string title, string detail)
    {
        ViewModel.WaitingTitle = title;
        ViewModel.WaitingDetail = detail;
    }

    // Words still being heard (a partial transcript): the grey live line under the
    // transcript. From the first words on, the transcript replaces the "listening" message.
    private void ShowHearing(string text)
    {
        ViewModel.Verbatim = text;
        ViewModel.LiveText = text;
        ViewModel.HasSpeech = true;
        ScrollToLatestIfFollowing();
    }

    // A finalized phrase: add it to the transcript, then simplify it, streaming the caption
    // into its line as it is generated so the reader is not left waiting for the rewrite.
    private async Task OnPhraseAsync(string text)
    {
        // Latency is what the student waits: from the end of the speech to the caption.
        // For call/video audio that includes Whisper's transcription, so start the clock
        // when the utterance ended. (Read before the first await: the transcriber is
        // still on this phrase.)
        var spokenEndUtc = _speech is LoopbackTranscriber lt ? lt.LastUtteranceEndUtc : DateTime.UtcNow;
        var entry = new CaptionEntry(text, ViewModel.ShowOriginal);
        Dispatch(() => AddEntry(entry));
        if (_llm == null) { Dispatch(() => FinishEntry(entry, text)); return; }
        await _simplifyGate.WaitAsync();
        try
        {
            bool started = false;
            void Show(string caption) => Dispatch(() =>
            {
                if (!started)
                {
                    started = true;
                    ViewModel.PreviousSimplified = _lastCaption;
                }
                ViewModel.Simplified = caption;
                entry.Simplified = caption;
                ScrollToLatestIfFollowing();
            });

            // null = the model answered in English; showing the French that was actually
            // said is always better than a wrong-language caption.
            var simple = await _llm.SimplifyAsync(Prompts.FrenchFalc, text, Show) ?? text;
            var latency = FormatLatency(DateTime.UtcNow - spokenEndUtc);
            if (string.IsNullOrWhiteSpace(simple)) simple = text;
            Show(simple);
            Dispatch(() =>
            {
                _lastCaption = simple;
                ViewModel.Latency = latency;
                FinishEntry(entry, simple);
            });
        }
        catch
        {
            // Keep the words even when simplifying fails: the line shows what was said.
            Dispatch(() => FinishEntry(entry, text));
        }
        finally { _simplifyGate.Release(); }
    }

    // ---- Transcript ----------------------------------------------------------------

    private void AddEntry(CaptionEntry entry)
    {
        ViewModel.Verbatim = entry.Original;
        ViewModel.LiveText = "";
        ViewModel.HasSpeech = true;
        if (ViewModel.Entries.Count > 0) ViewModel.Entries[^1].IsLatest = false;
        ViewModel.Entries.Add(entry);
        SaveButton.IsEnabled = true;
        ScrollToLatestIfFollowing();
    }

    // The caption is final: show it and append it to the autosaved transcript.
    private void FinishEntry(CaptionEntry entry, string caption)
    {
        entry.Simplified = caption;
        _transcript.Append(entry);
        ScrollToLatestIfFollowing();
    }

    // Keep the newest line in view, unless the student has scrolled up to reread.
    private void ScrollToLatestIfFollowing()
    {
        if (!_followLatest) return;
        FeedScroll.UpdateLayout();
        FeedScroll.ChangeView(null, FeedScroll.ScrollableHeight, null, disableAnimation: true);
    }

    private void OnFeedViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        _followLatest = FeedScroll.VerticalOffset >= FeedScroll.ScrollableHeight - 48;
        JumpToLatestButton.Visibility = _followLatest ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnJumpToLatestClick(object sender, RoutedEventArgs e)
    {
        _followLatest = true;
        JumpToLatestButton.Visibility = Visibility.Collapsed;
        ScrollToLatestIfFollowing();
    }

    private void OnShowOriginalToggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowOriginal = ShowOriginalSwitch.IsOn;
        OriginalToggle.IsChecked = ShowOriginalSwitch.IsOn;
    }

    // "Save transcript": the whole session as a text file, wherever the student chooses.
    private async void OnSaveTranscriptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _transcript.SuggestedFileName,
            };
            picker.FileTypeChoices.Add("Text file", new System.Collections.Generic.List<string> { ".txt" });
            // Unpackaged app: the picker must be told which window it belongs to.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await File.WriteAllTextAsync(file.Path, _transcript.Export(ViewModel.Entries), System.Text.Encoding.UTF8);
            ShowBriefly(InfoBarSeverity.Success, "Transcript saved.", file.Path);
        }
        catch (Exception ex)
        {
            ShowBriefly(InfoBarSeverity.Error, "Could not save the transcript.", ex.Message);
        }
    }

    // A notice that clears itself after a few seconds (unless something replaced it).
    private void ShowBriefly(InfoBarSeverity severity, string title, string message)
    {
        SetHint(severity, title, message);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(6);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if (ViewModel.Hint == message) ViewModel.Hint = ""; };
        timer.Start();
    }

    // ---- Caption box -------------------------------------------------------------

    private void OnCaptionModeClick(object sender, RoutedEventArgs e) => EnterCaptionMode();

    private void OnExpandClick(object sender, RoutedEventArgs e) => ExitCaptionMode();

    // Same setting as the main window's "Show original" switch (which updates the view model).
    private void OnOriginalToggleClick(object sender, RoutedEventArgs e) =>
        ShowOriginalSwitch.IsOn = OriginalToggle.IsChecked == true;

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
        await rtb.RenderAsync(Shell);
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
