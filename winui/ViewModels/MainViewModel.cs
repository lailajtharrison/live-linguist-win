using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveLinguistWinUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // First-run state: guidance, not a confusing pre-filled sample.
    private string _verbatim = "Votre texte apparaîtra ici.";
    private string _simplified = "La version simple apparaîtra ici.";
    private string _mode = "Micro éteint";
    // The mic hint. Default = the "mic off" instructions (also what CI screenshots).
    private string _hint =
        "🎤  Le micro n'est pas activé. Écrivez ci-dessous pour essayer tout de suite.\n" +
        "Pour parler en français : Paramètres Windows → Heure et langue → Voix → ajouter « Français (France) », puis rouvrez l'application.";

    // Measured round-trip for the last phrase. Em dash until there is a real
    // measurement — the old status bar hard-coded "~1,8 s", which was wrong on
    // any machine slower than the one it was written on.
    private string _latency = "Latence —";

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
    public string StatusText => $"Sur l'appareil · {_modelLabel} · rien ne quitte ce PC";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
