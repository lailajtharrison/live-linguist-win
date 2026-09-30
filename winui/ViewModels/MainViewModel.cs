using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveLinguistWinUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // First-run state: guidance, not a confusing pre-filled sample.
    // Interface text is in English so any student can operate the app; the captions
    // themselves stay French, since easy French is what they are there to read.
    private string _verbatim = "The French you hear will appear here.";
    private string _simplified = "The easy-French version will appear here.";
    private string _mode = "Microphone off";
    // The mic hint. Default = the "mic off" instructions (also what CI screenshots).
    private string _hint =
        "🎤  The microphone is not on. Type below to try it right away.\n" +
        "To speak French: Windows Settings → Time & language → Speech → add “French (France)”, then reopen the app.";

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
    public string Mode { get => _mode; set => Set(ref _mode, value); }
    public string Hint { get => _hint; set => Set(ref _hint, value); }
    public string Latency { get => _latency; set => Set(ref _latency, value); }
    public string PreviousSimplified { get => _previousSimplified; set => Set(ref _previousSimplified, value); }
    public bool ShowOriginal { get => _showOriginal; set => Set(ref _showOriginal, value); }
    public string ModelLabel
    {
        get => _modelLabel;
        set { Set(ref _modelLabel, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); }
    }
    public string AudioWarning
    {
        get => _audioWarning;
        set { Set(ref _audioWarning, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAudioWarning))); }
    }
    public bool HasAudioWarning => _audioWarning.Length > 0;
    public string StatusText => $"On this device · {_modelLabel} · nothing leaves this PC";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
