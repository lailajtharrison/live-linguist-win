using System;
using System.IO;
using System.Text.Json;

namespace LiveLinguistWinUI.Services;

/// Whether the student has been through the welcome screen, and which audio source they
/// chose, so the app opens ready to caption. Same storage as CaptionWindowSettings: a
/// plain JSON file under %LOCALAPPDATA%\LiveLinguist (the app is unpackaged).
public static class AppSettings
{
    private sealed record Saved(bool Onboarded, AudioSource Source);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveLinguist", "settings.json");

    public static bool Onboarded { get; private set; }
    public static AudioSource Source { get; private set; } = AudioSource.SystemPlayback;

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath));
            if (s == null) return;
            Onboarded = s.Onboarded;
            Source = s.Source;
        }
        catch { } // a corrupt file just means the welcome screen shows again
    }

    public static void Save(bool onboarded, AudioSource source)
    {
        Onboarded = onboarded;
        Source = source;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(onboarded, source)));
        }
        catch { /* not being able to remember is not worth an error */ }
    }
}
