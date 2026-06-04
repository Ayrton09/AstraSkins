namespace AstraSkins.Models;

public sealed class CosmeticEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int PaintKit { get; set; }
    public int Seed { get; set; }
    public float Wear { get; set; }
    public ushort? ItemDefinitionIndex { get; set; }
    public string? CustomName { get; set; }
    public bool? LegacyModel { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
    public string? Rarity { get; set; }
    public string? Group { get; set; }
}
