using System;
using System.Threading.Tasks;

namespace LiveLinguistWinUI.Services;

/// A source of live French text: either the microphone (the speaker in the room)
/// or system loopback (what's playing through the speakers — a Teams partner or a
/// video). Both raise the same events so the UI/simplifier pipeline is identical.
public interface ISpeechSource : IDisposable
{
    /// Partial, still-changing text for the current utterance (may be empty for
    /// sources that only emit finalized phrases, e.g. Whisper).
    event Action<string>? Hypothesis;

    /// A finalized phrase, ready to simplify.
    event Action<string>? Phrase;

    /// Start capturing. Returns false if the source is unavailable
    /// (no device / model / recognizer) so the caller can fall back.
    Task<bool> StartAsync();

    /// Stop capturing but keep the instance usable for a later StartAsync.
    Task StopAsync();
}

/// Where the French audio is coming from.
public enum AudioSource
{
    /// The microphone — the person speaking in the room (in-person use).
    Microphone,

    /// System loopback — audio playing out of the speakers: a Teams/Zoom partner
    /// or a video. This is the exchange-call use case.
    SystemPlayback,
}
