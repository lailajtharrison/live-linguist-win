using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

namespace LiveLinguistWinUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // Interface text is in English so any student can operate the app; the captions
    // themselves stay French, since easy French is what they are there to read.
    // Nothing is shown in the caption cards until French is actually heard: the window
    // stays empty apart from a short "listening" message (Waiting*).
    private string _verbatim = "";
    private string _simplified = "";
    private bool _hasSpeech;
    private string _waitingTitle = "Getting ready…";
    private string _waitingDetail = "Loading the language model. This takes a few seconds.";
    private string _mode = "Starting…";
    // The notice under the captions, shown only when there is something to act on.
    private string _hint = "";
    private string _hintTitle = "";
    private InfoBarSeverity _hintSeverity = InfoBarSeverity.Informational;

    // Measured round-trip for the last phrase. Em dash until there is a real
    // measurement — the old status bar hard-coded "~1,8 s", which was wrong on
    // any machine slower than the one it was written on.
    private string _latency = "Latency —";

    // Caption box: the caption before the current one, shown dimmer above it so a reader
    // who looked away for a second can catch up.
    private string _previousSimplified = "";
    private bool _showOriginal;
    // Set when the meeting/video source has heard nothing for a while (empty = fine).
    private string _audioWarning = "";

    public string Verbatim { get => _verbatim; set => Set(ref _verbatim, value); }
    public string Simplified { get => _simplified; set => Set(ref _simplified, value); }
    // False until the first French is heard; the caption cards appear then.
    public bool HasSpeech
    {
        get => _hasSpeech;
        set { Set(ref _hasSpeech, value); Notify(nameof(IsWaiting)); }
    }
    public bool IsWaiting => !_hasSpeech;
    public string WaitingTitle { get => _waitingTitle; set => Set(ref _waitingTitle, value); }
    public string WaitingDetail { get => _waitingDetail; set => Set(ref _waitingDetail, value); }
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
    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            Set(ref _showOriginal, value);
            foreach (var e in Entries) e.ShowOriginal = value;
        }
    }

    // The transcript: every caption since the app started, oldest first.
    public ObservableCollection<CaptionEntry> Entries { get; } = new();

    // What is being heard right now, before it becomes a transcript line.
    public string LiveText
    {
        get => _liveText;
        set { Set(ref _liveText, value); Notify(nameof(HasLiveText)); }
    }
    public bool HasLiveText => _liveText.Length > 0;
    private string _liveText = "";
    public string AudioWarning
    {
        get => _audioWarning;
        set { Set(ref _audioWarning, value); Notify(nameof(HasAudioWarning)); }
    }
    public bool HasAudioWarning => _audioWarning.Length > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        Notify(name);
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
