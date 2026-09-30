using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LiveLinguistWinUI.Services;

/// Last line of defence between the model and the caption. The 0.6B model is right far
/// more often than the 1.7B it replaces, but on the 2026-09-30 evaluation it failed in
/// two ways a student must never see: it answered in English once ("Incorporate the snow
/// white with a spatule…") and it looped once ("Ne vous arrêtez pas." ×4). Both are
/// cheap to detect, and both can be caught while the caption is still streaming.
public static class OutputGuard
{
    // Frequent function words: common in any English sentence, rare in French captions.
    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "with", "you", "your", "is", "are", "this", "that", "from", "don't",
        "please", "will", "it's", "of", "to", "in", "for", "be", "not", "it", "they", "we",
    };

    private static readonly HashSet<string> French = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "un", "une", "des", "de", "du", "et", "est", "vous", "nous", "il",
        "elle", "on", "je", "tu", "pas", "que", "qui", "pour", "dans", "sur", "avec", "au",
        "aux", "ce", "cette", "ils", "elles", "sont", "a", "en", "ne", "se", "son", "sa",
    };

    // Letters and digits: "Elle ouvre de 9h à 12h." and "Elle ouvre de 14h à 18h." are
    // different sentences, and comparing letters alone would call the second a repeat.
    private static readonly Regex Word = new(@"[\p{L}\p{N}']+", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…])\s+", RegexOptions.Compiled);

    /// True when the text reads as English rather than French. Needs a few words of
    /// evidence, so a lone borrowed word ("le week-end", "un e-mail") never trips it.
    public static bool LooksEnglish(string text)
    {
        int en = 0, fr = 0;
        foreach (Match m in Word.Matches(text))
        {
            if (English.Contains(m.Value)) en++;
            else if (French.Contains(m.Value)) fr++;
        }
        return en >= 2 && en > fr;
    }

    /// The sentences of a caption, trimmed; the last one may still be incomplete.
    public static List<string> Sentences(string text) =>
        SentenceEnd.Split(text.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// True once a complete sentence repeats an earlier one: the model is looping and
    /// generation should stop. Only finished sentences count, so a sentence that is
    /// still arriving is never judged on its first few words.
    public static bool IsLooping(string text)
    {
        var sentences = Sentences(text);
        bool lastComplete = text.TrimEnd().Length > 0 && ".!?…".Contains(text.TrimEnd()[^1]);
        var complete = lastComplete ? sentences : sentences.Take(sentences.Count - 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in complete)
            if (!seen.Add(Normalize(s))) return true;
        return false;
    }

    // A character repeated 8+ times, or a "word" longer than any French word: the model
    // running away inside a sentence, which IsLooping (whole sentences) cannot see. Seen on
    // a run-on input: "Une réparation de 1000000000000000000000…".
    private static readonly Regex Runaway = new(@"(\S)\1{7,}|\S{40,}", RegexOptions.Compiled);

    public static bool IsRunaway(string text) => Runaway.IsMatch(text);

    /// Cut a caption back to its last complete sentence before a runaway.
    public static string CutRunaway(string text)
    {
        var m = Runaway.Match(text);
        if (!m.Success) return text;
        var head = text[..m.Index];
        int end = head.LastIndexOfAny(new[] { '.', '!', '?' });
        return (end >= 0 ? head[..(end + 1)] : head).Trim();
    }

    /// Final cleanup of a finished caption: drop repeated sentences, keep the first of each.
    public static string RemoveRepeats(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = Sentences(text).Where(s => seen.Add(Normalize(s)));
        return string.Join(" ", kept);
    }

    private static string Normalize(string s) =>
        string.Join(" ", Word.Matches(s).Select(m => m.Value.ToLowerInvariant()));
}
