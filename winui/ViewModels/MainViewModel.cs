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

    public string Verbatim { get => _verbatim; set => Set(ref _verbatim, value); }
    public string Simplified { get => _simplified; set => Set(ref _simplified, value); }
    public string Mode { get => _mode; set => Set(ref _mode, value); }
    public string Hint { get => _hint; set => Set(ref _hint, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
