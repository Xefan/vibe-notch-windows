using System.Media;

namespace ClaudeIslandWindows.Services;

public static class SoundPlayer
{
    // Map friendly names to Windows Media files
    private static readonly Dictionary<string, string> SoundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"]        = "",
        ["Ding"]        = "Windows Ding.wav",
        ["Notify"]      = "Windows Notify.wav",
        ["Chimes"]      = "chimes.wav",
        ["Chord"]       = "chord.wav",
        ["Tada"]        = "tada.wav",
        ["Default"]     = "Windows Default.wav",
        ["Background"]  = "Windows Background.wav",
        ["Foreground"]  = "Windows Foreground.wav",
        ["Exclamation"] = "Windows Exclamation.wav",
        ["Logon"]       = "Windows Logon.wav",
        ["Balloon"]     = "Windows Balloon.wav",
        ["Ringin"]      = "Windows Ringin.wav",
        ["Ringout"]     = "Windows Ringout.wav",
    };

    public static IReadOnlyList<string> AvailableSounds => SoundMap.Keys.ToList();

    private static System.Media.SoundPlayer? _player;

    public static void Play(string name)
    {
        if (!SoundMap.TryGetValue(name, out var file) || string.IsNullOrEmpty(file))
            return;

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", file);
        if (!File.Exists(path)) return;

        try
        {
            _player?.Stop();
            _player = new System.Media.SoundPlayer(path);
            _player.Play();
        }
        catch { }
    }

    public static void PlayCurrent() => Play(Settings.Current.NotificationSound);
}
