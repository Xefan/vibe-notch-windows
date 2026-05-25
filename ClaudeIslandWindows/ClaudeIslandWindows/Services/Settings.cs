using System.Text.Json;

namespace ClaudeIslandWindows.Services;

public sealed class BuddyBridgeSettings
{
    public bool Enabled { get; set; } = true;
    public string NamePrefix { get; set; } = "Claude";
    public bool AutoReconnect { get; set; } = true;
    public int HeartbeatKeepaliveSeconds { get; set; } = 10;
    /// Last-seen BLE address (uint64). 0 = none yet. We prefer this on reconnect.
    public ulong LastDeviceAddress { get; set; } = 0;
    public string? LastDeviceName { get; set; }
}

public sealed class Settings
{
    public string NotificationSound { get; set; } = "Ding";
    public BuddyBridgeSettings BuddyBridge { get; set; } = new();

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeIsland");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Current { get; private set; } = Load();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
