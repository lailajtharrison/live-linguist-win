using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LiveLinguistWinUI.ViewModels;

/// One line of the transcript: what was said, and its easy-French caption (which streams
/// in while the simplifier writes it). The newest line is shown large; older ones smaller
/// and dimmer, so the eye goes to what is being said now.
public sealed class CaptionEntry : INotifyPropertyChanged
{
    private string _simplified = "";
    private bool _isLatest = true;
    private bool _showOriginal;

    public CaptionEntry(string original, bool showOriginal)
    {
        Original = original;
        _showOriginal = showOriginal;
    }

    public DateTime Time { get; } = DateTime.Now;
    public string TimeText => Time.ToString("HH:mm");
    public string Original { get; }

    /// Until the simplifier's first words arrive, show "…" so the line is visibly working.
    public string Simplified
    {
        get => _simplified;
        set { Set(ref _simplified, value); Notify(nameof(CaptionText)); }
    }
    public string CaptionText => _simplified.Length > 0 ? _simplified : "…";

    public bool IsLatest
    {
        get => _isLatest;
        set
        {
            Set(ref _isLatest, value);
            Notify(nameof(CaptionSize)); Notify(nameof(CaptionOpacity)); Notify(nameof(CaptionWeight));
        }
    }
    public double CaptionSize => _isLatest ? 30 : 21;
    public double CaptionOpacity => _isLatest ? 1.0 : 0.72;
    public Windows.UI.Text.FontWeight CaptionWeight =>
        _isLatest ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public bool ShowOriginal { get => _showOriginal; set => Set(ref _showOriginal, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        field = value;
        Notify(name);
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
