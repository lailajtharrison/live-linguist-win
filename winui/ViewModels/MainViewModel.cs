using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveLinguistWinUI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // Demo defaults (also what CI screenshots): real 1.7B output on the potter clip.
    private string _verbatim =
        "euh du coup le tour de potier faut centrer la motte d'argile avant tout sinon ben ça part dans tous les sens et là on mouille les mains et on appuie fort vers le centre";
    private string _simplified =
        "Avant de faire le tour de potier, centrez la motte d'argile. Sinon, l'argile part dans tous les sens. Ensuite, mouillez vos mains. Appuyez fort vers le centre.";
    private string _mode = "En direct";

    public string Verbatim { get => _verbatim; set => Set(ref _verbatim, value); }
    public string Simplified { get => _simplified; set => Set(ref _simplified, value); }
    public string Mode { get => _mode; set => Set(ref _mode, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
