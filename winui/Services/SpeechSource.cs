using System;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace LiveLinguistWinUI.Services;

/// On-device continuous French speech recognition (Windows built-in).
/// Fires Hypothesis (partial, live) and Phrase (finalized) events.
public sealed class SpeechSource : IDisposable
{
    private SpeechRecognizer? _recognizer;

    public event Action<string>? Hypothesis;
    public event Action<string>? Phrase;

    /// Returns false if the fr-FR recognizer or a mic is unavailable (→ demo mode).
    public async Task<bool> StartAsync()
    {
        try
        {
            _recognizer = new SpeechRecognizer(new Language("fr-FR"));
            var result = await _recognizer.CompileConstraintsAsync();
            if (result.Status != SpeechRecognitionResultStatus.Success)
                return false;

            _recognizer.HypothesisGenerated += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Hypothesis.Text))
                    Hypothesis?.Invoke(e.Hypothesis.Text);
            };
            _recognizer.ContinuousRecognitionSession.ResultGenerated += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result.Text))
                    Phrase?.Invoke(e.Result.Text);
            };

            await _recognizer.ContinuousRecognitionSession.StartAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { _recognizer?.Dispose(); } catch { /* best effort */ }
    }
}
