namespace AstraSkins.Models;

public sealed class GloveDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ushort ItemDefinitionIndex { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
    public List<CosmeticEntry> Skins { get; set; } = new();
}
