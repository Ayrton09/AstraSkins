namespace AstraSkins.Models;

public sealed class MusicKitDefinition
{
    public string Id { get; set; } = string.Empty;
    public int MusicKit { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameZh { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
}
