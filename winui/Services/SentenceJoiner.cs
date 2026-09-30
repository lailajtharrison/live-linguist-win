using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LiveLinguistWinUI.Services;

/// Decides when transcribed text is ready to simplify. Utterances are cut at 5 s to keep
/// captions quick, which can split a sentence ("il existe des | dizaines de jungles"),
/// and the simplifier turns the fragment into a wrong caption ("Il existe des.").
///
/// When Whisper ends a cut utterance without any end punctuation, the sentence really is
/// unfinished, so that tail is held back and joined to the next utterance. When Whisper
/// does end it with a period, the text is released as it is. Measured on two news clips
/// (see the commit message): Whisper puts a period at the cut about as often mid-sentence as
/// not, and guessing past it ("next starts in lower case, so join") glued separate
/// sentences together and once sent the 0.6B model into a runaway "1000000…" caption.
///
/// A new speaker (Whisper's leading "-"), a real pause, or MaxHeldCuts cuts release what
/// is held, which bounds the extra wait to one more utterance (about 4 s).
public sealed class SentenceJoiner
{
    public int MaxHeldCuts { get; init; } = 1;

    private string _pending = "";
    private int _heldCuts;

    /// Text heard but not yet released (shown as the live line while held).
    public string Pending => _pending;

    /// Add one utterance's transcript. `endedAtPause`: the speaker paused (or the audio
    /// stopped), as opposed to the utterance being cut at the length limit. Returns the
    /// text to simplify now: none, one or two captions, oldest first.
    public List<string> Add(string text, bool endedAtPause)
    {
        var ready = new List<string>();
        text = Clean(text);
        if (_pending.Length > 0 && text.StartsWith('-')) ReleaseInto(ready, _pending);

        var all = Join(_pending, text);
        if (endedAtPause || _heldCuts >= MaxHeldCuts || EndsSentence(all))
        {
            ReleaseInto(ready, all);
            return ready;
        }

        // Cut mid-sentence: release the complete sentences, hold the unfinished tail.
        int end = LastSentenceEnd(all);
        var head = end < 0 ? "" : all[..(end + 1)].Trim();
        _pending = end < 0 ? all : all[(end + 1)..].Trim();
        _heldCuts++;
        if (head.Length > 0) ready.Add(head);
        return ready;
    }

    /// Release whatever is held (the audio has gone quiet).
    public List<string> Flush()
    {
        var ready = new List<string>();
        ReleaseInto(ready, _pending);
        return ready;
    }

    public static string Join(string a, string b) =>
        a.Length == 0 ? b.Trim() : b.Trim().Length == 0 ? a : a + " " + b.Trim();

    // Whisper's sound tags are not speech: "[Musique]", "*Musique*", "♪ … ♪".
    private static readonly Regex SoundTags = new(@"\[[^\]]*\]|\*[^*]*\*|♪[^♪]*♪|♪", RegexOptions.Compiled);

    public static string Clean(string text) =>
        Regex.Replace(SoundTags.Replace(text, " "), @"\s+", " ").Trim();

    private void ReleaseInto(List<string> ready, string text)
    {
        _pending = "";
        _heldCuts = 0;
        text = text.Trim();
        if (text.Trim('.', '!', '?', '-', '…', ' ').Length > 0) ready.Add(text);
    }

    // Ends with '.', '!' or '?' (an ellipsis is a trailing-off, not an end).
    private static bool EndsSentence(string s)
    {
        s = s.TrimEnd();
        return s.Length > 0 && s[^1] is '.' or '!' or '?' && !s.EndsWith("..");
    }

    // Index of the last '.', '!' or '?' followed by a space, or -1.
    private static int LastSentenceEnd(string s)
    {
        for (int i = s.Length - 2; i >= 0; i--)
        {
            char c = s[i];
            if (c is not ('.' or '!' or '?')) continue;
            if (!char.IsWhiteSpace(s[i + 1])) continue;
            if (c == '.' && i > 0 && s[i - 1] == '.') continue;
            return i;
        }
        return -1;
    }
}
