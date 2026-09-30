using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiveLinguistWinUI.ViewModels;

namespace LiveLinguistWinUI.Services;

/// The session transcript on disk. Each finished caption is appended as it happens to
/// Documents\Live Linguist\Transcript <date> <time>.txt, so nothing is lost if the app is
/// closed or crashes; "Save transcript" writes the same text wherever the student wants.
public sealed class TranscriptLog
{
    private readonly DateTime _started = DateTime.Now;
    private string? _autosavePath;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Live Linguist");

    public string SuggestedFileName => $"Live Linguist transcript {_started:yyyy-MM-dd HH-mm}";

    /// Append one finished caption to the autosave file (created on the first caption).
    public void Append(CaptionEntry entry)
    {
        try
        {
            if (_autosavePath == null)
            {
                Directory.CreateDirectory(Folder);
                _autosavePath = Path.Combine(Folder, $"Transcript {_started:yyyy-MM-dd HH-mm}.txt");
                File.WriteAllText(_autosavePath, Header(_started, null), Encoding.UTF8);
            }
            File.AppendAllText(_autosavePath, Block(entry), Encoding.UTF8);
        }
        catch { /* a full disk or locked folder must not stop the captions */ }
    }

    /// The whole transcript as text, for "Save transcript".
    public string Export(IReadOnlyList<CaptionEntry> entries)
    {
        var sb = new StringBuilder(Header(_started, entries.Count > 0 ? entries[^1].Time : null));
        foreach (var e in entries.Where(e => e.Simplified.Length > 0)) sb.Append(Block(e));
        return sb.ToString();
    }

    // English date to match the interface, whatever the PC's regional format.
    private static string Header(DateTime start, DateTime? end) =>
        "Live Linguist transcript\r\n" +
        start.ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) +
        (end is DateTime e ? $" to {e:HH:mm}" : "") + "\r\n\r\n";

    private static string Block(CaptionEntry e) =>
        $"[{e.Time:HH:mm:ss}]\r\nEasy French: {e.Simplified}\r\nSaid:        {e.Original}\r\n\r\n";
}
