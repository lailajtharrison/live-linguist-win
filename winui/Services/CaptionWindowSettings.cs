using System;
using System.IO;
using System.Text.Json;
using Windows.Graphics;

namespace LiveLinguistWinUI.Services;

/// Remembers where the student left the caption box, so it reopens in the same spot.
/// A plain JSON file under %LOCALAPPDATA%\LiveLinguist: the app is unpackaged, so the
/// packaged-app settings store (ApplicationData) is not available.
public static class CaptionWindowSettings
{
    private sealed record Saved(int X, int Y, int Width, int Height);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLinguist", "caption-window.json");

    public static RectInt32? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (s == null || s.Width < 200 || s.Height < 80) return null;
            return new RectInt32(s.X, s.Y, s.Width, s.Height);
        }
        catch { return null; } // a corrupt file just means the default placement
    }

    public static void Save(PointInt32 position, SizeInt32 size)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(position.X, position.Y, size.Width, size.Height)));
        }
        catch { /* not being able to remember the position is not worth an error */ }
    }
}
