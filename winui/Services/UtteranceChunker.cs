using System;
using System.Collections.Generic;

namespace LiveLinguistWinUI.Services;

/// Cuts a stream of 16 kHz mono audio into utterances for Whisper: at a pause, or, when
/// no pause comes (music under the voice, fast talkers, a video), at the quietest moment
/// of the last stretch once the utterance reaches MaxMs. The remainder carries over into
/// the next utterance, so nothing is dropped and words are not cut in half.
///
/// A caption cannot appear before its utterance ends, so MaxMs sets the worst-case wait.
/// It used to be 12 s with a hard cut; on a YouTube clip most utterances ran the full
/// 12 s because the background never dropped below the speech threshold.
public sealed class UtteranceChunker
{
    public const int SampleRate = 16_000;
    public const int FrameSamples = 480;                         // 30 ms
    private const int FrameMs = FrameSamples * 1000 / SampleRate;

    public float SpeechRms { get; init; } = 0.006f;   // energy gate (normalized float)
    public int SilenceFlushMs { get; init; } = 500;   // a pause this long ends the utterance
    public int MaxMs { get; init; } = 5_000;          // no pause: split by this length
    public int SplitSearchMs { get; init; } = 1_500;  // …at the quietest frame in this last stretch
    public int MinMs { get; init; } = 400;            // ignore blips shorter than this

    private readonly List<float> _samples = new(SampleRate * 6);
    private readonly List<float> _frameRms = new();   // one per frame pushed, parallel to _samples
    private bool _hasSpeech;
    private int _trailingSilenceMs;

    /// True while an utterance is being collected.
    public bool HasSpeech => _hasSpeech;
    public int LengthMs => _samples.Count * 1000 / SampleRate;

    /// The utterance collected so far (for live partial transcripts).
    public float[] Snapshot() => _samples.ToArray();

    /// Feed one frame (FrameSamples long, except possibly the last). Returns a finished
    /// utterance, or null. `voiced` says whether this frame was above the speech gate.
    public float[]? Push(ReadOnlySpan<float> frame, out bool voiced)
    {
        float rms = Rms(frame);
        voiced = rms >= SpeechRms;
        if (voiced)
        {
            _hasSpeech = true;
            _trailingSilenceMs = 0;
            Append(frame, rms);
        }
        else if (_hasSpeech)
        {
            _trailingSilenceMs += FrameMs;
            Append(frame, rms);   // keep a little tail
        }
        if (!_hasSpeech) return null;

        if (_trailingSilenceMs >= SilenceFlushMs)
        {
            var done = _samples.Count * 1000 / SampleRate >= MinMs ? _samples.ToArray() : null;
            Reset();
            return done;
        }
        return LengthMs >= MaxMs ? SplitAtQuietest() : null;
    }

    private float[] SplitAtQuietest()
    {
        int frames = _frameRms.Count;
        int searchFrames = Math.Min(frames - 1, SplitSearchMs / FrameMs);
        int best = frames - 1;
        for (int i = frames - searchFrames; i < frames; i++)
            if (_frameRms[i] < _frameRms[best]) best = i;

        // Cut just after the quietest frame; the rest starts the next utterance.
        int cutFrames = best + 1;
        int cutSamples = Math.Min(_samples.Count, cutFrames * FrameSamples);
        var head = _samples.GetRange(0, cutSamples).ToArray();
        _samples.RemoveRange(0, cutSamples);
        _frameRms.RemoveRange(0, Math.Min(cutFrames, _frameRms.Count));
        _trailingSilenceMs = 0;
        _hasSpeech = _samples.Count > 0;
        return head;
    }

    public void Reset()
    {
        _samples.Clear();
        _frameRms.Clear();
        _hasSpeech = false;
        _trailingSilenceMs = 0;
    }

    private void Append(ReadOnlySpan<float> frame, float rms)
    {
        foreach (var s in frame) _samples.Add(s);
        _frameRms.Add(rms);
    }

    private static float Rms(ReadOnlySpan<float> buf)
    {
        double sum = 0;
        foreach (var s in buf) sum += s * s;
        return buf.Length == 0 ? 0f : (float)Math.Sqrt(sum / buf.Length);
    }
}
