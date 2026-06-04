namespace AstraSkins.Models;

public sealed class WeaponDefinition
{
    public string EntityName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<CosmeticEntry> Skins { get; set; } = new();
}
