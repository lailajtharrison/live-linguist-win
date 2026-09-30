using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

namespace LiveLinguistWinUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // First-run state: guidance, not a confusing pre-filled sample.
    // Interface text is in English so any student can operate the app; the captions
    // themselves stay French, since easy French is what they are there to read.
    private string _verbatim = "The French you hear will appear here.";
    private string _simplified = "The easy-French version will appear here.";
    private string _mode = "Microphone off";
    // The notice above the input box, shown only when there is something to act on.
    // Default = the "mic off" instructions (also what CI screenshots).
    private string _hint =
        "To caption French speech: Windows Settings → Time & language → Speech → add “French (France)”, " +
        "then reopen the app. Or choose “Meeting / video” to caption a call or video.";
    private string _hintTitle = "The microphone is not on.";
    private InfoBarSeverity _hintSeverity = InfoBarSeverity.Informational;

    // Measured round-trip for the last phrase. Em dash until there is a real
    // measurement — the old status bar hard-coded "~1,8 s", which was wrong on
    // any machine slower than the one it was written on.
    private string _latency = "Latency —";

    // Caption box: the caption before the current one, shown dimmer above it so a reader
    // who looked away for a second can catch up.
    private string _previousSimplified = "";
    private bool _showOriginal;
    private string _modelLabel = "Qwen3";
    // Set when the meeting/video source has heard nothing for a while (empty = fine).
    private string _audioWarning = "";

    public string Verbatim { get => _verbatim; set => Set(ref _verbatim, value); }
    public string Simplified { get => _simplified; set => Set(ref _simplified, value); }
    public string Mode
    {
        get => _mode;
        set { Set(ref _mode, value); Notify(nameof(IsLive)); Notify(nameof(IsNotLive)); }
    }
    // Drives the status dot: green while listening, grey otherwise.
    public bool IsLive => _mode.StartsWith("Live");
    public bool IsNotLive => !IsLive;
    public string Hint
    {
        get => _hint;
        set { Set(ref _hint, value); Notify(nameof(HasHint)); }
    }
    public bool HasHint => _hint.Length > 0;
    public string HintTitle { get => _hintTitle; set => Set(ref _hintTitle, value); }
    public InfoBarSeverity HintSeverity { get => _hintSeverity; set => Set(ref _hintSeverity, value); }
    public string Latency { get => _latency; set => Set(ref _latency, value); }
    public string PreviousSimplified { get => _previousSimplified; set => Set(ref _previousSimplified, value); }
    public bool ShowOriginal { get => _showOriginal; set => Set(ref _showOriginal, value); }
    public string ModelLabel
    {
        get => _modelLabel;
        set { Set(ref _modelLabel, value); Notify(nameof(StatusText)); }
    }
    public string AudioWarning
    {
        get => _audioWarning;
        set { Set(ref _audioWarning, value); Notify(nameof(HasAudioWarning)); }
    }
    public bool HasAudioWarning => _audioWarning.Length > 0;
    public string StatusText => $"On this device · {_modelLabel} · nothing leaves this PC";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        Notify(name);
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
